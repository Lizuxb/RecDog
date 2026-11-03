using System.Text.Json;
using RecDog.Core;

namespace RecDog.Desktop;

public sealed partial class WorkbenchWindow
{
    private Task<WorkspaceDocument> RestoreDocumentVersionAsync(JsonElement payload)
    {
        RequireNoRecording();
        return repository.RestoreDocumentVersionAsync(Text(payload, "id"), Text(payload, "versionId"), payload.GetProperty("expectedUpdatedAt").GetDateTimeOffset());
    }
}
