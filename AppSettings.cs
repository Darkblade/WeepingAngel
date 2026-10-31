using System.Text.Json;

namespace DontBlink.Services;

public sealed record AppSettings(string? ArchiveFolder = null, string Theme = "Dark", bool AutoArchive = false,
    bool CameraGrid = true, bool RefreshCameraImages = false, int CameraRefreshValue = 5, string CameraRefreshUnit = "Minutes", bool RecordLive = false,
    RecordingOverlay? Overlay = null, bool SecurePhone = false, double TextScale = 1, bool TextSpacing = false,
    bool ComfortableControls = false, bool ReduceVisualClutter = false, string MotionPreference = "System", bool DebugLogCollapsed = true, int DebugLogHeight = 240, string[]? CameraOrder = null, string DiagnosticVerbosity = "Normal")
{
    private static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DontBlink", "settings.json");
    public static AppSettings Load(string? path = null)
    {
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path ?? DefaultPath)) ?? new(); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path + ".partial", JsonSerializer.Serialize(this));
        File.Move(path + ".partial", path, true);
    }
}
