using System.Text.Json;

namespace RecDog.Core;

public sealed record DocumentVersion(string Id, string DocumentId, string Label, string Reason, DateTimeOffset CreatedAt, WorkspaceDocument Document);
public sealed record DocumentVersionInfo(string Id, string DocumentId, string Label, string Reason, DateTimeOffset CreatedAt, string Title);

public sealed partial class WorkspaceRepository
{
    public Task<List<DocumentVersionInfo>> ListDocumentVersionsAsync(string id) => LockedAsync(async () =>
    {
        FindHistoryDocument(await LoadOrCreateUnlockedAsync(), id);
        return await ListHistoryUnlockedAsync(id);
    });
    public Task<DocumentVersion> ReadDocumentVersionAsync(string id, string versionId) => LockedAsync(async () =>
    {
        FindHistoryDocument(await LoadOrCreateUnlockedAsync(), id);
        return await ReadHistoryUnlockedAsync(id, versionId);
    });
    public Task<DocumentVersion> CreateDocumentVersionAsync(string id, string label) => LockedAsync(async () =>
    {
        var document = FindHistoryDocument(await LoadOrCreateUnlockedAsync(), id);
        if (label is null || label.Length > 100) throw new InvalidDataException("版本名称最多 100 个字符。");
        return await WriteHistoryUnlockedAsync(document, label.Trim(), "manual");
    });
    public Task<WorkspaceDocument> RestoreDocumentVersionAsync(string id, string versionId, DateTimeOffset expectedUpdatedAt) => LockedAsync(async () =>
    {
        var workspace = await LoadOrCreateUnlockedAsync();
        var current = FindHistoryDocument(workspace, id);
        if (current.UpdatedAt != expectedUpdatedAt) throw new InvalidOperationException("文档已更新，请重新打开历史版本后再恢复。当前内容未覆盖。");
        var version = await ReadHistoryUnlockedAsync(id, versionId);
        if (version.Document.Kind != current.Kind) throw new InvalidDataException("历史版本类型与当前文档不一致。");
        await WriteHistoryUnlockedAsync(current, "恢复前自动备份", "beforeRestore");
        var restored = current with { Title = version.Document.Title, Content = version.Document.Content.Clone(), UpdatedAt = DateTimeOffset.UtcNow };
        ValidateDocument(restored);
        var updated = workspace with
        {
            Documents = workspace.Documents.Select(d => d.Id == id ? restored : d).ToList(),
            Folders = workspace.Folders.Select(f => f with { Documents = f.Documents.Select(d => d.Id == id ? restored : d).ToList() }).ToList()
        };
        await WriteAtomicAsync(updated);
        return restored;
    });
    private static WorkspaceDocument FindHistoryDocument(Workspace workspace, string id)
    {
        ValidateId(id);
        return workspace.Documents.Concat(workspace.Folders.SelectMany(f => f.Documents)).SingleOrDefault(d => d.Id == id)
            ?? throw new InvalidDataException("文档不存在。");
    }
    private string HistoryPath(string id, string? versionId = null)
    {
        ValidateId(id);
        if (versionId is not null) ValidateId(versionId);
        var path = Path.Combine(DirectoryPath, "history", id);
        if (versionId is not null) path = Path.Combine(path, versionId + ".json");
        EnsureNoReparsePoints(path);
        return path;
    }
    private async Task<List<DocumentVersionInfo>> ListHistoryUnlockedAsync(string id)
    {
        var directory = HistoryPath(id);
        if (!Directory.Exists(directory)) return [];
        var result = new List<DocumentVersionInfo>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            var version = await ReadHistoryUnlockedAsync(id, Path.GetFileNameWithoutExtension(file));
            result.Add(new(version.Id, id, version.Label, version.Reason, version.CreatedAt, version.Document.Title));
        }
        return result.OrderByDescending(v => v.CreatedAt).ThenBy(v => v.Id).ToList();
    }
    private async Task<DocumentVersion> ReadHistoryUnlockedAsync(string id, string versionId)
    {
        var file = HistoryPath(id, versionId);
        if (!File.Exists(file)) throw new FileNotFoundException("历史版本不存在。");
        await using var stream = File.OpenRead(file);
        var version = await JsonSerializer.DeserializeAsync<DocumentVersion>(stream, JsonOptions);
        if (version is null || version.Id != versionId || version.DocumentId != id || version.Document is null || version.Document.Id != id || version.CreatedAt == default || version.Label is null || version.Reason is not ("manual" or "automatic" or "beforeRestore"))
            throw new InvalidDataException("历史版本文件无效。");
        ValidateDocument(version.Document);
        return version;
    }
    private async Task<DocumentVersion> WriteHistoryUnlockedAsync(WorkspaceDocument document, string label, string reason)
    {
        var version = new DocumentVersion(Guid.NewGuid().ToString("N"), document.Id, label, reason, DateTimeOffset.UtcNow, document with { Content = document.Content.Clone() });
        var file = HistoryPath(document.Id, version.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var temporary = file + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, version, JsonOptions);
                await stream.FlushAsync(); stream.Flush(true);
            }
            File.Move(temporary, file);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return version;
    }
    private async Task CheckpointDocumentAsync(WorkspaceDocument? previous, WorkspaceDocument next)
    {
        if (previous is null || (previous.Title == next.Title && previous.Kind == next.Kind && previous.Content.GetRawText() == next.Content.GetRawText())) return;
        var directory = HistoryPath(previous.Id);
        // Read the newest file only on save, keeping two-second autosave independent of history size.
        var latest = Directory.Exists(directory) ? Directory.EnumerateFiles(directory, "*.json").MaxBy(File.GetLastWriteTimeUtc) : null;
        if (latest is not null && DateTime.UtcNow - File.GetLastWriteTimeUtc(latest) < TimeSpan.FromMinutes(5)) return;
        await WriteHistoryUnlockedAsync(previous, "自动保存检查点", "automatic");
    }
}
