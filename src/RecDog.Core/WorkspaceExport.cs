using System.IO.Compression;

namespace RecDog.Core;

public sealed record ExportSelection(string[] DocumentIds, string[] RecordingIds, string[] FolderIds, bool IncludeProject = false);
public sealed record ExportDocument(string Id, string RelativePath, WorkspaceDocument Document);

/// <summary>A frozen selection exported into a staging folder, published only after every file succeeds.</summary>
public sealed class WorkspaceExportSession : IAsyncDisposable
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public string RootName { get; private set; } = "";
    public List<ExportDocument> Documents { get; } = [];
    public int RecordingCount { get; private set; }
    private string staging = "";
    private string destination = "";
    private bool completed;
    private readonly HashSet<string> written = [];

    public static async Task<WorkspaceExportSession> CreateAsync(WorkspaceRepository repository, string parent, ExportSelection selection)
    {
        var workspace = await repository.LoadOrCreateAsync();
        if (selection.DocumentIds is null || selection.RecordingIds is null || selection.FolderIds is null) throw new InvalidDataException("缺少导出范围。");
        parent = Path.GetFullPath(parent);
        if (parent.Equals(repository.DirectoryPath, StringComparison.OrdinalIgnoreCase) || parent.StartsWith(repository.DirectoryPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("请选择项目目录之外的导出位置。");
        CheckPath(parent);
        var documents = selection.DocumentIds.ToHashSet();
        var recordings = selection.RecordingIds.ToHashSet();
        var folders = selection.FolderIds.ToHashSet();
        if (documents.Count + recordings.Count + folders.Count == 0 && !selection.IncludeProject) throw new InvalidDataException("请至少选择一项内容。");
        if (documents.Except(workspace.Documents.Concat(workspace.Folders.SelectMany(f => f.Documents)).Select(d => d.Id)).Any()
            || recordings.Except(workspace.Folders.SelectMany(f => f.Recordings).Select(r => r.Id)).Any()
            || folders.Except(workspace.Folders.Select(f => f.Id)).Any()) throw new InvalidDataException("所选资料已不存在，请重新打开导出窗口。");
        Directory.CreateDirectory(parent);
        var session = new WorkspaceExportSession();
        session.destination = AvailablePath(parent, SafeName(workspace.Name), "");
        session.RootName = Path.GetFileName(session.destination);
        session.staging = Path.Combine(parent, ".RecDog-export-" + session.Id);
        Directory.CreateDirectory(session.staging);
        var allocated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string Allocate(string directory, string name, string extension)
        {
            var stem = SafeName(name); var candidate = Path.Combine(directory, stem + extension);
            for (var n = 2; !allocated.Add(candidate); n++) candidate = Path.Combine(directory, stem + " (" + n + ")" + extension);
            return candidate;
        }
        void AddDocument(WorkspaceDocument document, string folder)
        {
            if (!documents.Contains(document.Id)) return;
            var name = document.Title.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) ? document.Title[..^5] : document.Title;
            session.Documents.Add(new(document.Id, Allocate(folder, name, ".docx").Replace('\\', '/'), document));
        }
        try
        {
            // Allocate person names before root documents to avoid file/directory name collisions.
            var personNames = workspace.Folders.ToDictionary(f => f.Id, f => Allocate("", f.Name, ""));
            var templatePath = Allocate("", "模板导出", "");
            if (workspace.Documents.Any(d => d.Kind == "template" && documents.Contains(d.Id)))
                Directory.CreateDirectory(Path.Combine(session.staging, templatePath));
            foreach (var doc in workspace.Documents) AddDocument(doc, doc.Kind == "template" ? templatePath : "");
            foreach (var person in workspace.Folders)
            {
                if (!folders.Contains(person.Id) && !person.Documents.Any(d => documents.Contains(d.Id)) && !person.Recordings.Any(r => recordings.Contains(r.Id))) continue;
                var personPath = personNames[person.Id];
                Directory.CreateDirectory(Path.Combine(session.staging, personPath));
                foreach (var document in person.Documents) AddDocument(document, personPath);
                foreach (var recording in person.Recordings.Where(r => recordings.Contains(r.Id)))
                {
                    var name = recording.Name.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ? recording.Name[..^4] : recording.Name;
                    var target = Path.Combine(session.staging, Allocate(personPath, name, ".mp3"));
                    await RecordingExport.CopyForPlaybackAsync(repository.ResolveRecordingPath(recording.RelativePath), target);
                    session.RecordingCount++;
                }
            }
            return session;
        }
        catch { await session.DisposeAsync(); throw; }
    }

    public async Task WriteDocumentAsync(string id, byte[] bytes)
    {
        if (completed) throw new InvalidOperationException("导出会话已结束。");
        var document = Documents.SingleOrDefault(d => d.Id == id) ?? throw new InvalidDataException("文档不在本次导出范围内。");
        if (written.Contains(id)) throw new InvalidDataException("文档已导出。");
        if (bytes.Length > 128 * 1024 * 1024) throw new InvalidDataException("单份导出文档过大。");
        using (var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
            if (zip.GetEntry("[Content_Types].xml") is null || zip.GetEntry("word/document.xml") is null) throw new InvalidDataException("无效的 DOCX 文档。");
        CheckPath(staging);
        var target = Path.Combine(staging, document.RelativePath);
        CheckPath(target);
        await using (var file = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true)) await file.WriteAsync(bytes);
        written.Add(id);
    }

    public Task<string> CompleteAsync()
    {
        if (completed) throw new InvalidOperationException("导出会话已结束。");
        if (written.Count != Documents.Count) throw new InvalidOperationException("部分文档尚未转换，未生成导出文件夹。");
        CheckPath(staging); CheckPath(destination);
        Directory.Move(staging, destination);
        completed = true;
        return Task.FromResult(destination);
    }

    public ValueTask DisposeAsync()
    {
        if (!completed && staging.Length > 0 && Directory.Exists(staging)) { CheckPath(staging); Directory.Delete(staging, true); }
        completed = true;
        return ValueTask.CompletedTask;
    }

    private static string SafeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        if (name.Length > 80) name = name[..80].TrimEnd(' ', '.');
        if (name.Length == 0 || name is "." or "..") name = "未命名";
        var stem = name.Split('.')[0];
        if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(stem, StringComparer.OrdinalIgnoreCase)) name = "_" + name;
        return name;
    }
    private static string AvailablePath(string parent, string name, string extension)
    {
        var path = Path.Combine(parent, name + extension);
        for (var i = 2; Directory.Exists(path) || File.Exists(path); i++) path = Path.Combine(parent, name + " (" + i + ")" + extension);
        return path;
    }
    private static void CheckPath(string path)
    {
        for (var current = new FileInfo(Path.GetFullPath(path)); current is not null; current = current.Directory is { } parent ? new FileInfo(parent.FullName) : null)
            if ((File.Exists(current.FullName) || Directory.Exists(current.FullName)) && (File.GetAttributes(current.FullName) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("导出路径不能经过目录联接或符号链接。");
    }
}
