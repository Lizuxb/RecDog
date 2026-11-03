using System.Collections.Concurrent;
using System.Text.Json;

namespace RecDog.Core;

public sealed record Transcript(string RecordingId, string Model, string Text, string? EditedText, int Revision, DateTimeOffset CreatedAt);

/// <summary>Independent recording results with atomic optimistic revision checks.</summary>
public sealed class TranscriptRepository(WorkspaceRepository workspace)
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);
    public Task<Transcript?> ReadAsync(string folderId, string id) => LockedAsync(folderId, id, ReadFileAsync);
    public Task<Transcript> CreateAsync(string folderId, string id, string model, string text, CancellationToken cancellation = default) => LockedAsync(folderId, id, async file =>
    {
        cancellation.ThrowIfCancellationRequested();
        if (await ReadFileAsync(file) is not null) throw new InvalidOperationException("已有转写结果，请查看并修订已有结果。");
        if (string.IsNullOrWhiteSpace(model) || text is null) throw new InvalidDataException("转写结果无效。");
        var transcript = new Transcript(id, model, text, null, 1, DateTimeOffset.UtcNow);
        await WriteAsync(file, transcript, cancellation); return transcript;
    });
    public Task<Transcript> SaveAsync(string folderId, string id, string text, int revision) => LockedAsync(folderId, id, async file =>
    {
        var previous = await ReadFileAsync(file) ?? throw new InvalidOperationException("尚无转写结果。");
        if (revision != previous.Revision) throw new InvalidOperationException("转写结果已更新，请重新打开后再保存，当前修订尚未覆盖。");
        if (text is null || text.Length > 10_000_000) throw new InvalidDataException("修订文字过长或无效。");
        var next = previous with { EditedText = text, Revision = checked(previous.Revision + 1) };
        await WriteAsync(file, next); return next;
    });
    public Task<Transcript> ReplaceMachineTextAsync(string folderId, string id, string model, string text, int revision, CancellationToken cancellation = default) => LockedAsync(folderId, id, async file =>
    {
        cancellation.ThrowIfCancellationRequested();
        var previous = await ReadFileAsync(file) ?? throw new InvalidOperationException("原转写结果已不存在，请重新打开录音后再转写。");
        if (revision != previous.Revision) throw new InvalidOperationException("转写期间结果已被修订，保留现有结果。请查看修订后再重新转写。");
        if (string.IsNullOrWhiteSpace(model) || text is null) throw new InvalidDataException("转写结果无效。");
        var next = previous with { Model = model, Text = text, Revision = checked(previous.Revision + 1) };
        await WriteAsync(file, next, cancellation); return next;
    });

    private async Task<T> LockedAsync<T>(string folderId, string id, Func<string, Task<T>> action)
    {
        ValidateId(folderId); ValidateId(id);
        var gate = Gates.GetOrAdd(workspace.DirectoryPath, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            var state = await workspace.LoadOrCreateAsync();
            var recording = state.Folders.SingleOrDefault(f => f.Id == folderId)?.Recordings.SingleOrDefault(r => r.Id == id) ?? throw new InvalidDataException("录音不存在或不属于当前人员文件夹。");
            workspace.ResolveRecordingPath(recording.RelativePath);
            var file = Path.Combine(workspace.DirectoryPath, "transcripts", folderId, id + ".json");
            for (var item = new FileInfo(file); item is not null; item = item.Directory is { } parent ? new FileInfo(parent.FullName) : null)
                if ((File.Exists(item.FullName) || Directory.Exists(item.FullName)) && (File.GetAttributes(item.FullName) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("转写路径不能经过符号链接或目录联接。");
            return await action(file);
        }
        finally { gate.Release(); }
    }
    private static void ValidateId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 100 || id.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))) throw new InvalidDataException("标识无效。");
    }
    private static async Task<Transcript?> ReadFileAsync(string file)
    {
        if (!File.Exists(file)) return null;
        await using var stream = File.OpenRead(file);
        var result = await JsonSerializer.DeserializeAsync<Transcript>(stream, WorkspaceRepository.JsonOptions);
        if (result is null || result.RecordingId != Path.GetFileNameWithoutExtension(file) || result.Revision < 1 || result.Text is null || string.IsNullOrWhiteSpace(result.Model) || result.CreatedAt == default) throw new InvalidDataException("转写结果文件无效。");
        return result;
    }
    private static async Task WriteAsync(string file, Transcript result, CancellationToken cancellation = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, result, WorkspaceRepository.JsonOptions, cancellation);
                await stream.FlushAsync(cancellation); stream.Flush(true);
            }
            cancellation.ThrowIfCancellationRequested();
            if (File.Exists(file)) File.Replace(temporary, file, null); else File.Move(temporary, file);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
