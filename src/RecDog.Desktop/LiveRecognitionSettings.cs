using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace RecDog.Desktop;

public sealed partial class WorkbenchWindow
{
    private string LiveSettingsPath => Path.Combine(smokeDirectory ?? appData, "live-recognition.json");
    private sealed record LiveSettings(string Provider = "off", string AppKey = "", string ProtectedToken = "");
    private async Task<LiveSettings> LoadLiveSettingsAsync() => File.Exists(LiveSettingsPath)
        ? JsonSerializer.Deserialize<LiveSettings>(await File.ReadAllTextAsync(LiveSettingsPath)) ?? new() : new();
    private async Task<object> LiveSettingsViewAsync()
    {
        var s = await LoadLiveSettingsAsync();
        return new { provider = s.Provider, appKey = s.AppKey, hasToken = s.ProtectedToken.Length > 0 };
    }
    private async Task<object> SaveLiveSettingsAsync(string provider, string? appKey, string token, bool clearToken)
    {
        RequireNoRecording();
        if (provider is not ("off" or "local" or "aliyun")) throw new InvalidOperationException("未知识别方式。");
        if (appKey?.Length > 256 || token.Length > 8192) throw new InvalidOperationException("配置内容过长。");
        var previous = await LoadLiveSettingsAsync();
        var encrypted = clearToken ? "" : string.IsNullOrWhiteSpace(token) ? previous.ProtectedToken : ProtectToken(token.Trim(), false);
        appKey = appKey?.Trim() ?? previous.AppKey;
        if (provider == "aliyun" && (appKey.Length == 0 || encrypted.Length == 0)) throw new InvalidOperationException("请填写 AppKey 和临时 Token。");
        Directory.CreateDirectory(Path.GetDirectoryName(LiveSettingsPath)!);
        var temporary = LiveSettingsPath + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new LiveSettings(provider, appKey, encrypted)));
            File.Move(temporary, LiveSettingsPath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return await LiveSettingsViewAsync();
    }
    [StructLayout(LayoutKind.Sequential)] private struct DataBlob { public int Size; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true)] private static extern bool CryptProtectData(ref DataBlob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);
    [DllImport("crypt32.dll", SetLastError = true)] private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr pointer);
    private static string ProtectToken(string value, bool decrypt)
    {
        var bytes = decrypt ? Convert.FromBase64String(value) : Encoding.UTF8.GetBytes(value);
        var input = new DataBlob { Size = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        var output = new DataBlob();
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            var ok = decrypt ? CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new InvalidOperationException("无法读取或保存当前 Windows 用户的 Token，请重新填写。");
            var result = new byte[output.Size]; Marshal.Copy(output.Data, result, 0, result.Length);
            return decrypt ? Encoding.UTF8.GetString(result) : Convert.ToBase64String(result);
        }
        finally { Marshal.FreeHGlobal(input.Data); if (output.Data != IntPtr.Zero) LocalFree(output.Data); }
    }
}
