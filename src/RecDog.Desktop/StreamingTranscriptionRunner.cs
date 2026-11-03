using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace RecDog.Desktop;

public sealed record StreamingUpdate(string Text, bool IsFinal, string? State = null, string? Error = null, int? SentenceId = null);

public interface ILiveTranscription : IAsyncDisposable
{
    bool TrySendAudio(byte[] pcm16);
    ValueTask PauseAsync(CancellationToken cancellation);
    ValueTask ResumeAsync(CancellationToken cancellation);
    Task StopAsync(CancellationToken cancellation);
}

/// <summary>Owns the isolated sherpa-onnx streaming worker and forwards bounded PCM chunks.</summary>
public sealed class StreamingTranscriptionRunner : ILiveTranscription
{
    private readonly Process process;
    private readonly Channel<string> commands = Channel.CreateBounded<string>(new BoundedChannelOptions(32) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = false });
    private readonly Action<StreamingUpdate> update;
    private readonly CancellationTokenSource lifetime;
    private readonly Task writer;
    private readonly Task reader;
    private readonly Task stderrDrain;
    private int stopped;

    private StreamingTranscriptionRunner(Process process, Action<StreamingUpdate> update, CancellationTokenSource lifetime)
    {
        this.process = process; this.update = update; this.lifetime = lifetime;
        writer = Task.Run(WriteLoopAsync);
        reader = Task.Run(ReadLoopAsync);
        stderrDrain = Task.Run(DrainStandardErrorAsync);
    }

    public static async Task<StreamingTranscriptionRunner> StartAsync(string python, string model, string worker, Action<StreamingUpdate> update, CancellationToken cancellation)
    {
        var start = new ProcessStartInfo(python) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, StandardInputEncoding = new UTF8Encoding(false) };
        start.ArgumentList.Add("-u"); start.ArgumentList.Add(worker); start.ArgumentList.Add("--model"); start.ArgumentList.Add(model);
        start.Environment["HF_HUB_OFFLINE"] = "1"; start.Environment["TRANSFORMERS_OFFLINE"] = "1"; start.Environment["PYTHONIOENCODING"] = "utf-8";
        var process = new Process { StartInfo = start };
        if (!process.Start()) throw new InvalidOperationException("无法启动流式转写进程。");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var line = await process.StandardOutput.ReadLineAsync(timeout.Token);
            if (line is null) throw new InvalidOperationException("流式转写进程未返回 ready。");
            var root = Parse(line);
            if (root.GetProperty("type").GetString() != "ready")
                throw new InvalidOperationException(root.TryGetProperty("message", out var message) ? message.GetString() : "流式转写进程未就绪。");
            return new StreamingTranscriptionRunner(process, update, CancellationTokenSource.CreateLinkedTokenSource(cancellation));
        }
        catch { try { if (!process.HasExited) process.Kill(true); } catch { } process.Dispose(); throw; }
    }

    public bool TrySendAudio(byte[] pcm16)
    {
        if (pcm16.Length == 0 || Volatile.Read(ref stopped) != 0) return false;
        return commands.Writer.TryWrite(JsonSerializer.Serialize(new { type = "audio", pcm = Convert.ToBase64String(pcm16) }));
    }

    public ValueTask PauseAsync(CancellationToken cancellation) => SendControlAsync("pause", cancellation);
    public ValueTask ResumeAsync(CancellationToken cancellation) => SendControlAsync("resume", cancellation);

    private ValueTask SendControlAsync(string type, CancellationToken cancellation)
        => commands.Writer.WriteAsync(JsonSerializer.Serialize(new { type }), cancellation);

    public async Task StopAsync(CancellationToken cancellation)
    {
        if (Interlocked.Exchange(ref stopped, 1) != 0) return;
        try
        {
            await commands.Writer.WriteAsync(JsonSerializer.Serialize(new { type = "finish" }), cancellation);
            commands.Writer.TryComplete();
            await writer.WaitAsync(TimeSpan.FromSeconds(3), cancellation);
            await reader.WaitAsync(TimeSpan.FromSeconds(5), cancellation);
        }
        finally { Kill(); }
    }

    private async Task WriteLoopAsync()
    {
        try { await foreach (var command in commands.Reader.ReadAllAsync(lifetime.Token)) await process.StandardInput.WriteLineAsync(command); }
        catch (OperationCanceledException) { }
        catch (IOException) { }
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (await process.StandardOutput.ReadLineAsync(lifetime.Token) is { } line)
            {
                var root = Parse(line); var type = root.GetProperty("type").GetString();
                if (type is "transcript" or "partial" or "final")
                {
                    var text = root.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
                    var final = type == "final" || (root.TryGetProperty("isFinal", out var f) && f.GetBoolean());
                    SafeUpdate(new StreamingUpdate(text, final, root.TryGetProperty("state", out var s) ? s.GetString() : null));
                }
                else if (type == "error") SafeUpdate(new StreamingUpdate("", true, "error", root.TryGetProperty("message", out var e) ? e.GetString() : "流式转写失败。"));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { SafeUpdate(new StreamingUpdate("", true, "error", ex.Message)); }
    }

    private async Task DrainStandardErrorAsync()
    {
        var buffer = new char[1024];
        try { while (await process.StandardError.ReadAsync(buffer, lifetime.Token) > 0) { } }
        catch (OperationCanceledException) { }
        catch (IOException) { }
    }

    private static JsonElement Parse(string line)
    {
        using var doc = JsonDocument.Parse(line);
        return doc.RootElement.Clone();
    }
    private void SafeUpdate(StreamingUpdate item) { try { update(item); } catch { } }
    private void Kill() { lifetime.Cancel(); try { if (!process.HasExited) process.Kill(true); } catch { } process.Dispose(); }
    public async ValueTask DisposeAsync() { if (Volatile.Read(ref stopped) == 0) try { await StopAsync(CancellationToken.None); } catch { Kill(); } else Kill(); lifetime.Dispose(); }
}
