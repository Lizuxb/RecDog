using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace RecDog.Desktop;

public sealed record WorkerUpdate(string Type, double CompletedSeconds = 0, double TotalSeconds = 0);
public sealed record WorkerResult(string Text, string Language);

/// <summary>Bounded streaming audio preparation and an isolated, cancellable JSONL process.</summary>
public static class TranscriptionRunner
{
    public static void PrepareAudio(string source, string destination, CancellationToken cancellation)
    {
        using var reader = new AudioFileReader(source);
        ISampleProvider samples = reader;
        if (samples.WaveFormat.Channels == 2) samples = new StereoToMonoSampleProvider(samples);
        if (samples.WaveFormat.Channels != 1) throw new InvalidDataException("仅支持单声道或双声道录音。");
        samples = new WdlResamplingSampleProvider(samples, 16000);
        var pcm = new SampleToWaveProvider16(samples);
        using var output = new WaveFileWriter(destination, pcm.WaveFormat);
        var buffer = new byte[32768]; int count;
        while ((count = pcm.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellation.ThrowIfCancellationRequested(); output.Write(buffer, 0, count);
        }
        cancellation.ThrowIfCancellationRequested();
    }

    public static async Task<WorkerResult> RunWorkerAsync(string python, string model, string worker, string audioPath, Action<WorkerUpdate> update, CancellationToken cancellation, TimeSpan? timeout = null)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        limit.CancelAfter(timeout ?? TimeSpan.FromHours(8));
        var token = limit.Token;
        var start = new ProcessStartInfo(python) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, StandardInputEncoding = new UTF8Encoding(false) };
        start.ArgumentList.Add("-u"); start.ArgumentList.Add(worker); start.ArgumentList.Add("--model"); start.ArgumentList.Add(model);
        start.Environment["HF_HUB_OFFLINE"] = "1"; start.Environment["TRANSFORMERS_OFFLINE"] = "1"; start.Environment["PYTHONIOENCODING"] = "utf-8";
        using var process = new Process { StartInfo = start };
        process.Start();
        using var kill = token.Register(() => Kill(process));
        var errors = new StringBuilder();
        var drain = Task.Run(async () =>
        {
            var buffer = new char[2048]; int count;
            while ((count = await process.StandardError.ReadAsync(buffer, token)) > 0)
                if (errors.Length < 8192) errors.Append(buffer, 0, Math.Min(count, 8192 - errors.Length));
        }, CancellationToken.None);
        try
        {
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { audioPath }).AsMemory(), token);
            process.StandardInput.Close();
            WorkerResult? result = null;
            while (await ReadBoundedLineAsync(process.StandardOutput, token) is { } line)
            {
                using var json = JsonDocument.Parse(line);
                var root = json.RootElement;
                switch (root.GetProperty("type").GetString())
                {
                    case "loading": update(new("loading")); break;
                    case "progress":
                        var completed = root.GetProperty("completedSeconds").GetDouble(); var total = root.GetProperty("totalSeconds").GetDouble();
                        if (!double.IsFinite(completed) || !double.IsFinite(total) || completed < 0 || total <= 0 || completed > total) throw new InvalidDataException("转写进程返回无效进度。");
                        update(new("progress", completed, total)); break;
                    case "completed":
                        if (result is not null) throw new InvalidDataException("转写进程重复返回结果。");
                        result = new(root.GetProperty("text").GetString() ?? throw new InvalidDataException("转写文字为空。"), root.GetProperty("language").GetString() ?? ""); break;
                    case "error": throw new InvalidOperationException(root.GetProperty("message").GetString() ?? "转写失败。");
                    default: throw new InvalidDataException("转写进程协议无效。");
                }
            }
            await process.WaitForExitAsync(token);
            await drain;
            token.ThrowIfCancellationRequested();
            if (process.ExitCode != 0 || result is null) throw new InvalidOperationException("转写进程未成功完成。" + errors);
            return result;
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw new TimeoutException("转写超时，工作进程已停止。"); }
        catch (JsonException ex) { throw new InvalidDataException("转写进程返回了无效 JSON。", ex); }
        finally
        {
            Kill(process);
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)); } catch (TimeoutException) { }
            try { await drain.WaitAsync(TimeSpan.FromSeconds(3)); } catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or IOException) { }
        }
    }

    private static async Task<string?> ReadBoundedLineAsync(StreamReader reader, CancellationToken token)
    {
        var text = new StringBuilder(); var one = new char[1];
        while (await reader.ReadAsync(one, token) != 0)
        {
            if (one[0] == '\n') return text.ToString();
            if (text.Length >= 10_000_000) throw new InvalidDataException("转写进程返回数据过大。");
            text.Append(one[0]);
        }
        return text.Length == 0 ? null : text.ToString();
    }
    private static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }
}
