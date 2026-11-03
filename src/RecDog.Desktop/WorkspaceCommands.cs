using System.IO;
using System.Diagnostics;
using Microsoft.Web.WebView2.Core;
using RecDog.Core;

namespace RecDog.Desktop;

public sealed partial class WorkbenchWindow
{
    private readonly AudioRecorder recorder = new();
    private bool workspaceOpen = true;
    private string? recordingFolderId;
    private RecordingResult? uncommittedRecording;
    private string? recordingDisplayName;
    private DateTimeOffset recordingStartedAt;
    private ILiveTranscription? streamingRunner;
    private string? streamingSessionId;

    private object MemorySnapshot()
    {
        var ids = browser.CoreWebView2.Environment.GetProcessInfos().Select(item => (int)item.ProcessId).Append(Environment.ProcessId).Distinct();
        var values = new List<object>(); long privateBytes = 0;
        foreach (var id in ids)
        {
            try { using var process = Process.GetProcessById(id); process.Refresh(); privateBytes += process.PrivateMemorySize64; values.Add(new { id, name = process.ProcessName, privateBytes = process.PrivateMemorySize64, workingSetBytes = process.WorkingSet64 }); }
            catch (ArgumentException) { }
        }
        return new { privateBytes, processes = values };
    }

    private static bool IsRecordingMedia(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "recdog-media.local" && uri.IsDefaultPort;

    private void MapRecordingMedia()
    {
        var mediaPath = Path.Combine(repository.DirectoryPath, "recordings");
        Directory.CreateDirectory(mediaPath);
        browser.CoreWebView2.SetVirtualHostNameToFolderMapping("recdog-media.local", mediaPath, CoreWebView2HostResourceAccessKind.DenyCors);
    }

    private void RequireNoRecording()
    {
        if (recorder.IsActive || uncommittedRecording is not null) throw new InvalidOperationException("请先停止并保存当前录音。");
    }

    private async Task<object?> CloseWorkspaceAsync()
    {
        RequireNoRecording();
        await CancelTranscriptionAsync(true);
        await File.WriteAllTextAsync(Path.Combine(smokeDirectory ?? appData, "workspace-path.txt"), "!closed");
        workspaceOpen = false;
        return null;
    }

    private async Task<object?> DeleteWorkspaceAsync()
    {
        RequireNoRecording();
        await CancelTranscriptionAsync(true);
        await repository.LoadOrCreateAsync();
        var target = Path.GetFullPath(repository.DirectoryPath);
        var application = Path.GetFullPath(AppContext.BaseDirectory);
        var currentDirectory = Path.GetFullPath(Environment.CurrentDirectory);
        static bool Contains(string parent, string child) => child.Equals(parent, StringComparison.OrdinalIgnoreCase) || child.StartsWith(Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        if (Directory.GetParent(target) is null || Contains(target, application) || Contains(target, currentDirectory) || Directory.Exists(Path.Combine(target, ".git")) || (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("此项目目录同时包含程序、开发文件或目录链接，不能整目录删除。请先将资料迁移到独立项目目录。");
        var archiveRoot = Path.Combine(Directory.GetParent(target)!.FullName, ".RecDog-trash");
        Directory.CreateDirectory(archiveRoot);
        var destination = Path.Combine(archiveRoot, Path.GetFileName(target) + "-" + Guid.NewGuid().ToString("N"));
        browser.CoreWebView2.ClearVirtualHostNameToFolderMapping("recdog-media.local");
        try { Directory.Move(target, destination); }
        catch { MapRecordingMedia(); throw; }
        workspaceOpen = false;
        await File.WriteAllTextAsync(Path.Combine(smokeDirectory ?? appData, "workspace-path.txt"), "!closed");
        return new { archivedPath = destination };
    }

    private async Task<Workspace> DeleteFolderAsync(string id)
    {
        RequireNoRecording();
        if (TranscriptionStatus()?.FolderId == id) await CancelTranscriptionAsync(true);
        return await repository.DeleteFolderAsync(id);
    }

    private async Task<object> StartRecordingAsync(string folderId, string documentName, int deviceNumber = 0, string? expectedDeviceName = null, bool streaming = false, string? sessionId = null)
    {
        RequireNoRecording();
        var workspace = await repository.LoadOrCreateAsync();
        var folder = workspace.Folders.SingleOrDefault(item => item.Id == folderId) ?? throw new InvalidDataException("人员文件夹不存在。");
        if (!folder.Documents.Any(document => document.Kind == "document" && document.Title == documentName))
            throw new InvalidDataException("请先打开人员文件夹中的具体文档，再开始录音。");
        // Resolve through the repository before handing a path to the audio module.
        var sentinel = repository.ResolveRecordingPath($"recordings/{folder.Id}/capture.mp3");
        recordingFolderId = folder.Id;
        recordingStartedAt = DateTimeOffset.Now;
        var fileName = folder.Name[..Math.Min(folder.Name.Length, 20)] + "_" + documentName[..Math.Min(documentName.Length, 35)] + "_录音_" + recordingStartedAt.ToString("yyyyMMdd_HHmmss");
        recordingDisplayName = Path.GetFileNameWithoutExtension(AudioRecorder.AvailablePath(Path.GetDirectoryName(sentinel)!, fileName));
        try
        {
            if (streaming)
            {
                try { await StartStreamingAsync(sessionId); }
                catch (Exception ex) { Post(new { @event = "streamingTranscript", sessionId, text = "", isFinal = true, state = "error", error = ex.Message }); }
            }
            var result = await recorder.StartAsync(Path.GetDirectoryName(sentinel)!, recordingDisplayName, deviceNumber, expectedDeviceName);
            return result;
        }
        catch { await FinishStreamingAsync(); recordingFolderId = null; throw; }
    }

    private async Task StartStreamingAsync(string? sessionId)
    {
        var live = await LoadLiveSettingsAsync();
        // Enforce the user's opt-out even if a stale frontend requests a streaming session.
        if (live.Provider == "off") return;
        if (live.Provider is not ("local" or "aliyun")) throw new InvalidOperationException("请在实时识别设置中选择有效的识别方式。");
        var activeSession = streamingSessionId = string.IsNullOrWhiteSpace(sessionId) ? Guid.NewGuid().ToString("N") : sessionId;
        void OnUpdate(StreamingUpdate update) => Dispatcher.BeginInvoke(() => Post(new { @event = "streamingTranscript", sessionId = activeSession, text = update.Text, isFinal = update.IsFinal, state = update.State, error = update.Error, sentenceId = update.SentenceId }));
        if (live.Provider == "aliyun")
        {
            if (live.AppKey.Length == 0 || live.ProtectedToken.Length == 0) throw new InvalidOperationException("请在实时识别设置中填写阿里云 AppKey 和临时 Token。");
            streamingRunner = await AliyunStreaming.StartAsync(live.AppKey, ProtectToken(live.ProtectedToken, true), OnUpdate, CancellationToken.None);
            recorder.PcmAvailable += OnStreamingPcm;
            return;
        }
        var settings = await ReadTranscriptionSettingsAsync();
        var model = DiscoverTranscriptionPath(Path.Combine("models", "sherpa-onnx-streaming-zipformer-bilingual-zh-en"))
            ?? DiscoverTranscriptionPath(Path.Combine("models", "sherpa-onnx-streaming-zipformer-zh-int8-2025-06-30"));
        var worker = DiscoverTranscriptionPath(Path.Combine("workers", "sherpa_asr", "worker.py"));
        if (!Path.IsPathFullyQualified(settings.PythonPath) || !File.Exists(settings.PythonPath)) throw new InvalidOperationException("请先在本机转写设置中指定 Python 环境。");
        if (model is null || worker is null) throw new InvalidOperationException("未找到 sherpa-onnx 流式中文模型或工作进程。");
        streamingRunner = await StreamingTranscriptionRunner.StartAsync(settings.PythonPath, model, worker, OnUpdate, CancellationToken.None);
        recorder.PcmAvailable += OnStreamingPcm;
        Post(new { @event = "streamingTranscript", sessionId = streamingSessionId, text = "", isFinal = false, state = "ready" });
    }
    private void OnStreamingPcm(byte[] pcm) => streamingRunner?.TrySendAudio(pcm);
    private async Task FinishStreamingAsync()
    {
        recorder.PcmAvailable -= OnStreamingPcm;
        var runner = streamingRunner; streamingRunner = null;
        if (runner is null) return;
        try { await runner.StopAsync(CancellationToken.None); }
        catch (Exception ex) { Post(new { @event = "streamingTranscript", sessionId = streamingSessionId, text = "", isFinal = true, state = "error", error = ex.Message }); }
        finally { await runner.DisposeAsync(); streamingSessionId = null; }
    }
    private async Task<object?> PauseRecording() { recorder.Pause(); if (streamingRunner is { } runner) await runner.PauseAsync(CancellationToken.None); return null; }
    private async Task<object?> ResumeRecording() { recorder.Resume(); if (streamingRunner is { } runner) await runner.ResumeAsync(CancellationToken.None); return null; }

    private async Task<WorkspaceRecording> StopRecordingAsync()
    {
        if (recordingFolderId is null) throw new InvalidOperationException("没有正在进行的录音。");
        // If metadata persistence failed, retry the same completed file instead of discarding it.
        try { uncommittedRecording ??= await recorder.StopAsync(); }
        finally { await FinishStreamingAsync(); }
        var result = uncommittedRecording;
        var recording = new WorkspaceRecording(Guid.NewGuid().ToString("N"), Path.GetFileNameWithoutExtension(result.FilePath), Path.GetRelativePath(repository.DirectoryPath, result.FilePath).Replace('\\', '/'), result.DurationMs, recordingStartedAt);
        await repository.SaveRecordingAsync(recordingFolderId, recording);
        uncommittedRecording = null;
        recordingFolderId = null;
        recordingDisplayName = null;
        return recording;
    }

    private async Task<object> ReadRecordingAsync(string folderId, string id)
    {
        var workspace = await repository.LoadOrCreateAsync();
        var recording = workspace.Folders.SingleOrDefault(item => item.Id == folderId)?.Recordings.SingleOrDefault(item => item.Id == id) ?? throw new InvalidDataException("录音不存在。");
        var path = repository.ResolveRecordingPath(recording.RelativePath);
        if (!File.Exists(path)) throw new FileNotFoundException("录音文件已被移动或删除。");
        var relative = recording.RelativePath["recordings/".Length..];
        return new { url = "https://recdog-media.local/" + string.Join('/', relative.Split('/').Select(Uri.EscapeDataString)) };
    }

    private async Task<(WorkspaceRecording Recording, string Path)> FindRecordingAsync(string folderId, string id)
    {
        var workspace = await repository.LoadOrCreateAsync();
        var recording = workspace.Folders.SingleOrDefault(item => item.Id == folderId)?.Recordings.SingleOrDefault(item => item.Id == id) ?? throw new InvalidDataException("录音不存在。");
        var path = repository.ResolveRecordingPath(recording.RelativePath);
        if (!File.Exists(path)) throw new FileNotFoundException("录音文件已被移动或删除。");
        return (recording, path);
    }

    private async Task<object> ExportRecordingAsync(string folderId, string id)
    {
        var (recording, source) = await FindRecordingAsync(folderId, id);
        var name = string.Concat(recording.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).TrimEnd(' ', '.');
        var dialog = new Microsoft.Win32.SaveFileDialog { Title = "导出录音 MP3", FileName = name + ".mp3", DefaultExt = ".mp3", AddExtension = true, Filter = "MP3 录音 (*.mp3)|*.mp3", OverwritePrompt = true };
        if (smokeDirectory is not null) { Directory.CreateDirectory(smokeDirectory); dialog.FileName = System.IO.Path.Combine(smokeDirectory, "exported-recording.mp3"); }
        else if (dialog.ShowDialog(this) != true) return new { canceled = true };
        if (!Path.GetExtension(dialog.FileName).Equals(".mp3", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("请选择 .mp3 文件扩展名。");
        if (Path.GetFullPath(source).Equals(Path.GetFullPath(dialog.FileName), StringComparison.OrdinalIgnoreCase)) return new { canceled = false };
        var destination = Path.GetFullPath(dialog.FileName);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await RecordingExport.CopyForPlaybackAsync(source, temporary);
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return new { canceled = false };
    }

    private async Task<object?> RecordingPathActionAsync(string folderId, string id, bool copy)
    {
        var (_, path) = await FindRecordingAsync(folderId, id);
        if (copy) System.Windows.Clipboard.SetText(path);
        else
        {
            var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
            start.ArgumentList.Add("/select,"); start.ArgumentList.Add(path);
            Process.Start(start);
        }
        return null;
    }
}
