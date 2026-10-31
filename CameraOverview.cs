using System.Text.Json;

namespace DontBlink.Services;

public record CameraOverview(int Id, int NetworkId, string Name, string Model, string Status, string Battery, string Motion, DateTimeOffset? ThumbnailCapturedUtc = null, string ServiceId = "blink", string? Firmware = null, bool ThumbnailTimeEstimated = false)
{
    public bool CanRequestVideo => CameraVideoCommands.IsSupported(this);
    public static CameraOverview? Parse(JsonElement camera)
    {
        string Value(string key) => camera.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : "Unknown";
        if (!int.TryParse(Value("id"), out int id) || !int.TryParse(Value("network_id"), out int network)) return null;
        string name = Value("name");
        string motion = Value("enabled");
        string model=Value("product_type");
        if(string.IsNullOrWhiteSpace(model)||model=="Unknown")model=Value("type");
        model=string.IsNullOrWhiteSpace(model)?"Unknown":model.Trim().ToLowerInvariant();
        return new(id, network, name == "Unknown" ? $"Camera {id}" : name, model, Value("status"), Value("battery"),
            motion.Equals("true", StringComparison.OrdinalIgnoreCase) ? "Enabled" : motion.Equals("false", StringComparison.OrdinalIgnoreCase) ? "Disabled" : "Unknown", DeviceFeatures.ThumbnailTime(Value("thumbnail")), "blink", Value("fw_version"));
    }
}

