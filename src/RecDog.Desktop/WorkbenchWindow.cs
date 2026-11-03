using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;
using RecDog.Core;

namespace RecDog.Desktop;

public sealed partial class WorkbenchWindow : Window
{
    private readonly WebView2 browser = new();
    private readonly SemaphoreSlim bridgeGate = new(1, 1);
    private readonly string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RecDog");
    private readonly string? requestedWorkspace;
    private readonly string? smokeDirectory;
    private readonly List<TaskCompletionSource<bool>> downloads = [];
    private readonly TaskCompletionSource<bool> downloadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private WorkspaceRepository repository = null!;
    private bool initialized;
    private bool closeApproved;
    private bool closingRequested;
    private bool smokeFinished;

    public WorkbenchWindow(string[] args)
    {
        requestedWorkspace = Argument(args, "--workspace");
        smokeDirectory = Argument(args, "--smoke-test");
        Title = "听录 · 文档工作台";
        Width = 1500; Height = 960; MinWidth = 1000; MinHeight = 680;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = System.Windows.Media.Brushes.White;
        Content = new TextBlock { Text = "正在打开文档工作台…", Margin = new Thickness(32), FontSize = 16 };
        Loaded += async (_, _) => await InitializeAsync();
        Closing += OnClosing;
    }

    private static string? Argument(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? Path.GetFullPath(args[index + 1]) : null;
    }

    private async Task InitializeAsync()
    {
        try
        {
            Directory.CreateDirectory(appData);
            repository = await OpenInitialWorkspaceAsync();
            if (workspaceOpen) await Catalog.RememberAsync(repository.DirectoryPath);
            var webRoot = Path.Combine(AppContext.BaseDirectory, "web");
            if (!File.Exists(Path.Combine(webRoot, "index.html"))) throw new FileNotFoundException("未找到本地编辑器资源。请先构建 frontend 并重新构建桌面应用。");
            Content = browser;
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: Path.Combine(smokeDirectory ?? appData, "WebView2"));
            await browser.EnsureCoreWebView2Async(environment);
            var web = browser.CoreWebView2;
            web.SetVirtualHostNameToFolderMapping("recdog.local", webRoot, CoreWebView2HostResourceAccessKind.DenyCors);
            MapRecordingMedia();
            web.Settings.AreDefaultContextMenusEnabled = false;
            web.Settings.AreDevToolsEnabled = false;
            web.Settings.IsStatusBarEnabled = false;
            web.Settings.IsZoomControlEnabled = false;
            web.Settings.AreBrowserAcceleratorKeysEnabled = false;
            web.Settings.IsGeneralAutofillEnabled = false;
            web.Settings.IsPasswordAutosaveEnabled = false;
            web.NavigationStarting += (_, e) => { if (!IsLocal(e.Uri)) e.Cancel = true; };
            web.NewWindowRequested += (_, e) => e.Handled = true;
            web.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            web.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            web.WebResourceRequested += (_, e) =>
            {
                if (!IsLocal(e.Request.Uri) && !IsRecordingMedia(e.Request.Uri) && !e.Request.Uri.StartsWith("blob:https://recdog.local/", StringComparison.OrdinalIgnoreCase) && !e.Request.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    e.Response = environment.CreateWebResourceResponse(Stream.Null, 403, "External requests disabled", "Content-Type: text/plain");
            };
            web.WebMessageReceived += OnWebMessage;
            web.DownloadStarting += OnDownloadStarting;
            web.ProcessFailed += (_, e) => MessageBox.Show(this, "编辑器进程意外停止，请重启应用。已保存文档仍在项目目录中。\n" + e.ProcessFailedKind, "编辑器错误", MessageBoxButton.OK, MessageBoxImage.Error);
            web.NavigationCompleted += async (_, e) =>
            {
                if (!e.IsSuccess) { await FailInitializationAsync(new IOException("本地编辑器加载失败：" + e.WebErrorStatus)); return; }
                initialized = true;
                if (smokeDirectory is not null) await StartSmokeAsync();
            };
            web.Navigate("https://recdog.local/index.html");
            recorder.Failed += ex => Dispatcher.InvokeAsync(() => Post(new { @event = "recordingError", error = ex.Message }));
            Closed += (_, _) => { recorder.Dispose(); browser.Dispose(); };
        }
        catch (Exception ex) { await FailInitializationAsync(ex); }
    }

