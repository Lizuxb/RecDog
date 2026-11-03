using System.IO;
using NAudio.MediaFoundation;
using NAudio.Wave;

namespace RecDog.Desktop;

public sealed record RecordingResult(string FilePath, long DurationMs);

/// <summary>Streams microphone PCM to disk, then archives it locally as MP3.</summary>
public sealed class AudioRecorder : IDisposable
{
    private readonly object sync = new();
    private WaveInEvent? input;
    private WaveFileWriter? writer;
    private TaskCompletionSource<bool>? stopped;
    private string wavePath = "";
    private string mp3Path = "";
    private long byteCount;
    private long lastFlush;
    private Exception? captureError;
    private bool active;
    private bool paused;
    private bool stopping;
    private bool disposed;

    public bool IsActive { get { lock (sync) return active; } }
    public bool IsPaused { get { lock (sync) return paused; } }
    public event Action<Exception>? Failed;
    public event Action<byte[]>? PcmAvailable;

    internal static void RequireDeviceName(string? expected, string actual)
    {
        if (expected is not null && !StringComparer.Ordinal.Equals(expected, actual)) throw new InvalidOperationException("麦克风列表已变化，请刷新并重新选择录音设备。");
    }

    internal static RecordingResult CompleteArchive(string wave, string mp3, long duration)
    {
        // Encoding already succeeded. Recovery-file housekeeping must not discard its result.
        try { File.Delete(wave); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return new RecordingResult(mp3, duration);
    }

    public static float MeasurePeak(byte[] buffer, int count)
    {
        var peak = 0;
        for (var i = 0; i + 1 < count; i += 2) peak = Math.Max(peak, Math.Abs((int)System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(i, 2))));
        return peak / 32768f;
    }

