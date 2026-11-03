using System.Text.Json;
using RecDog.Core;

var tests = new (string Name, Func<string, Task> Run)[]
{
    ("Recording export removes ID3 without changing MP3 frames or source", async path => {
        var repo = new WorkspaceRepository(path); await repo.CreateFolderAsync("p", "人员甲");
        var source = repo.ResolveRecordingPath("recordings/p/tagged.mp3"); Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        var audio = new byte[] { 0xff, 0xfb, 0x90, 0x64, 1, 2, 3, 4 };
        // 421-byte metadata exercises multi-byte synchsafe size parsing.
        var bytes = new byte[431 + audio.Length];
        new byte[] { 73, 68, 51, 3, 0, 0, 0, 0, 3, 37 }.CopyTo(bytes, 0); audio.CopyTo(bytes, 431);
        await File.WriteAllBytesAsync(source, bytes);
        await repo.SaveRecordingAsync("p", new("r", "录音", "recordings/p/tagged.mp3", 1000, DateTimeOffset.UtcNow));
        await using var session = await WorkspaceExportSession.CreateAsync(repo, path + "-exports", new([], ["r"], []));
        var output = await session.CompleteAsync();
        var exported = await File.ReadAllBytesAsync(Path.Combine(output, "人员甲", "录音.mp3"));
        var original = await File.ReadAllBytesAsync(source);
        Equal(true, audio.SequenceEqual(exported));
        Equal(true, bytes.SequenceEqual(original));
    }),
    ("Templates export into a separate folder within the project only when selected", async path => {
        var repo = new WorkspaceRepository(path);
        await repo.CreateFolderAsync("p", "人员甲");
        await repo.SaveFolderDocumentAsync("p", "d", "记录", Content("正文"));
        await repo.SaveDocumentAsync("t", "通用模板", "template", Content("模板"));
        await using (var project = await WorkspaceExportSession.CreateAsync(repo, path + "-exports", new(["d"], [], ["p"], true))) {
            Equal(1, project.Documents.Count);
            await project.WriteDocumentAsync("d", Docx());
            var output = await project.CompleteAsync();
            Equal(false, Directory.Exists(Path.Combine(output, "模板导出")));
        }
        await using var templates = await WorkspaceExportSession.CreateAsync(repo, path + "-exports", new(["t"], [], []));
        Equal("模板导出/通用模板.docx", templates.Documents.Single().RelativePath);
        await templates.WriteDocumentAsync("t", Docx());
        var templateOutput = await templates.CompleteAsync();
        Equal(true, File.Exists(Path.Combine(templateOutput, "模板导出", "通用模板.docx")));
        Equal(false, Directory.Exists(Path.Combine(templateOutput, "人员甲")));
    }),
    ("Selected recording export keeps project and person hierarchy without copying unrelated data", async path => {
        var repo = new WorkspaceRepository(path); await repo.CreateFolderAsync("p", "人员甲");
        var media = repo.ResolveRecordingPath("recordings/p/访谈录音.mp3"); Directory.CreateDirectory(Path.GetDirectoryName(media)!); await File.WriteAllBytesAsync(media, [1,2,3]);
        await repo.SaveRecordingAsync("p", new("r", "访谈录音", "recordings/p/访谈录音.mp3", 1000, DateTimeOffset.UtcNow));
        await repo.SaveFolderDocumentAsync("p", "doc", "不选的文档", Content("正文"));
        await using var session = await WorkspaceExportSession.CreateAsync(repo, path + "-exports", new([], ["r"], []));
        Equal(0, session.Documents.Count);
        var output = await session.CompleteAsync();
        Equal(true, File.Exists(Path.Combine(output, "人员甲", "访谈录音.mp3")));
        Equal(1, Directory.GetFiles(output, "*", SearchOption.AllDirectories).Length);
        Equal(true, File.Exists(media));
    }),
    ("Document export avoids name collisions and preserves selected empty personnel folders", async path => {
        var repo = new WorkspaceRepository(path); await repo.CreateFolderAsync("p1", "人员"); await repo.CreateFolderAsync("p2", "人员");
        await repo.SaveFolderDocumentAsync("p1", "d1", "同名", Content("一")); await repo.SaveFolderDocumentAsync("p1", "d2", "同名", Content("二"));
        var parent = path + "-exports";
        await using var session = await WorkspaceExportSession.CreateAsync(repo, parent, new(["d1", "d2"], [], ["p2"]));
        Equal(2, session.Documents.Select(d => d.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var doc in session.Documents) await session.WriteDocumentAsync(doc.Id, Docx());
        var output = await session.CompleteAsync();
        Equal(2, Directory.GetFiles(output, "*.docx", SearchOption.AllDirectories).Length);
        Equal(2, Directory.GetDirectories(output).Length);
        await using var another = await WorkspaceExportSession.CreateAsync(repo, parent, new([], [], ["p2"]));
        var second = await another.CompleteAsync();
        if (second == output) throw new Exception("Export overwrote an existing folder");
        Equal(2, Directory.GetFiles(output, "*.docx", SearchOption.AllDirectories).Length);
    }),
    ("Incomplete and invalid exports never publish partial folders or change source documents", async path => {
        var repo = new WorkspaceRepository(path); await repo.SaveDocumentAsync("d", "CON", "template", Content("模板"));
        var parent = path + "-exports";
        await using (var session = await WorkspaceExportSession.CreateAsync(repo, parent, new(["d"], [], []))) {
            await Throws(async () => await session.CompleteAsync());
            await Throws(async () => await session.WriteDocumentAsync("../d", Docx()));
            await Throws(async () => await session.WriteDocumentAsync("d", [1,2,3]));
        }
        Equal(0, Directory.GetFileSystemEntries(parent).Length);
        await Throws(async () => await WorkspaceExportSession.CreateAsync(repo, parent, new(["unknown"], [], [])));
        await Throws(async () => await WorkspaceExportSession.CreateAsync(repo, Path.Combine(path, "exports"), new(["d"], [], [])));
        Equal("模板", (await repo.LoadOrCreateAsync()).Documents[0].Content.GetProperty("main")[0].GetProperty("value").GetString());
    }),
    ("Document history snapshots are immutable and restore preserves current draft and ownership", async path => {
        var repo = new WorkspaceRepository(path); await repo.CreateFolderAsync("person", "人员");
        var original = await repo.SaveFolderDocumentAsync("person", "doc", "原记录", Content("第一稿"));
        var version = await repo.CreateDocumentVersionAsync("doc", "初稿");
        await repo.SaveFolderDocumentAsync("person", "doc", "新记录", Content("第二稿"));
        var current = (await repo.LoadOrCreateAsync()).Folders[0].Documents[0];
        await Throws(async () => await repo.RestoreDocumentVersionAsync("doc", version.Id, original.UpdatedAt));
        var restored = await repo.RestoreDocumentVersionAsync("doc", version.Id, current.UpdatedAt);
        Equal("原记录", restored.Title); Equal(original.CreatedAt, restored.CreatedAt);
        Equal("第一稿", restored.Content.GetProperty("main")[0].GetProperty("value").GetString());
        var history = await new WorkspaceRepository(path).ListDocumentVersionsAsync("doc");
        if (history.Count < 2) throw new Exception("Missing restore backup");
        var backup = await repo.ReadDocumentVersionAsync("doc", history.First(v => v.Reason == "beforeRestore").Id);
        Equal("第二稿", backup.Document.Content.GetProperty("main")[0].GetProperty("value").GetString());
        Equal("初稿", (await repo.ReadDocumentVersionAsync("doc", version.Id)).Label);
        await Throws(async () => await repo.ReadDocumentVersionAsync("doc", "../workspace"));
        await Throws(async () => await repo.ReadDocumentVersionAsync("other", version.Id));
        Equal(0, (await repo.LoadOrCreateAsync()).Documents.Count);
    }),
    ("Automatic checkpoints keep previous content and template restore retains kind", async path => {
        var repo = new WorkspaceRepository(path);
        await repo.SaveDocumentAsync("template", "模板", "template", Content("旧模板"));
        await repo.SaveDocumentAsync("template", "新模板", "template", Content("新模板"));
        var versions = await repo.ListDocumentVersionsAsync("template"); Equal(1, versions.Count);
        var snapshot = await repo.ReadDocumentVersionAsync("template", versions[0].Id);
        Equal("旧模板", snapshot.Document.Content.GetProperty("main")[0].GetProperty("value").GetString());
        var current = (await repo.LoadOrCreateAsync()).Documents[0];
        var restored = await repo.RestoreDocumentVersionAsync("template", snapshot.Id, current.UpdatedAt);
        Equal("template", restored.Kind);
        await repo.SaveDocumentAsync("template", restored.Title, restored.Kind, restored.Content);
        Equal(2, (await repo.ListDocumentVersionsAsync("template")).Count);
    }),
    ("History rejects corrupt snapshots and concurrent stale restore without losing edits", async path => {
        var repo = new WorkspaceRepository(path);
        await repo.SaveDocumentAsync("doc", "记录", "document", Content("旧内容"));
        var version = await repo.CreateDocumentVersionAsync("doc", "初始");
        var updated = await repo.SaveDocumentAsync("doc", "当前记录", "document", Content("当前内容"));
        var attempts = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ => {
            try { await new WorkspaceRepository(path).RestoreDocumentVersionAsync("doc", version.Id, updated.UpdatedAt); return true; }
            catch (InvalidOperationException) { return false; }
        }));
        Equal(1, attempts.Count(ok => ok));
        var currentBytes = await File.ReadAllTextAsync(Path.Combine(path, "workspace.json"));
        await File.WriteAllTextAsync(Path.Combine(path, "history", "doc", version.Id + ".json"), "{}");
        var current = (await repo.LoadOrCreateAsync()).Documents[0];
        await Throws(async () => await repo.RestoreDocumentVersionAsync("doc", version.Id, current.UpdatedAt));
        Equal(currentBytes, await File.ReadAllTextAsync(Path.Combine(path, "workspace.json")));
    }),
    ("Automatic history throttles frequent saves and resumes after five minutes", async path => {
        var repo = new WorkspaceRepository(path);
        await repo.SaveDocumentAsync("doc", "记录", "document", Content("0"));
        await repo.SaveDocumentAsync("doc", "记录", "document", Content("1"));
        var first = (await repo.ListDocumentVersionsAsync("doc")).Single();
        await repo.SaveDocumentAsync("doc", "记录", "document", Content("2"));
        Equal(1, (await repo.ListDocumentVersionsAsync("doc")).Count);
        File.SetLastWriteTimeUtc(Path.Combine(path, "history", "doc", first.Id + ".json"), DateTime.UtcNow.AddMinutes(-6));
        await repo.SaveDocumentAsync("doc", "记录", "document", Content("3"));
        var latest = (await repo.ListDocumentVersionsAsync("doc")).First();
        Equal("2", (await repo.ReadDocumentVersionAsync("doc", latest.Id)).Document.Content.GetProperty("main")[0].GetProperty("value").GetString());
    }),
    ("Transcripts preserve machine text and compare revisions across repositories", async path => {
        var workspace = new WorkspaceRepository(path); await workspace.CreateFolderAsync("person", "人员");
        var audio = workspace.ResolveRecordingPath("recordings/person/audio.mp3"); Directory.CreateDirectory(Path.GetDirectoryName(audio)!); await File.WriteAllTextAsync(audio, "audio");
        await workspace.SaveRecordingAsync("person", new WorkspaceRecording("audio", "录音", "recordings/person/audio.mp3", 1000, DateTimeOffset.UtcNow));
        var repo = new TranscriptRepository(workspace);
        Equal<Transcript?>(null, await repo.ReadAsync("person", "audio"));
        using (var canceled = new CancellationTokenSource()) {
            canceled.Cancel();
            await Throws(async () => await repo.CreateAsync("person", "audio", "model", "取消后不写入", canceled.Token));
            Equal<Transcript?>(null, await repo.ReadAsync("person", "audio"));
        }
        var original = await repo.CreateAsync("person", "audio", "Qwen3-ASR-0.6B", "机器原文");
        Equal(1, original.Revision);
        var edits = await Task.WhenAll(Enumerable.Range(0, 2).Select(async i => {
            try { await new TranscriptRepository(new WorkspaceRepository(path)).SaveAsync("person", "audio", "人工" + i, 1); return true; } catch (InvalidOperationException) { return false; }
        }));
        Equal(1, edits.Count(x => x));
        var saved = (await repo.ReadAsync("person", "audio"))!;
        Equal("机器原文", saved.Text); Equal(2, saved.Revision); Equal(original.CreatedAt, saved.CreatedAt);
        await Throws(async () => await repo.CreateAsync("person", "audio", "model", "重试覆盖"));
        await Throws(async () => await repo.ReadAsync("other", "audio"));
        await Throws(async () => await repo.ReadAsync("person", "../audio"));
        Equal(saved.EditedText, (await repo.ReadAsync("person", "audio"))!.EditedText);
        await workspace.DeleteRecordingAsync("person", "audio");
        await Throws(async () => await repo.SaveAsync("person", "audio", "已删除", 2));
    }),
    ("Retranscription replaces machine text while preserving edits and rejects edits made during inference", async path => {
        var workspace = new WorkspaceRepository(path); await workspace.CreateFolderAsync("person", "人员");
        var audio = workspace.ResolveRecordingPath("recordings/person/audio.mp3"); Directory.CreateDirectory(Path.GetDirectoryName(audio)!); await File.WriteAllTextAsync(audio, "audio");
        await workspace.SaveRecordingAsync("person", new WorkspaceRecording("audio", "录音", "recordings/person/audio.mp3", 1000, DateTimeOffset.UtcNow));
        var repo = new TranscriptRepository(workspace);
        var original = await repo.CreateAsync("person", "audio", "model-v1", "旧机器文字");
        await repo.SaveAsync("person", "audio", "人工修订", 1);
        var rerun = await repo.ReplaceMachineTextAsync("person", "audio", "model-v2", "新机器文字", 2);
        Equal("新机器文字", rerun.Text); Equal("人工修订", rerun.EditedText); Equal("model-v2", rerun.Model);
        Equal(3, rerun.Revision); Equal(original.CreatedAt, rerun.CreatedAt);
        await repo.SaveAsync("person", "audio", "推理期间保存的修订", 3);
        await Throws(async () => await repo.ReplaceMachineTextAsync("person", "audio", "model-v3", "不应写入", 3));
        var preserved = (await repo.ReadAsync("person", "audio"))!;
        Equal("新机器文字", preserved.Text); Equal("推理期间保存的修订", preserved.EditedText); Equal(4, preserved.Revision);
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Throws(async () => await repo.ReplaceMachineTextAsync("person", "audio", "model-v3", "取消结果", 4, canceled.Token));
        Equal(preserved, await repo.ReadAsync("person", "audio"));
    }),
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
static byte[] Docx() { using var stream = new MemoryStream(); using (var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create, true)) { foreach (var name in new[] { "[Content_Types].xml", "word/document.xml" }) { using var writer = new StreamWriter(zip.CreateEntry(name).Open()); writer.Write("<xml/>"); } } return stream.ToArray(); }
static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}"); }
static async Task Throws(Func<Task> action) { try { await action(); } catch (Exception ex) when (ex is not NotImplementedException) { return; } throw new Exception("Expected a rejected operation"); }