    private async Task<WorkspaceRepository> OpenInitialWorkspaceAsync()
    {
        var selectedPathFile = Path.Combine(smokeDirectory ?? appData, "workspace-path.txt");
        var selected = requestedWorkspace ?? (File.Exists(selectedPathFile) ? (await File.ReadAllTextAsync(selectedPathFile)).Trim() : null);
        WorkspaceRepository Empty() { workspaceOpen = false; return new WorkspaceRepository(Path.Combine(smokeDirectory ?? appData, "empty-workspace")); }
        if (selected == "!closed") return Empty();
        if (!string.IsNullOrWhiteSpace(selected))
        {
            if (await ProjectCatalog.OpenExistingAsync(selected) is not null) return new WorkspaceRepository(selected);
            return Empty();
        }
        var candidates = new[] {
            Path.Combine(AppContext.BaseDirectory, "data", "workspace"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "app", "data", "workspace")),
            Path.Combine(appData, "data", "workspace")
        };
        foreach (var candidate in candidates)
            if (await ProjectCatalog.OpenExistingAsync(candidate) is not null) return new WorkspaceRepository(candidate);
        return Empty();
    }

    private static bool IsLocal(string uri) => Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.Scheme == "https" && parsed.Host.Equals("recdog.local", StringComparison.OrdinalIgnoreCase) && parsed.IsDefaultPort;

    private async void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!IsLocal(e.Source)) return;
        string? id = null;
        await bridgeGate.WaitAsync();
        try
        {
            using var message = JsonDocument.Parse(e.WebMessageAsJson);
            var root = message.RootElement;
            id = root.GetProperty("id").GetString();
            if (string.IsNullOrWhiteSpace(id)) throw new InvalidDataException("请求缺少标识。");
            var method = root.GetProperty("method").GetString();
            if (!workspaceOpen && method is not ("workspaceStatus" or "selectWorkspace" or "settings" or "closeReady" or "closeAbort" or "recordingStatus" or "bootstrap" or "recordingDevices" or "recordingLevel" or "projectList" or "projectDefaults" or "createProject" or "openProject" or "chooseProjectParent")) throw new InvalidOperationException("请先打开项目。");
            var payload = root.TryGetProperty("payload", out var value) ? value : JsonSerializer.SerializeToElement(new { });
            object? result = method switch
            {
                "projectList" => await Catalog.ListAsync(),
                "projectDefaults" => new { name = await Catalog.NextNameAsync(), parent = Catalog.DefaultRoot },
                "createProject" => await CreateProjectAsync(Text(payload, "name"), payload.TryGetProperty("parent", out var parent) ? parent.GetString() : null),
                "openProject" => await OpenCatalogProjectAsync(Text(payload, "path")),
                "chooseProjectParent" => ChooseProjectParent(),
                "bootstrap" => workspaceOpen ? await repository.LoadOrCreateAsync() : new Workspace(2, "empty", "未打开项目", repository.DirectoryPath, [], []),
                "saveDocument" => await repository.SaveDocumentAsync(Text(payload, "id"), Text(payload, "title"), Text(payload, "kind"), payload.GetProperty("content")),
                "saveFolderDocument" => await repository.SaveFolderDocumentAsync(Text(payload, "folderId"), Text(payload, "id"), Text(payload, "title"), payload.GetProperty("content")),
                "workspaceStatus" => new { open = workspaceOpen },
                "createFolder" => await repository.CreateFolderAsync(Text(payload, "id"), Text(payload, "name")),
                "renameFolder" => await repository.RenameFolderAsync(Text(payload, "id"), Text(payload, "name")),
                "deleteFolder" => await DeleteFolderAsync(Text(payload, "id")),
                "deleteDocument" => await repository.DeleteDocumentAsync(Text(payload, "id")),
                "renameWorkspace" => await repository.RenameAsync(Text(payload, "name")),
                "closeWorkspace" => await CloseWorkspaceAsync(),
                "deleteWorkspace" => await DeleteWorkspaceAsync(),
                "startRecording" => await StartRecordingAsync(Text(payload, "folderId"), Text(payload, "documentName"), payload.TryGetProperty("deviceId", out var device) ? device.GetInt32() : 0, payload.TryGetProperty("deviceName", out var deviceName) ? deviceName.GetString() : null),
                "pauseRecording" => PauseRecording(),
                "resumeRecording" => ResumeRecording(),
                "stopRecording" => await StopRecordingAsync(),
                "recordingDevices" => AudioRecorder.Devices(),
                "recordingLevel" => new { peak = recorder.Peak },
                "recordingStatus" => new { active = recorder.IsActive || uncommittedRecording is not null, paused = recorder.IsPaused, pending = uncommittedRecording is not null },
                "memorySnapshot" when smokeDirectory is not null => MemorySnapshot(),
                "readRecording" => await ReadRecordingAsync(Text(payload, "folderId"), Text(payload, "id")),
                "exportRecording" => await ExportRecordingAsync(Text(payload, "folderId"), Text(payload, "id")),
                "revealRecording" => await RecordingPathActionAsync(Text(payload, "folderId"), Text(payload, "id"), false),
                "copyRecordingPath" => await RecordingPathActionAsync(Text(payload, "folderId"), Text(payload, "id"), true),
                "deleteRecording" => await repository.DeleteRecordingAsync(Text(payload, "folderId"), Text(payload, "id")),
                "selectWorkspace" => await SelectWorkspaceAsync(payload.TryGetProperty("create", out var create) && create.GetBoolean()),
                "importDocx" => await ImportDocxAsync(),
                "settings" => new { runtimeVersion = browser.CoreWebView2.Environment.BrowserVersionString, appVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.1.0", workspacePath = repository.DirectoryPath },
                "revealWorkspace" => RevealWorkspace(),
                "closeReady" => ApproveClose(),
                "closeAbort" => AbortClose(),
                "smokeComplete" when smokeDirectory is not null => await CompleteSmokeAsync(payload),
                "smokeFailed" when smokeDirectory is not null => await SmokeFailedAsync(Text(payload, "message")),
                _ => throw new InvalidOperationException("不支持的宿主操作：" + method)
            };
            Respond(id, true, result, null);
        }
        catch (Exception ex) { if (id is not null) Respond(id, false, null, ex.Message); }
        finally { bridgeGate.Release(); }
    }

    private static string Text(JsonElement payload, string key) => payload.GetProperty(key).GetString() ?? throw new InvalidDataException("字段为空：" + key);
    private void Respond(string id, bool ok, object? result, string? error) => Post(new { id, ok, result, error });
    private void Post(object value) => browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(value, WorkspaceRepository.JsonOptions));

    private async Task<Workspace?> SelectWorkspaceAsync(bool create)
    {
        RequireNoRecording();
        var dialog = new OpenFolderDialog { Title = create ? "选择新项目的空文件夹" : "打开项目文件夹", InitialDirectory = Directory.Exists(repository.DirectoryPath) ? repository.DirectoryPath : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Multiselect = false };
        if (dialog.ShowDialog(this) != true) return null;
        var manifest = Path.Combine(dialog.FolderName, "workspace.json");
        if (!create && !File.Exists(manifest)) throw new InvalidDataException("所选文件夹中没有 workspace.json 项目文件。请使用新建项目。");
        if (create && Directory.EnumerateFileSystemEntries(dialog.FolderName).Any()) throw new InvalidDataException("新建项目请选择空文件夹，避免覆盖已有资料。");
        return await ActivateProjectAsync(dialog.FolderName);
    }

    private ProjectCatalog Catalog => new(Path.Combine(smokeDirectory ?? appData, "projects.json"), smokeDirectory is null ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "RecDog", "Projects") : Path.Combine(smokeDirectory, "Projects"));
    private object? ChooseProjectParent()
    {
        RequireNoRecording();
        var dialog = new OpenFolderDialog { Title = "选择项目保存的父文件夹", Multiselect = false };
        return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
    }
    private async Task<Workspace> CreateProjectAsync(string name, string? parent)
    {
        RequireNoRecording();
        var created = await Catalog.CreateAsync(name, parent);
        return await ActivateProjectAsync(created.Path);
    }
    private async Task<Workspace> OpenCatalogProjectAsync(string path)
    {
        RequireNoRecording();
        if (!(await Catalog.ListAsync()).Any(item => item.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("项目已不存在，请重新选择项目文件夹。");
        return await ActivateProjectAsync(path);
    }
    private async Task<Workspace> ActivateProjectAsync(string path)
    {
        var next = new WorkspaceRepository(path);
        var workspace = await next.LoadOrCreateAsync();
        await Catalog.RememberAsync(path);
        var selectedPathFile = Path.Combine(smokeDirectory ?? appData, "workspace-path.txt");
        var temporary = selectedPathFile + ".tmp";
        await File.WriteAllTextAsync(temporary, next.DirectoryPath);
        File.Move(temporary, selectedPathFile, overwrite: true);
        repository = next;
        workspaceOpen = true;
        MapRecordingMedia();
        return workspace;
    }

    private async Task<object?> ImportDocxAsync()
    {
        var dialog = new OpenFileDialog { Title = "导入 DOCX 文档", Filter = "Word 文档 (*.docx)|*.docx", CheckFileExists = true, Multiselect = false };
        if (dialog.ShowDialog(this) != true) return null;
        if (!Path.GetExtension(dialog.FileName).Equals(".docx", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("请选择 .docx 文件；旧版 .doc 请先转换。");
        var bytes = await File.ReadAllBytesAsync(dialog.FileName);
        if (bytes.Length < 4 || bytes[0] != 0x50 || bytes[1] != 0x4b) throw new InvalidDataException("所选文件不是有效的 DOCX 压缩容器。");
        var sources = Path.Combine(repository.DirectoryPath, "sources");
        Directory.CreateDirectory(sources);
        var name = Path.GetFileName(dialog.FileName);
        var preserved = Path.Combine(sources, DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8] + "-" + name);
        await using (var file = new FileStream(preserved, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await file.WriteAsync(bytes);
        return new { name, base64 = Convert.ToBase64String(bytes) };
    }

    private object? RevealWorkspace()
    {
        Process.Start(new ProcessStartInfo("explorer.exe", repository.DirectoryPath) { UseShellExecute = true });
        return null;
    }

    private void OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        e.Handled = true;
        var suggested = Path.GetFileName(e.ResultFilePath);
        var extension = Path.GetExtension(suggested).ToLowerInvariant();
        if (extension is not (".docx" or ".pdf")) { e.Cancel = true; Post(new { @event = "exportFailed", error = "仅支持导出 .docx 或 .pdf 文件。" }); return; }
        string destination;
        if (smokeDirectory is not null)
        {
            Directory.CreateDirectory(smokeDirectory);
            destination = Path.Combine(smokeDirectory, "export" + extension);
        }
        else
        {
            var dialog = new SaveFileDialog { Title = "导出文档", FileName = suggested, DefaultExt = extension, AddExtension = true, OverwritePrompt = true, Filter = extension == ".docx" ? "Word 文档 (*.docx)|*.docx" : "PDF 文档 (*.pdf)|*.pdf" };
            if (dialog.ShowDialog(this) != true) { e.Cancel = true; Post(new { @event = "exportCanceled" }); return; }
            destination = dialog.FileName;
        }
        if (!Path.GetExtension(destination).Equals(extension, StringComparison.OrdinalIgnoreCase)) { e.Cancel = true; Post(new { @event = "exportFailed", error = "导出扩展名与文档类型不一致。" }); return; }
        e.ResultFilePath = destination;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        downloads.Add(completion);
        downloadStarted.TrySetResult(true);
        var operation = e.DownloadOperation;
        operation.StateChanged += (_, _) =>
        {
            if (operation.State == CoreWebView2DownloadState.Completed)
            {
                completion.TrySetResult(true);
                Post(new { @event = "exported", path = destination });
            }
            else if (operation.State == CoreWebView2DownloadState.Interrupted)
            {
                completion.TrySetResult(false);
                Post(new { @event = "exportFailed", error = operation.InterruptReason.ToString() });
            }
        };
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (closeApproved || !initialized) return;
        e.Cancel = true;
        if (closingRequested) return;
        closingRequested = true;
        Post(new { @event = "closing" });
    }

    private object? ApproveClose()
    {
        if (!closingRequested) throw new InvalidOperationException("没有待处理的关闭请求。");
        RequireNoRecording();
        closeApproved = true;
        _ = Dispatcher.BeginInvoke(Close);
        return null;
    }
    private object? AbortClose() { closingRequested = false; return null; }

    private async Task StartSmokeAsync()
    {
        await browser.CoreWebView2.ExecuteScriptAsync("""
            (async () => {
              try {
                for (let i=0; i<300 && (!window.recDogReady || !window.recDogSmokeTest); i++) await new Promise(r=>setTimeout(r,100));
                if (!window.recDogSmokeTest) throw new Error('Smoke test globals were not exposed');
                await window.recDogReady;
                const result = await window.recDogSmokeTest();
                window.chrome.webview.postMessage({id:'native-smoke',method:'smokeComplete',payload:result});
              } catch (error) {
                window.chrome.webview.postMessage({id:'native-smoke',method:'smokeFailed',payload:{message:String(error?.stack || error)}});
              }
            })();
            """);
        _ = SmokeTimeoutAsync();
    }

    private async Task SmokeTimeoutAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(90));
        if (!smokeFinished) await SmokeFailedAsync("Smoke test timed out after 90 seconds.");
    }

    private async Task<object?> CompleteSmokeAsync(JsonElement result)
    {
        if (smokeFinished) return null;
        Directory.CreateDirectory(smokeDirectory!);
        if (result.TryGetProperty("docxBytes", out var docxBytes) && docxBytes.TryGetInt64(out var byteCount) && byteCount > 0)
            await downloadStarted.Task.WaitAsync(TimeSpan.FromSeconds(20));
        if (downloads.Count > 0)
        {
            var completed = await Task.WhenAll(downloads.Select(d => d.Task)).WaitAsync(TimeSpan.FromSeconds(20));
            if (completed.Any(done => !done)) throw new IOException("Smoke export download failed.");
        }
        await File.WriteAllTextAsync(Path.Combine(smokeDirectory!, "smoke-result.json"), JsonSerializer.Serialize(result, WorkspaceRepository.JsonOptions));
        var reopened = await new WorkspaceRepository(repository.DirectoryPath).LoadOrCreateAsync();
        await File.WriteAllTextAsync(Path.Combine(smokeDirectory!, "native-workspace.json"), JsonSerializer.Serialize(reopened, WorkspaceRepository.JsonOptions));
        if (result.TryGetProperty("docxBase64", out var base64) && base64.ValueKind == JsonValueKind.String)
            await File.WriteAllBytesAsync(Path.Combine(smokeDirectory!, "smoke-export.docx"), Convert.FromBase64String(base64.GetString()!));
        await using (var screenshot = File.Create(Path.Combine(smokeDirectory!, "screenshot.png")))
            await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, screenshot);
        smokeFinished = true;
        closeApproved = true;
        _ = Dispatcher.BeginInvoke(Close);
        return null;
    }

    private async Task<object?> SmokeFailedAsync(string message)
    {
        if (smokeFinished) return null;
        smokeFinished = true;
        Directory.CreateDirectory(smokeDirectory!);
        await File.WriteAllTextAsync(Path.Combine(smokeDirectory!, "smoke-error.txt"), message);
        closeApproved = true;
        _ = Dispatcher.BeginInvoke(() => Application.Current.Shutdown(1));
        return null;
    }

    private async Task FailInitializationAsync(Exception ex)
    {
        if (smokeDirectory is not null) { await SmokeFailedAsync(ex.ToString()); return; }
        MessageBox.Show(this, "无法打开文档工作台。项目文件不会被覆盖。\n\n" + ex.Message, "听录 · 打开失败", MessageBoxButton.OK, MessageBoxImage.Error);
        closeApproved = true;
        Close();
    }
}