    public static string AvailablePath(string directory, string name)
    {
        var clean = new string(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        clean = clean[..Math.Min(clean.Length, 90)];
        if (string.IsNullOrWhiteSpace(clean)) clean = "录音";
        var candidate = Path.Combine(directory, clean + ".mp3");
        for (var n = 2; File.Exists(candidate) || File.Exists(candidate + ".pending.wav"); n++) candidate = Path.Combine(directory, clean + "_" + n + ".mp3");
        return candidate;
    }

    public static object[] Devices() => Enumerable.Range(0, WaveInEvent.DeviceCount).Select(i => (object)new { id = i, name = WaveInEvent.GetCapabilities(i).ProductName }).ToArray();
    public float Peak { get { lock (sync) return paused || !active || Environment.TickCount64 - lastInputAt > 500 ? 0 : peak; } }
    private float peak;
    private long lastInputAt;
    public Task<object> StartAsync(string directory, string? fileName = null, int deviceNumber = 0, string? expectedDeviceName = null)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (active) throw new InvalidOperationException("当前录音尚未保存，请先停止录音。");
            if (WaveInEvent.DeviceCount == 0) throw new InvalidOperationException("未检测到麦克风。请连接录音设备，并检查 Windows 麦克风权限。");
            if (deviceNumber < 0 || deviceNumber >= WaveInEvent.DeviceCount) throw new InvalidOperationException("所选麦克风已不可用，请刷新设备列表。");
            var deviceName = WaveInEvent.GetCapabilities(deviceNumber).ProductName;
            RequireDeviceName(expectedDeviceName, deviceName);
            Directory.CreateDirectory(directory);
            var id = Guid.NewGuid().ToString("N");
            mp3Path = AvailablePath(directory, fileName ?? id);
            wavePath = mp3Path + ".pending.wav";
            peak = 0;
            byteCount = lastFlush = 0;
            captureError = null;
            paused = stopping = false;
            stopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var source = new WaveInEvent { DeviceNumber = deviceNumber, WaveFormat = new WaveFormat(44100, 16, 1), BufferMilliseconds = 100, NumberOfBuffers = 3 };
            try
            {
                writer = new WaveFileWriter(wavePath, source.WaveFormat);
                input = source;
                source.DataAvailable += WriteAudio;
                source.RecordingStopped += CaptureStopped;
                source.StartRecording();
                active = true;
                return Task.FromResult<object>(new { active = true, paused = false, device = deviceName });
            }
            catch (Exception ex)
            {
                source.DataAvailable -= WriteAudio;
                source.RecordingStopped -= CaptureStopped;
                source.Dispose();
                writer?.Dispose(); writer = null; input = null;
                if (File.Exists(wavePath) && byteCount == 0) File.Delete(wavePath);
                throw new InvalidOperationException("无法启动麦克风。请检查设备是否被占用及 Windows 麦克风权限。", ex);
            }
        }
    }

    public void Pause()
    {
        lock (sync) { RequireActive(); paused = true; }
    }

    public void Resume()
    {
        lock (sync) { RequireActive(); if (captureError is not null) throw new InvalidOperationException("录音设备已停止，请先结束并保存当前录音。", captureError); paused = false; }
    }

    private void RequireActive()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!active || stopping) throw new InvalidOperationException("当前没有正在进行的录音。");
    }

    private void WriteAudio(object? sender, WaveInEventArgs e)
    {
        WaveInEvent? failedSource = null;
        byte[]? pcm = null;
        lock (sync)
        {
            if (paused || writer is null || captureError is not null) return;
            try
            {
                peak = MeasurePeak(e.Buffer, e.BytesRecorded);
                lastInputAt = Environment.TickCount64;
                writer.Write(e.Buffer, 0, e.BytesRecorded);
                byteCount += e.BytesRecorded;
                pcm = e.Buffer.AsSpan(0, e.BytesRecorded).ToArray();
                if (byteCount - lastFlush >= writer.WaveFormat.AverageBytesPerSecond)
                {
                    writer.Flush();
                    lastFlush = byteCount;
                }
            }
            catch (Exception ex) { captureError = ex; failedSource = input; }
        }
        if (pcm is not null)
            try { PcmAvailable?.Invoke(pcm); } catch { /* Transcription subscribers must never interrupt audio archiving. */ }
        failedSource?.StopRecording();
    }

    private void CaptureStopped(object? sender, StoppedEventArgs e)
    {
        Exception? failure;
        lock (sync)
        {
            captureError ??= e.Exception;
            failure = captureError;
            if (failure is not null) paused = true;
            stopped?.TrySetResult(true);
        }
        if (failure is not null) Failed?.Invoke(failure);
    }

    public async Task<RecordingResult> StopAsync()
    {
        WaveInEvent source;
        Task completion;
        lock (sync)
        {
            RequireActive();
            stopping = true;
            source = input!;
            completion = stopped!.Task;
        }
        try
        {
            source.StopRecording();
            await completion.WaitAsync(TimeSpan.FromSeconds(10));
            long duration;
            Exception? error;
            lock (sync)
            {
                duration = byteCount * 1000 / writer!.WaveFormat.AverageBytesPerSecond;
                writer.Dispose(); writer = null;
                error = captureError;
            }
            if (error is not null) throw new IOException("录音设备或磁盘发生错误。已采集的 WAV 保留于：" + wavePath, error);
            if (duration == 0) throw new IOException("未采集到音频。请检查麦克风权限，录音文件保留于：" + wavePath);
            await Task.Run(() => EncodeMp3(wavePath, mp3Path));
            return CompleteArchive(wavePath, mp3Path, duration);
        }
        catch (Exception ex) when (ex is not IOException)
        {
            throw new IOException("录音保存失败，恢复用 WAV 保留于：" + wavePath, ex);
        }
        finally
        {
            source.DataAvailable -= WriteAudio;
            source.RecordingStopped -= CaptureStopped;
            source.Dispose();
            lock (sync) { writer?.Dispose(); writer = null; input = null; active = paused = stopping = false; }
        }
    }

    private static void EncodeMp3(string source, string destination)
    {
        // The temporary output is renamed only after the encoder has closed it successfully.
        // Media Foundation selects its container from the final extension.
        var temporary = destination + ".encoding.mp3";
        try
        {
            MediaFoundationApi.Startup();
            using var reader = new WaveFileReader(source);
            MediaFoundationEncoder.EncodeToMp3(reader, temporary, 128000);
            File.Move(temporary, destination);
        }
        catch (Exception ex) { throw new IOException("无法完成 MP3 编码，恢复用 WAV 保留于：" + source, ex); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void Dispose()
    {
        WaveInEvent? source;
        lock (sync)
        {
            if (disposed) return;
            disposed = true; paused = true;
            source = input; input = null;
        }
        if (source is not null)
        {
            source.StopRecording();
            source.DataAvailable -= WriteAudio;
            source.RecordingStopped -= CaptureStopped;
            source.Dispose();
        }
        lock (sync) { writer?.Dispose(); writer = null; active = paused = false; }
    }
}
