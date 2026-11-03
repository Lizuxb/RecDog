using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace RecDog.Core;

public sealed record WorkspaceDocument(string Id, string Title, string Kind, DateTimeOffset UpdatedAt, JsonElement Content, DateTimeOffset? CreatedAt = null);
public sealed record WorkspaceRecording(string Id, string Name, string RelativePath, long DurationMs, DateTimeOffset CreatedAt);
public sealed record WorkspaceFolder(string Id, string Name, List<WorkspaceDocument> Documents, List<WorkspaceRecording> Recordings);
public sealed record Workspace(int SchemaVersion, string Id, string Name, string Path, List<WorkspaceDocument> Documents, List<WorkspaceFolder> Folders);

/// <summary>Atomic versioned project storage, serialized against the canonical directory.</summary>
public sealed class WorkspaceRepository(string path)
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    public string DirectoryPath { get; } = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path));
    private string ManifestPath => System.IO.Path.Combine(DirectoryPath, "workspace.json");

    public Task<Workspace> LoadOrCreateAsync() => LockedAsync(LoadOrCreateUnlockedAsync);

    public Task<WorkspaceDocument> SaveDocumentAsync(string id, string title, string kind, JsonElement content)
    {
        var document = new WorkspaceDocument(id, title, kind, DateTimeOffset.UtcNow, content.Clone());
        ValidateDocument(document);
        return LockedAsync(async () =>
        {
            var workspace = await LoadOrCreateUnlockedAsync();
            document = document with { CreatedAt = workspace.Documents.FirstOrDefault(d => d.Id == id) is { } previous ? previous.CreatedAt ?? previous.UpdatedAt : document.UpdatedAt };
            if (workspace.Folders.Any(f => f.Documents.Any(d => d.Id == id))) throw new InvalidDataException("文档已属于人员文件夹，不能改变归属。");
            await WriteAtomicAsync(workspace with { Documents = ReplaceOrAppend(workspace.Documents, document, d => d.Id) });
            return document;
        });
    }

    public Task<WorkspaceDocument> SaveFolderDocumentAsync(string folderId, string id, string title, JsonElement content)
    {
        var document = new WorkspaceDocument(id, title, "document", DateTimeOffset.UtcNow, content.Clone());
        ValidateDocument(document);
        return LockedAsync(async () =>
        {
            var workspace = await LoadOrCreateUnlockedAsync();
            var folder = FindFolder(workspace, folderId);
            document = document with { CreatedAt = folder.Documents.FirstOrDefault(d => d.Id == id) is { } previous ? previous.CreatedAt ?? previous.UpdatedAt : document.UpdatedAt };
            if (workspace.Documents.Any(d => d.Id == id) || workspace.Folders.Any(f => f.Id != folderId && f.Documents.Any(d => d.Id == id))) throw new InvalidDataException("文档标识已属于其他位置。");
            await WriteAtomicAsync(ReplaceFolder(workspace, folder with { Documents = ReplaceOrAppend(folder.Documents, document, d => d.Id) }));
            return document;
        });
    }

    public Task<Workspace> RenameAsync(string name)
    {
        ValidateName(name);
        return LockedAsync(async () => { var updated = (await LoadOrCreateUnlockedAsync()) with { Name = name.Trim() }; await WriteAtomicAsync(updated); return updated; });
    }

    public Task<WorkspaceFolder> CreateFolderAsync(string id, string name)
    {
        ValidateId(id); ValidateName(name);
        return LockedAsync(async () =>
        {
            var workspace = await LoadOrCreateUnlockedAsync();
            if (workspace.Folders.Any(f => f.Id.Equals(id, StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("人员文件夹标识已存在。");
            var folder = new WorkspaceFolder(id, name.Trim(), [], []);
            await WriteAtomicAsync(workspace with { Folders = [.. workspace.Folders, folder] });
            return folder;
        });
    }

    public Task<WorkspaceFolder> RenameFolderAsync(string id, string name)
    {
        ValidateName(name);
        return LockedAsync(async () =>
        {
            var workspace = await LoadOrCreateUnlockedAsync();
            var updated = FindFolder(workspace, id) with { Name = name.Trim() };
            await WriteAtomicAsync(ReplaceFolder(workspace, updated));
            return updated;
        });
    }

    public Task<Workspace> DeleteFolderAsync(string id) => LockedAsync(async () =>
    {
        var workspace = await LoadOrCreateUnlockedAsync();
        var folder = FindFolder(workspace, id);
        var updated = workspace with { Folders = workspace.Folders.Where(f => f.Id != id).ToList() };
        await DeleteWithRecoveryAsync(workspace, updated, folder.Recordings);
        return updated;
    });

    public Task<Workspace> DeleteDocumentAsync(string id) => LockedAsync(async () =>
    {
        var workspace = await LoadOrCreateUnlockedAsync();
        if (!workspace.Documents.Any(d => d.Id == id) && !workspace.Folders.Any(f => f.Documents.Any(d => d.Id == id))) throw new InvalidDataException("文档或模板不存在。");
        var updated = workspace with { Documents = workspace.Documents.Where(d => d.Id != id).ToList(), Folders = workspace.Folders.Select(f => f with { Documents = f.Documents.Where(d => d.Id != id).ToList() }).ToList() };
        await DeleteWithRecoveryAsync(workspace, updated, []);
        return updated;
    });

    public Task<WorkspaceRecording> SaveRecordingAsync(string folderId, WorkspaceRecording recording) => LockedAsync(async () =>
    {
        var workspace = await LoadOrCreateUnlockedAsync();
        var folder = FindFolder(workspace, folderId);
        ValidateRecording(recording, folderId);
        if (!File.Exists(ResolveRecordingPath(recording.RelativePath))) throw new FileNotFoundException("录音文件尚未生成。");
        if (workspace.Folders.Any(f => f.Id != folderId && f.Recordings.Any(r => r.Id == recording.Id))) throw new InvalidDataException("录音标识已属于其他人员文件夹。");
        if (folder.Recordings.Any(r => r.Id != recording.Id && SamePath(r.RelativePath, recording.RelativePath))) throw new InvalidDataException("录音文件已被其他录音引用。");
        var old = folder.Recordings.FirstOrDefault(r => r.Id == recording.Id);
        if (old is not null && !SamePath(old.RelativePath, recording.RelativePath)) throw new InvalidDataException("已有录音不能更换文件路径。");
        await WriteAtomicAsync(ReplaceFolder(workspace, folder with { Recordings = ReplaceOrAppend(folder.Recordings, recording, r => r.Id) }));
        return recording;
    });

    public Task<Workspace> DeleteRecordingAsync(string folderId, string recordingId) => LockedAsync(async () =>
    {
        var workspace = await LoadOrCreateUnlockedAsync();
        var folder = FindFolder(workspace, folderId);
        var recording = folder.Recordings.FirstOrDefault(r => r.Id == recordingId) ?? throw new InvalidDataException("录音不存在。");
        var updated = ReplaceFolder(workspace, folder with { Recordings = folder.Recordings.Where(r => r.Id != recordingId).ToList() });
        await DeleteWithRecoveryAsync(workspace, updated, [recording]);
        return updated;
    });

    public string ResolveRecordingPath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || System.IO.Path.IsPathRooted(relativePath)) throw new InvalidDataException("录音路径必须位于当前项目 recordings 目录中。");
        var parts = relativePath.Replace('\\', '/').Split('/');
        if (parts.Length != 3 || !parts[0].Equals("recordings", StringComparison.OrdinalIgnoreCase)
            || parts.Any(p => string.IsNullOrWhiteSpace(p) || p is "." or ".." || p.EndsWith(' ') || p.EndsWith('.') || p.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
            || !System.IO.Path.GetExtension(parts[2]).Equals(".mp3", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("录音路径无效。");
        ValidateId(parts[1]);
        var resolved = System.IO.Path.GetFullPath(System.IO.Path.Combine(DirectoryPath, parts[0], parts[1], parts[2]));
        EnsureNoReparsePoints(resolved);
        return resolved;
    }

    private async Task<T> LockedAsync<T>(Func<Task<T>> action)
    {
        var gate = Gates.GetOrAdd(DirectoryPath, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try { return await action(); } finally { gate.Release(); }
    }

    private async Task<Workspace> LoadOrCreateUnlockedAsync()
    {
        Directory.CreateDirectory(DirectoryPath);
        if (!File.Exists(ManifestPath))
        {
            var created = new Workspace(2, Guid.NewGuid().ToString("N"), new DirectoryInfo(DirectoryPath).Name, DirectoryPath, [], []);
            await WriteAtomicAsync(created);
            return created;
        }
        await using var stream = File.OpenRead(ManifestPath);
        var workspace = await JsonSerializer.DeserializeAsync<Workspace>(stream, JsonOptions) ?? throw new InvalidDataException("项目文件为空，请从备份恢复 workspace.json。");
        if (workspace.SchemaVersion is not (1 or 2) || string.IsNullOrWhiteSpace(workspace.Path)) throw new InvalidDataException("项目文件结构或版本不受支持。");
        if (workspace.SchemaVersion == 2 && workspace.Folders is null) throw new InvalidDataException("项目缺少人员文件夹列表。");
        var upgraded = workspace with { SchemaVersion = 2, Path = DirectoryPath, Folders = workspace.Folders ?? [] };
        ValidateWorkspace(upgraded);
        return upgraded;
    }

    private void ValidateWorkspace(Workspace workspace)
    {
        ValidateId(workspace.Id); ValidateName(workspace.Name);
        if (workspace.Documents is null || workspace.Folders is null) throw new InvalidDataException("项目列表结构无效。");
        var documents = new HashSet<string>(StringComparer.Ordinal);
        foreach (var document in workspace.Documents)
        {
            ValidateDocument(document);
            if (!documents.Add(document.Id)) throw new InvalidDataException("项目存在重复文档标识。");
        }
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var recordings = new HashSet<string>(StringComparer.Ordinal);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in workspace.Folders)
        {
            if (folder is null || folder.Documents is null || folder.Recordings is null) throw new InvalidDataException("人员文件夹结构无效。");
            ValidateId(folder.Id); ValidateName(folder.Name);
            if (!folders.Add(folder.Id)) throw new InvalidDataException("项目存在重复人员文件夹标识。");
            foreach (var document in folder.Documents)
            {
                ValidateDocument(document);
                if (document.Kind != "document" || !documents.Add(document.Id)) throw new InvalidDataException("人员文件夹文档类型或标识无效。");
            }
            foreach (var recording in folder.Recordings)
            {
                ValidateRecording(recording, folder.Id);
                if (!recordings.Add(recording.Id) || !paths.Add(ResolveRecordingPath(recording.RelativePath))) throw new InvalidDataException("项目存在重复录音标识或文件。");
            }
        }
    }

    private void ValidateRecording(WorkspaceRecording? recording, string folderId)
    {
        if (recording is null || recording.DurationMs < 0 || recording.CreatedAt == default) throw new InvalidDataException("录音记录无效。");
        ValidateId(recording.Id); ValidateName(recording.Name); ResolveRecordingPath(recording.RelativePath);
        if (!recording.RelativePath.Replace('\\', '/').Split('/')[1].Equals(folderId, StringComparison.Ordinal)) throw new InvalidDataException("录音文件不属于当前人员文件夹。");
    }

    private static void ValidateDocument(WorkspaceDocument? document)
    {
        if (document is null || document.Kind is not ("document" or "template") || document.Content.ValueKind != JsonValueKind.Object) throw new InvalidDataException("文档缺少有效的类型或编辑内容。");
        ValidateId(document.Id); ValidateName(document.Title);
    }

    private static void ValidateId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 100 || id.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))) throw new InvalidDataException("标识无效。");
    }

    private static void ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100) throw new InvalidDataException("名称必须为 1 至 100 个字符。");
    }

    private static WorkspaceFolder FindFolder(Workspace workspace, string id) => workspace.Folders.FirstOrDefault(f => f.Id == id) ?? throw new InvalidDataException("人员文件夹不存在。");
    private static Workspace ReplaceFolder(Workspace workspace, WorkspaceFolder folder) => workspace with { Folders = workspace.Folders.Select(f => f.Id == folder.Id ? folder : f).ToList() };
    private static List<T> ReplaceOrAppend<T>(List<T> items, T item, Func<T, string> id)
    {
        var result = items.ToList(); var index = result.FindIndex(existing => id(existing) == id(item));
        if (index < 0) result.Add(item); else result[index] = item;
        return result;
    }
    private static bool SamePath(string left, string right) => left.Replace('\\', '/').Equals(right.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);

    // Snapshot before mutation; audio moves only after the atomic manifest commit.
    // Locked audio remain at their original path, recoverable with the snapshot.
    private async Task DeleteWithRecoveryAsync(Workspace previous, Workspace next, IEnumerable<WorkspaceRecording> recordings)
    {
        var trash = System.IO.Path.Combine(DirectoryPath, ".trash", Guid.NewGuid().ToString("N"));
        EnsureNoReparsePoints(trash);
        Directory.CreateDirectory(trash);
        await File.WriteAllTextAsync(System.IO.Path.Combine(trash, "workspace.json"), JsonSerializer.Serialize(previous, JsonOptions));
        await WriteAtomicAsync(next);
        foreach (var recording in recordings)
        {
            var source = ResolveRecordingPath(recording.RelativePath);
            if (!File.Exists(source)) continue;
            try { File.Move(source, System.IO.Path.Combine(trash, System.IO.Path.GetFileName(source))); }
            catch (IOException) { /* Snapshot retains the original path for recovery. */ }
            catch (UnauthorizedAccessException) { /* Keep inaccessible media in place. */ }
        }
    }

    private static void EnsureNoReparsePoints(string path)
    {
        for (var item = new FileInfo(path); item is not null; item = item.Directory is { } parent ? new FileInfo(parent.FullName) : null)
            if ((File.Exists(item.FullName) || Directory.Exists(item.FullName)) && (File.GetAttributes(item.FullName) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("项目资料路径不能经过符号链接或目录联接。");
    }

    private async Task WriteAtomicAsync(Workspace workspace)
    {
        ValidateWorkspace(workspace);
        var temporary = System.IO.Path.Combine(DirectoryPath, ".workspace-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.WriteThrough | FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, workspace, JsonOptions);
                await stream.FlushAsync(); stream.Flush(flushToDisk: true);
            }
            if (File.Exists(ManifestPath)) File.Replace(temporary, ManifestPath, null); else File.Move(temporary, ManifestPath);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
