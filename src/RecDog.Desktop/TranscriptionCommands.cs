using System.IO;
using System.Text.Json;
using RecDog.Core;

namespace RecDog.Desktop;

public sealed record TranscriptionSettings(string PythonPath, string ModelPath, bool Ready, string Message);
public sealed record TranscriptionJob(string Id, string FolderId, string RecordingId, string RecordingName, string State, string Message, double CompletedSeconds, double TotalSeconds, string? Error = null);

public sealed partial class WorkbenchWindow
{
    private TranscriptionJob? transcriptionJob;
    private CancellationTokenSource? transcriptionCancellation;
    private Task? transcriptionTask;
    private string TranscriptionSettingsPath => Path.Combine(smokeDirectory ?? appData, "transcription-settings.json");
    private TranscriptionJob? TranscriptionStatus() => Volatile.Read(ref transcriptionJob);
    private static string? DiscoverTranscriptionPath(string relative)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, relative);
            if (File.Exists(candidate) || Directory.Exists(candidate)) return candidate;
        }
        return null;
    }
    private static string WorkerPath => DiscoverTranscriptionPath(Path.Combine("workers", "qwen_asr", "worker.py")) ?? Path.Combine(AppContext.BaseDirectory, "workers", "qwen_asr", "worker.py");
    private async Task<TranscriptionSettings> ReadTranscriptionSettingsAsync()
    {
        string python = DiscoverTranscriptionPath(Path.Combine(".tools", "qwen-asr-env", "Scripts", "python.exe")) ?? "";
        string model = DiscoverTranscriptionPath(Path.Combine("models", "Qwen3-ASR-0.6B")) ?? "";
        if (File.Exists(TranscriptionSettingsPath))
        {
            try
            {
                using var json = JsonDocument.Parse(await File.ReadAllTextAsync(TranscriptionSettingsPath));
                python = json.RootElement.GetProperty("pythonPath").GetString() ?? "";
                model = json.RootElement.GetProperty("modelPath").GetString() ?? "";
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
            { return new(python, model, false, "转写设置文件无效，请重新保存 Python 和模型路径。"); }
        }
        return CheckTranscriptionSettings(python, model);
    }
    private static TranscriptionSettings CheckTranscriptionSettings(string python, string model)
    {
        if (!Path.IsPathFullyQualified(python) || !File.Exists(python)) return new(python, model, false, "请指定已安装 qwen-asr 的本机 Python 可执行文件。");
        if (!Path.IsPathFullyQualified(model) || !Directory.Exists(model)) return new(python, model, false, "请指定本机 Qwen3-ASR-0.6B 模型目录。");
        var weights = Path.Combine(model, "model.safetensors");
        if (!File.Exists(Path.Combine(model, "config.json")) || !File.Exists(weights) || new FileInfo(weights).Length != 1_876_091_704)
            return new(python, model, false, "模型文件缺失或大小不正确，请完成 Qwen3-ASR-0.6B 模型下载。");
        if (!File.Exists(WorkerPath)) return new(python, model, false, "缺少转写工作进程，请重新构建或安装应用。");
        return new(python, model, true, "本机路径已就绪；首次转写时加载模型。");
    }
    private async Task<TranscriptionSettings> SaveTranscriptionSettingsAsync(string python, string model)
    {
        if (transcriptionTask is { IsCompleted: false }) throw new InvalidOperationException("请先取消当前转写，再修改路径。");
        python = python.Trim().Trim('"'); model = model.Trim().Trim('"');
        var result = CheckTranscriptionSettings(python, model);
        Directory.CreateDirectory(Path.GetDirectoryName(TranscriptionSettingsPath)!);
        var temporary = TranscriptionSettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new { pythonPath = python, modelPath = model }, WorkspaceRepository.JsonOptions));
            File.Move(temporary, TranscriptionSettingsPath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return result;
    }
    private async Task<TranscriptionJob> StartTranscriptionAsync(string folderId, string id)
    {
        if (transcriptionTask is { IsCompleted: false }) throw new InvalidOperationException("已有转写任务，请等待完成或取消。");
        var project = repository;
        var workspace = await project.LoadOrCreateAsync();
        var recording = workspace.Folders.SingleOrDefault(f => f.Id == folderId)?.Recordings.SingleOrDefault(r => r.Id == id) ?? throw new InvalidDataException("录音不存在。");
        var source = project.ResolveRecordingPath(recording.RelativePath);
        if (!File.Exists(source)) throw new FileNotFoundException("录音文件已被移动或删除。");
        var transcripts = new TranscriptRepository(project);
        var previous = await transcripts.ReadAsync(folderId, id);
        var settings = await ReadTranscriptionSettingsAsync();
        if (!settings.Ready) throw new InvalidOperationException(settings.Message);
        transcriptionCancellation?.Dispose();
        var cancellation = transcriptionCancellation = new CancellationTokenSource();
        var job = new TranscriptionJob(Guid.NewGuid().ToString("N"), folderId, id, recording.Name, "loading", "正在准备音频与加载模型…", 0, recording.DurationMs / 1000d);
        Volatile.Write(ref transcriptionJob, job);
        var worker = WorkerPath;
        transcriptionTask = Task.Run(async () =>
        {
            var temporary = Path.Combine(Path.GetTempPath(), "RecDog-transcription-" + Guid.NewGuid().ToString("N") + ".wav");
            try
            {
                TranscriptionRunner.PrepareAudio(source, temporary, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                var result = await TranscriptionRunner.RunWorkerAsync(settings.PythonPath, settings.ModelPath, worker, temporary, update =>
                {
                    var next = update.Type == "loading" ? job with { Message = "正在加载本机模型…" } : job with { State = "transcribing", Message = "正在本机转写…", CompletedSeconds = update.CompletedSeconds, TotalSeconds = update.TotalSeconds };
                    Volatile.Write(ref transcriptionJob, next);
                }, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                const string model = "Qwen3-ASR-0.6B@4ce9cc728b473a5aedbe7b6e1ea45646316824dc";
                if (previous is null) await transcripts.CreateAsync(folderId, id, model, result.Text, cancellation.Token);
                else await transcripts.ReplaceMachineTextAsync(folderId, id, model, result.Text, previous.Revision, cancellation.Token);
                Volatile.Write(ref transcriptionJob, job with { State = "completed", Message = "转写完成，可查看并修订。", CompletedSeconds = job.TotalSeconds });
            }
            catch (OperationCanceledException) { Volatile.Write(ref transcriptionJob, job with { State = "canceled", Message = "转写已取消。" }); }
            catch (Exception ex) { Volatile.Write(ref transcriptionJob, job with { State = "failed", Message = "本机转写失败。", Error = ex.Message }); }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        });
        return job;
    }
    private async Task<object?> CancelTranscriptionAsync(bool clear = false)
    {
        transcriptionCancellation?.Cancel();
        if (transcriptionTask is { } task) await task;
        if (clear) Volatile.Write(ref transcriptionJob, null);
        return null;
    }
    private async Task<Workspace> DeleteTranscribedRecordingAsync(string folderId, string id)
    {
        if (TranscriptionStatus() is { } job && job.FolderId == folderId && job.RecordingId == id) await CancelTranscriptionAsync(true);
        return await repository.DeleteRecordingAsync(folderId, id);
    }
}
