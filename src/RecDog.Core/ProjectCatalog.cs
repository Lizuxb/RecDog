using System.Text.Json;

namespace RecDog.Core;

public sealed record ProjectEntry(string Name, string Path);

/// <summary>Local project history; each project's manifest remains its source of truth.</summary>
public sealed class ProjectCatalog(string catalogFile, string defaultRoot)
{
    public string DefaultRoot { get; } = Path.GetFullPath(defaultRoot);
    private async Task<List<string>> ReadAsync()
    {
        if (!File.Exists(catalogFile)) return [];
        await using var stream = File.OpenRead(catalogFile);
        return await JsonSerializer.DeserializeAsync<List<string>>(stream) ?? [];
    }

    public static async Task<Workspace?> OpenExistingAsync(string path)
    {
        if (!File.Exists(Path.Combine(path, "workspace.json"))) return null;
        return await new WorkspaceRepository(path).LoadOrCreateAsync();
    }

    public async Task<List<ProjectEntry>> ListAsync()
    {
        var result = new List<ProjectEntry>();
        foreach (var path in (await ReadAsync()).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try { var workspace = await OpenExistingAsync(path); if (workspace is not null) result.Add(new(workspace.Name, workspace.Path)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        }
        return result;
    }

    public async Task RememberAsync(string path)
    {
        var paths = await ReadAsync();
        paths.RemoveAll(item => item.Equals(path, StringComparison.OrdinalIgnoreCase));
        paths.Insert(0, Path.GetFullPath(path));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(catalogFile))!);
        var temporary = catalogFile + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(paths));
        File.Move(temporary, catalogFile, true);
    }

    public async Task<string> NextNameAsync()
    {
        var names = (await ListAsync()).Select(item => item.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var n = 1; ; n++)
        {
            var name = $"项目{n:00}";
            if (!names.Contains(name) && !Directory.Exists(Path.Combine(DefaultRoot, name)) && !File.Exists(Path.Combine(DefaultRoot, name))) return name;
        }
    }

    public async Task<Workspace> CreateAsync(string name, string? parent = null)
    {
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80 || name is "." or ".." || name.EndsWith('.') || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException("项目名称不能包含路径符号，且最多 80 个字符。");
        var destination = Path.Combine(Path.GetFullPath(parent ?? DefaultRoot), name);
        if (Directory.Exists(destination) || File.Exists(destination)) throw new InvalidDataException("同名文件夹已存在，请修改项目名称。");
        Directory.CreateDirectory(destination);
        var workspace = await new WorkspaceRepository(destination).LoadOrCreateAsync();
        await RememberAsync(destination);
        return workspace;
    }
}
