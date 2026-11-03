using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace RecDog.Desktop;

public static class AliyunProtocol
{
    public static string Start(string appkey, string task) => JsonSerializer.Serialize(new
    {
        header = Header(appkey, task, "StartTranscription"),
        payload = new { format = "pcm", sample_rate = 16000, enable_intermediate_result = true, enable_punctuation_prediction = true, enable_inverse_text_normalization = true, max_sentence_silence = 800 }
    });
    public static string Stop(string appkey, string task) => JsonSerializer.Serialize(new { header = Header(appkey, task, "StopTranscription") });
    private static object Header(string appkey, string task, string name) => new { appkey, task_id = task, message_id = Guid.NewGuid().ToString("N"), @namespace = "SpeechTranscriber", name };
    public static StreamingUpdate? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement; var h = root.GetProperty("header"); var name = h.GetProperty("name").GetString();
        if (name == "TaskFailed" || (h.TryGetProperty("status", out var code) && code.GetInt32() != 20000000))
            return new("", false, "failed", "阿里云识别失败：" + (h.TryGetProperty("status_text", out var message) ? message.GetString() : "未知错误"));
        if (name is "TranscriptionStarted" or "TranscriptionCompleted") return new("", false, name == "TranscriptionStarted" ? "ready" : "stopped");
        if (name is not ("TranscriptionResultChanged" or "SentenceEnd")) return null;
        var p = root.GetProperty("payload");
        return new(p.GetProperty("result").GetString() ?? "", name == "SentenceEnd", "listening", null, p.GetProperty("index").GetInt32());
    }
}

public sealed class AliyunStreaming : ILiveTranscription
{
    private readonly ClientWebSocket socket = new();
    private readonly Channel<byte[]> audio = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(50) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });
    private readonly CancellationTokenSource lifetime = new();
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Action<StreamingUpdate> update;
    private readonly string appkey;
    private readonly string task = Guid.NewGuid().ToString("N");
    private Task writer = Task.CompletedTask, reader = Task.CompletedTask;
    private int stopped, disposed;
    private AliyunStreaming(string appkey, Action<StreamingUpdate> update) { this.appkey = appkey; this.update = update; }
    public static async Task<AliyunStreaming> StartAsync(string appkey, string token, Action<StreamingUpdate> update, CancellationToken cancellation)
    {
        var session = new AliyunStreaming(appkey, update);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            await session.socket.ConnectAsync(new Uri("wss://nls-gateway-cn-shanghai.aliyuncs.com/ws/v1?token=" + Uri.EscapeDataString(token)), timeout.Token);
            session.reader = session.ReceiveAsync();
            await session.SendTextAsync(AliyunProtocol.Start(appkey, session.task), timeout.Token);
            await session.ready.Task.WaitAsync(timeout.Token);
            session.writer = session.WriteAsync();
            return session;
        }
        catch { await session.DisposeAsync(); throw new InvalidOperationException("无法启动阿里云实时识别，请检查网络、AppKey 和临时 Token 是否有效。"); }
    }
    private Task SendTextAsync(string text, CancellationToken token) => socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(text)), WebSocketMessageType.Text, true, token);
    public bool TrySendAudio(byte[] pcm16)
    {
        if (Volatile.Read(ref stopped) != 0) return false;
        if (audio.Writer.TryWrite(pcm16)) return true;
        Fail("网络发送过慢，实时识别已停止；本地录音继续保存。");
        return false;
    }
    private async Task WriteAsync()
    {
        var converter = new StreamingPcmResampler();
        try
        {
            await foreach (var pcm in audio.Reader.ReadAllAsync(lifetime.Token))
            {
                var converted = converter.Convert(pcm);
                if (converted.Length > 0) await socket.SendAsync(new ArraySegment<byte>(converted), WebSocketMessageType.Binary, true, lifetime.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch { Fail("阿里云音频发送失败，请检查网络；本地录音继续保存。"); }
    }
    private async Task ReceiveAsync()
    {
        try
        {
            var buffer = new byte[8192];
            while (!lifetime.IsCancellationRequested)
            {
                using var message = new MemoryStream();
                WebSocketReceiveResult part;
                do
                {
                    part = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), lifetime.Token);
                    if (part.MessageType == WebSocketMessageType.Close) throw new IOException("连接关闭");
                    message.Write(buffer, 0, part.Count);
                    if (message.Length > 1024 * 1024) throw new IOException("响应过长");
                } while (!part.EndOfMessage);
                if (AliyunProtocol.Parse(Encoding.UTF8.GetString(message.ToArray())) is not { } item) continue;
                if (item.Error is not null) { Fail(item.Error); return; }
                if (item.State == "ready") ready.TrySetResult();
                if (item.State == "stopped") { completed.TrySetResult(); return; }
                update(item);
            }
        }
        catch (OperationCanceledException) { }
        catch { Fail("阿里云实时识别连接中断；已识别文字和本地录音保留。"); }
    }
    private void Fail(string message)
    {
        if (Interlocked.Exchange(ref stopped, 1) != 0) return;
        ready.TrySetException(new InvalidOperationException(message));
        completed.TrySetException(new InvalidOperationException(message));
        update(new("", false, "failed", message));
        lifetime.Cancel(); socket.Abort(); audio.Writer.TryComplete();
    }
    public ValueTask PauseAsync(CancellationToken cancellation) => ValueTask.CompletedTask;
    public ValueTask ResumeAsync(CancellationToken cancellation) => ValueTask.CompletedTask;
    public async Task StopAsync(CancellationToken cancellation)
    {
        if (Interlocked.Exchange(ref stopped, 1) != 0) return;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        audio.Writer.TryComplete();
        await writer.WaitAsync(timeout.Token);
        await SendTextAsync(AliyunProtocol.Stop(appkey, task), timeout.Token);
        await completed.Task.WaitAsync(timeout.Token);
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        lifetime.Cancel(); socket.Abort(); audio.Writer.TryComplete();
        try { await Task.WhenAll(writer, reader); } catch { }
        socket.Dispose(); lifetime.Dispose();
    }
}
