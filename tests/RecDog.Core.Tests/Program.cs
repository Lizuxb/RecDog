using System.Text.Json;
using RecDog.Core;

var tests = new (string Name, Func<string, Task> Run)[]
{
    ("Project catalog creates numbered folders and survives reopening", async path => {
        var catalog = new ProjectCatalog(Path.Combine(path, "catalog.json"), Path.Combine(path, "Projects"));
        Equal(0, (await catalog.ListAsync()).Count);
        var missing = Path.Combine(path, "moved-project");
        Equal<Workspace?>(null, await ProjectCatalog.OpenExistingAsync(missing));
        Equal(false, Directory.Exists(missing));
        var first = await catalog.CreateAsync("项目01");
        Equal("项目01", first.Name); Equal(true, File.Exists(Path.Combine(first.Path, "workspace.json")));
        Equal("项目02", await catalog.NextNameAsync());
        await catalog.RememberAsync(first.Path); await catalog.RememberAsync(first.Path);
        Equal(1, (await new ProjectCatalog(Path.Combine(path, "catalog.json"), Path.Combine(path, "Projects")).ListAsync()).Count);
        await Throws(async () => await catalog.CreateAsync("../错误"));
        await Throws(async () => await catalog.CreateAsync("项目01"));
        Directory.Move(first.Path, first.Path + "-removed");
        Equal(0, (await catalog.ListAsync()).Count);
    }),
    ("Document creation time survives edits and reopening", async path => {
        var repo = new WorkspaceRepository(path); await repo.CreateFolderAsync("person", "人员");
        var first = await repo.SaveFolderDocumentAsync("person", "doc", "记录", Content("a"));
        await Task.Delay(20);
        var second = await repo.SaveFolderDocumentAsync("person", "doc", "改名", Content("b"));
        Equal(first.CreatedAt, second.CreatedAt);
        if (first.CreatedAt is null) throw new Exception("Missing creation time");
        Equal(first.CreatedAt, (await new WorkspaceRepository(path).LoadOrCreateAsync()).Folders[0].Documents[0].CreatedAt);
    }),
    ("Folder ownership survives reopening and cannot be stolen", async path => {
        var repo = new WorkspaceRepository(path);
        await repo.CreateFolderAsync("alice", "张三");
        await repo.CreateFolderAsync("bob", "李四");
        await repo.SaveFolderDocumentAsync("alice", "doc", "访谈", Content("中文内容"));
        await Throws(async () => await repo.SaveFolderDocumentAsync("bob", "doc", "误归属", Content("x")));
        await Throws(async () => await repo.SaveDocumentAsync("doc", "误归属", "template", Content("x")));
        await Throws(async () => await repo.SaveFolderDocumentAsync("missing", "new", "错误", Content("x")));
        var loaded = await repo.LoadOrCreateAsync();
        Equal("中文内容", loaded.Folders[0].Documents.Single().Content.GetProperty("main")[0].GetProperty("value").GetString());
        Equal(0, loaded.Folders[1].Documents.Count);
    }),
    ("Legacy manifest is read without mutation and upgraded on next write", async path => {
        Directory.CreateDirectory(path);
        var file = Path.Combine(path, "workspace.json");
        var legacy = JsonSerializer.Serialize(new { schemaVersion = 1, id = "legacy", name = "旧项目", path, documents = new[] { new WorkspaceDocument("old", "旧文档", "document", DateTimeOffset.UtcNow, Content("旧内容")) } });
        await File.WriteAllTextAsync(file, legacy);
        var repo = new WorkspaceRepository(path);
        var loaded = await repo.LoadOrCreateAsync();
        Equal(2, loaded.SchemaVersion); Equal(0, loaded.Folders.Count); Equal("old", loaded.Documents.Single().Id);
        Equal(legacy, await File.ReadAllTextAsync(file));
        await repo.CreateFolderAsync("new", "新人");
        loaded = await new WorkspaceRepository(path).LoadOrCreateAsync();
        Equal("旧内容", loaded.Documents.Single().Content.GetProperty("main")[0].GetProperty("value").GetString());
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(file)); Equal(2, json.RootElement.GetProperty("schemaVersion").GetInt32());
    }),
    ("Renaming project and folder preserves documents", async path => {
        var repo = new WorkspaceRepository(path); await repo.CreateFolderAsync("person", "原名");
        await repo.SaveFolderDocumentAsync("person", "doc", "记录", Content("内容"));
        Equal("新项目", (await repo.RenameAsync("新项目")).Name);
        Equal("新姓名", (await repo.RenameFolderAsync("person", "新姓名")).Name);
        Equal(1, (await repo.LoadOrCreateAsync()).Folders.Single().Documents.Count);
        await Throws(async () => await repo.RenameFolderAsync("missing", "姓名"));
        await Throws(async () => await repo.RenameAsync("  "));
    }),
    ("Recording paths reject traversal rooted and alias paths", async path => {
        var repo = new WorkspaceRepository(path); await repo.CreateFolderAsync("person", "人员");
        foreach (var invalid in new[] { "../outside.mp3", "recordings/../outside.mp3", "C:/outside.mp3", "recordings/person/../../outside.mp3", "sources/audio.mp3", "recordings/person/a.wav", "recordings/person/a.mp3:stream", "recordings/person/./a.mp3" })
            await Throws(() => Task.FromResult(repo.ResolveRecordingPath(invalid)));
        Equal(Path.Combine(path, "recordings", "person", "a.mp3"), repo.ResolveRecordingPath("recordings/person/a.mp3"));
    }),
    ("Recordings belong to one folder and delete is recoverable", async path => {
        var repo = new WorkspaceRepository(path); await repo.CreateFolderAsync("person", "人员");
        var audio = repo.ResolveRecordingPath("recordings/person/audio.mp3"); Directory.CreateDirectory(Path.GetDirectoryName(audio)!); await File.WriteAllTextAsync(audio, "audio-bytes");
        var recording = new WorkspaceRecording("audio", "录音", "recordings/person/audio.mp3", 1234, DateTimeOffset.UtcNow);
        await repo.SaveRecordingAsync("person", recording);
        Equal(1234L, (await new WorkspaceRepository(path).LoadOrCreateAsync()).Folders.Single().Recordings.Single().DurationMs);
        await Throws(async () => await repo.SaveRecordingAsync("missing", recording));
        await Throws(async () => await repo.SaveRecordingAsync("person", recording with { Id = "bad", RelativePath = "recordings/other/audio.mp3" }));
        var manifest = Path.Combine(path, "workspace.json"); var before = await File.ReadAllTextAsync(manifest);
        using (var locked = new FileStream(manifest, FileMode.Open, FileAccess.Read, FileShare.Read)) {
            await Throws(async () => await repo.DeleteRecordingAsync("person", "audio"));
            Equal("audio-bytes", await File.ReadAllTextAsync(audio)); Equal(before, await File.ReadAllTextAsync(manifest));
        }
        Equal(0, (await repo.DeleteRecordingAsync("person", "audio")).Folders.Single().Recordings.Count);
        Equal(true, Directory.GetFiles(Path.Combine(path, ".trash"), "*.mp3", SearchOption.AllDirectories).Any());
    }),
    ("Deleting folders and templates preserves unrelated data", async path => {
        var repo = new WorkspaceRepository(path); await repo.CreateFolderAsync("one", "人员一"); await repo.CreateFolderAsync("two", "人员二");
        await repo.SaveFolderDocumentAsync("one", "one-doc", "记录一", Content("内容一"));
        await repo.SaveFolderDocumentAsync("two", "two-doc", "记录二", Content("内容二"));
        await repo.SaveDocumentAsync("tpl", "模板", "template", Content("格式"));
        Equal(0, (await repo.DeleteDocumentAsync("tpl")).Documents.Count);
        var loaded = await repo.DeleteFolderAsync("one"); Equal("two-doc", loaded.Folders.Single().Documents.Single().Id);
        await Throws(async () => await repo.DeleteDocumentAsync("missing")); await Throws(async () => await repo.DeleteFolderAsync("missing"));
    }),
    ("Duplicate manifest IDs and folder templates are rejected without mutation", async path => {
        var repo = new WorkspaceRepository(path); await repo.CreateFolderAsync("person", "人员"); await repo.SaveDocumentAsync("doc", "记录", "document", Content("x"));
        var loaded = await repo.LoadOrCreateAsync(); var file = Path.Combine(path, "workspace.json");
        foreach (var bad in new[] {
            loaded with { Folders = [loaded.Folders[0] with { Documents = [loaded.Documents.Single()] }] },
            loaded with { Folders = [loaded.Folders[0], loaded.Folders[0]] },
            loaded with { Folders = [loaded.Folders[0] with { Documents = [loaded.Documents.Single() with { Id = "template", Kind = "template" }] }] }
        }) {
            var bytes = JsonSerializer.Serialize(bad, WorkspaceRepository.JsonOptions); await File.WriteAllTextAsync(file, bytes);
            await Throws(async () => await repo.LoadOrCreateAsync()); Equal(bytes, await File.ReadAllTextAsync(file));
        }
    }),
    ("Folder deletion failure keeps recordings and ownership intact", async path => {
        var repo = new WorkspaceRepository(path); await repo.CreateFolderAsync("person", "人员");
        var audio = repo.ResolveRecordingPath("recordings/person/audio.mp3"); Directory.CreateDirectory(Path.GetDirectoryName(audio)!); await File.WriteAllTextAsync(audio, "audio-bytes");
        await repo.SaveRecordingAsync("person", new WorkspaceRecording("audio", "录音", "recordings/person/audio.mp3", 1234, DateTimeOffset.UtcNow));
        var file = Path.Combine(path, "workspace.json"); var before = await File.ReadAllTextAsync(file);
        using (var locked = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read)) {
            await Throws(async () => await repo.DeleteFolderAsync("person"));
            Equal(before, await File.ReadAllTextAsync(file)); Equal("audio-bytes", await File.ReadAllTextAsync(audio));
        }
        Equal(1, (await repo.LoadOrCreateAsync()).Folders.Single().Recordings.Count);
    }),
    ("Missing audio invalid metadata and duplicate media references are rejected", async path => {
        var repo = new WorkspaceRepository(path); await repo.CreateFolderAsync("person", "人员");
        var recording = new WorkspaceRecording("audio", "录音", "recordings/person/audio.mp3", 1234, DateTimeOffset.UtcNow);
        await Throws(async () => await repo.SaveRecordingAsync("person", recording));
        var audio = repo.ResolveRecordingPath(recording.RelativePath); Directory.CreateDirectory(Path.GetDirectoryName(audio)!); await File.WriteAllTextAsync(audio, "audio");
        await Throws(async () => await repo.SaveRecordingAsync("person", recording with { DurationMs = -1 }));
        await repo.SaveRecordingAsync("person", recording);
        await Throws(async () => await repo.SaveRecordingAsync("person", recording with { Id = "duplicate" }));
        Equal(1, (await repo.LoadOrCreateAsync()).Folders.Single().Recordings.Count);
    }),
    ("Folders reject invalid IDs and Windows path case aliases", async path => {
        var repo = new WorkspaceRepository(path); await repo.CreateFolderAsync("person", "人员");
        await Throws(async () => await repo.CreateFolderAsync("PERSON", "另一人"));
        await Throws(async () => await repo.CreateFolderAsync("../outside", "错误"));
        await Throws(async () => await repo.CreateFolderAsync("empty", "  "));
        Equal(1, (await repo.LoadOrCreateAsync()).Folders.Count);
    }),
    ("Chinese document survives reopening", async path => {
        var repo = new WorkspaceRepository(path);
        await repo.LoadOrCreateAsync();
        await repo.SaveDocumentAsync("interview", "访谈记录", "document", Content("你好，世界。"));
        var reopened = await new WorkspaceRepository(path).LoadOrCreateAsync();
        Equal("访谈记录", reopened.Documents.Single().Title);
        Equal("你好，世界。", reopened.Documents.Single().Content.GetProperty("main")[0].GetProperty("value").GetString());
    }),
    ("Updating keeps a stable ID without duplicating", async path => {
        var repo = new WorkspaceRepository(path);
        await repo.SaveDocumentAsync("one", "旧标题", "document", Content("旧文"));
        await repo.SaveDocumentAsync("one", "新标题", "template", Content("新文"));
        var saved = (await repo.LoadOrCreateAsync()).Documents.Single();
        Equal("one", saved.Id); Equal("新标题", saved.Title); Equal("template", saved.Kind);
        Equal("新文", saved.Content.GetProperty("main")[0].GetProperty("value").GetString());
    }),
    ("Malformed and unsupported manifests are rejected without mutation", async path => {
        Directory.CreateDirectory(path);
        foreach (var bad in new[] { "{broken", "{}", "{\"schemaVersion\":99,\"id\":\"x\",\"name\":\"x\",\"path\":\"x\",\"documents\":[]}", "{\"schemaVersion\":1,\"id\":\"x\",\"name\":\"x\",\"documents\":[{\"id\":\"x\",\"title\":\"x\",\"kind\":\"bad\",\"content\":{}}]}" }) {
            await File.WriteAllTextAsync(Path.Combine(path, "workspace.json"), bad);
            await Throws(async () => await new WorkspaceRepository(path).LoadOrCreateAsync());
            Equal(bad, await File.ReadAllTextAsync(Path.Combine(path, "workspace.json")));
        }
    }),
    ("Failed atomic save retains previous bytes and document", async path => {
        var repo = new WorkspaceRepository(path);
        await repo.SaveDocumentAsync("one", "已保存", "document", Content("原文"));
        var file = Path.Combine(path, "workspace.json");
        var previous = await File.ReadAllTextAsync(file);
        using (var locked = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read)) {
            await Throws(async () => await repo.SaveDocumentAsync("one", "不应保存", "document", Content("失败")));
            Equal(previous, await File.ReadAllTextAsync(file));
        }
        Equal("已保存", (await repo.LoadOrCreateAsync()).Documents.Single().Title);
        Equal(0, Directory.GetFiles(path, "*.tmp").Length);
    }),
    ("Concurrent repositories serialize writes without losing documents", async path => {
        await Task.WhenAll(Enumerable.Range(0, 24).Select(i => new WorkspaceRepository(path).SaveDocumentAsync($"doc-{i}", $"记录 {i}", "document", Content($"内容 {i}"))));
        var workspace = await new WorkspaceRepository(path).LoadOrCreateAsync();
        Equal(24, workspace.Documents.Count); Equal(24, workspace.Documents.Select(d => d.Id).Distinct().Count());
    }),
    ("Persisted external path cannot redirect repository writes", async path => {
        Directory.CreateDirectory(path);
        await File.WriteAllTextAsync(Path.Combine(path, "workspace.json"), "{\"schemaVersion\":1,\"id\":\"x\",\"name\":\"项目\",\"path\":\"C:/outside\",\"documents\":[]}");
        var repo = new WorkspaceRepository(path);
        Equal(Path.GetFullPath(path), (await repo.LoadOrCreateAsync()).Path);
        await repo.SaveDocumentAsync("one", "当前目录", "document", Content("测试"));
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(path, "workspace.json")));
        Equal(Path.GetFullPath(path), json.RootElement.GetProperty("path").GetString());
    }),
    ("Invalid document does not modify manifest", async path => {
        var repo = new WorkspaceRepository(path);
        await repo.LoadOrCreateAsync();
        var file = Path.Combine(path, "workspace.json"); var before = await File.ReadAllTextAsync(file);
        await Throws(async () => await repo.SaveDocumentAsync("bad", "错误", "other", Content("x")));
        await Throws(async () => await repo.SaveDocumentAsync("", "错误", "document", Content("x")));
        Equal(before, await File.ReadAllTextAsync(file));
    })
};
var root = Path.Combine(Path.GetTempPath(), "RecDog-tests-" + Guid.NewGuid().ToString("N"));
var failures = 0;
try {
    foreach (var (name, run) in tests) {
        try { await run(Path.Combine(root, Guid.NewGuid().ToString("N"))); Console.WriteLine("PASS " + name); }
        catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
    }
} finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
Console.WriteLine($"{tests.Length - failures}/{tests.Length} passed");
return failures == 0 ? 0 : 1;

static JsonElement Content(string text) => JsonSerializer.SerializeToElement(new { main = new[] { new { value = text } } });
static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}"); }
static async Task Throws(Func<Task> action) { try { await action(); } catch (Exception ex) when (ex is not NotImplementedException) { return; } throw new Exception("Expected a rejected operation"); }
