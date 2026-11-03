using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using RecDog.Core;

namespace RecDog.Desktop;

public sealed partial class WorkbenchWindow
{
    private WorkspaceExportSession? exportSession;

    private async Task<object?> BeginExportAsync(JsonElement payload)
    {
        RequireNoRecording();
        if (exportSession is not null) throw new InvalidOperationException("上一项导出尚未结束。");
        var selection = payload.Deserialize<ExportSelection>(WorkspaceRepository.JsonOptions) ?? throw new InvalidDataException("缺少导出范围。");
        string parent;
        if (smokeDirectory is not null) parent = Path.Combine(smokeDirectory, "selected-export");
        else
        {
            var dialog = new OpenFolderDialog { Title = "选择导出位置（将在其中创建项目文件夹）", Multiselect = false };
            if (dialog.ShowDialog(this) != true) return null;
            parent = dialog.FolderName;
        }
        exportSession = await WorkspaceExportSession.CreateAsync(repository, parent, selection);
        return new { sessionId = exportSession.Id, rootName = exportSession.RootName, documents = exportSession.Documents, recordingCount = exportSession.RecordingCount };
    }

    private WorkspaceExportSession ExportSession(JsonElement payload) => exportSession is { } session && session.Id == Text(payload, "sessionId")
        ? session : throw new InvalidOperationException("导出会话已结束，请重试。");

    private async Task<object?> WriteExportDocumentAsync(JsonElement payload)
    {
        var session = ExportSession(payload);
        var base64 = Text(payload, "base64");
        if (base64.Length > 180 * 1024 * 1024) throw new InvalidDataException("单份导出文档过大。");
        await session.WriteDocumentAsync(Text(payload, "id"), Convert.FromBase64String(base64));
        return null;
    }

    private async Task<object> FinishExportAsync(JsonElement payload)
    {
        var session = ExportSession(payload);
        var path = await session.CompleteAsync();
        exportSession = null;
        await session.DisposeAsync();
        return new { path, documentCount = session.Documents.Count, recordingCount = session.RecordingCount };
    }

    private async Task<object?> CancelExportAsync(JsonElement payload)
    {
        var session = ExportSession(payload);
        await session.DisposeAsync(); exportSession = null;
        return null;
    }
}
