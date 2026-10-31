using System.Text.Json;
using Blink.Models;

namespace DontBlink.Services;

public record ServiceProfile(string Id,string Name,bool Connected,string State,string[] Capabilities);
public record SystemOverview(string ServiceId,int Id,string Name,bool? Armed,string ModuleStatus,string Firmware,string TimeZone,DateTimeOffset? LastUpdatedUtc,int? ModuleId,string? ModuleName);
public record ManagementSnapshot(ServiceProfile[] Services,SystemOverview[] Systems,bool Cached,DateTimeOffset? ObservedUtc,string? Message=null);
public record DeviceSetting(string Group,string Id,string Label,string Value,bool Editable=false,string? Reason=null,string[]? Choices=null);
public record CameraSettings(string ServiceId,int NetworkId,int CameraId,string Name,DeviceSetting[] Settings);
public interface IDeviceManagement
{
    Task<ManagementSnapshot> ManagementAsync(bool refresh,CancellationToken ct);
    Task SetArmedAsync(int network,bool armed,CancellationToken ct);
    Task<CameraSettings> CameraSettingsAsync(int network,int camera,CancellationToken ct);
    Task SetNightVisionAsync(int network,int camera,string value,CancellationToken ct);
}

public static class DeviceFeatures
{
    public static ServiceProfile[] Services(bool blink)=>[
        new("blink","Blink",blink,blink?"Connected · cloud API":"Sign in required",["Cameras","System arm/disarm","Device information","Snapshots","Live view","USB archive"]),
        new("rtsp","RTSP",false,"Planned · adapter not implemented",["Future local video"]),
        new("onvif","ONVIF",false,"Planned · adapter not implemented",["Future local discovery and controls"]),
        new("archive","Local archive",true,"Available without Blink",["Playback","Import","Export"])];
    public static readonly (string Group,string Id,string Label,string[]? Choices)[] CameraFields=[
        ("General","name","Device name",null),("General","battery","Battery level",null),("General","fw_version","Firmware",null),("General","led_status","Status LED",["Always off","When recording"]),
        ("General","wifi_strength","Network strength",null),("General","lfr_strength","Connection to Sync Module",null),("General","temperature","Temperature (reported °F)",null),("General","temperature_alerts","Temperature alerts",null),
        ("Motion","enabled","Motion recording",["On","Off"]),("Motion","motion_sensitivity","Motion sensitivity",null),("Motion","activity_zones","Activity zones",null),("Motion","retrigger_time","Retrigger time",null),("Motion","early_notification","Early notification",["On","Off"]),
        ("Video and photo","clip_length","Motion clip length",null),("Video and photo","video_quality","Video quality",["Best","Standard","Saver"]),("Video and photo","motion_clip_stop","End clip early if motion stops",["On","Off"]),
        ("Video and photo","illuminator_enable","Night vision",["off","on","auto"]),("Video and photo","illuminator_intensity","IR intensity",["Low","Medium","High"]),("Video and photo","photo_capture","Photo capture",["On","Off"]),("Video and photo","auto_update_thumbnail","Auto-update thumbnail",["On","Off"]),
        ("Audio","speaker_volume","Speaker volume",null),
        ("Privacy","privacy_zones","Privacy zones",null),("Privacy","video_recording","Video recording",["On","Off"]),("Privacy","audio_enabled","Audio streaming",["On","Off"]),
        ("Notifications","snooze","Notification snooze",null),
        ("Device management","add_camera","Add / pair camera",null),("Device management","remove_camera","Remove camera from system",null)];
    public static string Value(JsonElement root,string key)
    {
        if(root.ValueKind!=JsonValueKind.Object)return "Unknown";
        if(root.TryGetProperty(key,out var value))return value.ValueKind switch{JsonValueKind.String=>value.GetString()??"Unknown",JsonValueKind.True=>"On",JsonValueKind.False=>"Off",JsonValueKind.Number=>value.ToString(),_=>"Not exposed"};
        foreach(var wrapper in new[]{"camera","config","signals","syncmodule","sync_module"})if(root.TryGetProperty(wrapper,out var child)){if(child.ValueKind==JsonValueKind.Array&&child.GetArrayLength()>0)child=child[0];var found=Value(child,key);if(found!="Unknown")return found;}
        return "Unknown";
    }
    public static DateTimeOffset? ThumbnailTime(string? value)
    {
        if(string.IsNullOrWhiteSpace(value))return null;
        if(long.TryParse(value,out var stamp)){try{var time=stamp>100_000_000_000?DateTimeOffset.FromUnixTimeMilliseconds(stamp):DateTimeOffset.FromUnixTimeSeconds(stamp);return time.Year>=2010&&time<=DateTimeOffset.UtcNow.AddMinutes(5)?time:null;}catch(ArgumentOutOfRangeException){return null;}}
        return DateTimeOffset.TryParse(value,System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.AssumeUniversal,out var parsed)&&parsed.Year>=2010?parsed.ToUniversalTime():null;
    }
    public static string Age(DateTimeOffset? captured,DateTimeOffset now)
    {
        if(captured==null)return "Age unknown";var elapsed=now-captured.Value;
        if(elapsed.TotalMinutes<1)return "Just now";if(elapsed.TotalHours<1)return $"{(int)elapsed.TotalMinutes}m ago";if(elapsed.TotalDays<1)return $"{(int)elapsed.TotalHours}h ago";if(elapsed.TotalDays<365)return $"{(int)elapsed.TotalDays}d ago";return $"{(int)(elapsed.TotalDays/365)}y ago";
    }
}

public sealed partial class BlinkClipSource : IDeviceManagement
{
    private ManagementSnapshot? _management;
    private void RememberDashboard(Dashboard dashboard)
    {
        var now=DateTimeOffset.UtcNow;
        var systems=dashboard.Networks.Select(n=>{
            int id=n.TryGetProperty("id",out var identifier)&&identifier.ValueKind==JsonValueKind.Number&&identifier.TryGetInt32(out var v)?v:0;
            var module=dashboard.SyncModules.FirstOrDefault(m=>m.NetworkId==id);
            bool? armed=n.TryGetProperty("armed",out var a)&&a.ValueKind is JsonValueKind.True or JsonValueKind.False?a.GetBoolean():null;
            return new SystemOverview("blink",id,DeviceFeatures.Value(n,"name"),armed,module?.Status??"Unknown",module?.Firmware??"Unknown",DeviceFeatures.Value(n,"time_zone"),DeviceFeatures.ThumbnailTime(module?.UpdatedAt),module?.Id,module?.Name);
        }).Where(n=>n.Id>0).ToList();
        foreach(var module in dashboard.SyncModules.Where(m=>systems.All(s=>s.Id!=m.NetworkId)))systems.Add(new("blink",module.NetworkId,"System "+module.NetworkId,null,module.Status??"Unknown",module.Firmware??"Unknown","Unknown",DeviceFeatures.ThumbnailTime(module.UpdatedAt),module.Id,module.Name));
        _management=new(DeviceFeatures.Services(IsAuthenticated),systems.ToArray(),false,now);
    }
    public async Task<ManagementSnapshot> ManagementAsync(bool refresh,CancellationToken ct){ct.ThrowIfCancellationRequested();if(refresh||_management==null)RememberDashboard(await Call(c=>c.GetDashboardAsync()));return _management! with{Services=DeviceFeatures.Services(IsAuthenticated)};}
    public async Task SetArmedAsync(int network,bool armed,CancellationToken ct){ct.ThrowIfCancellationRequested();if(!await Call(c=>armed?c.ArmSystemAsync(network):c.DisarmSystemAsync(network)))throw new IOException("Blink did not confirm the system command.");_management=null;}
    public async Task<CameraSettings> CameraSettingsAsync(int network,int camera,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();var dashboard=await Call(c=>c.GetDashboardAsync());var item=dashboard.Cameras.FirstOrDefault(c=>c.TryGetProperty("id",out var id)&&id.ToString()==camera.ToString()&&c.TryGetProperty("network_id",out var n)&&n.ToString()==network.ToString());if(item.ValueKind!=JsonValueKind.Object)throw new ArgumentException("Camera is not in this Blink system.");
        JsonElement config=default,signals=default;string? readError=null;
        try{config=JsonSerializer.SerializeToElement(await Call(c=>c.GetCameraInfoAsync(network,camera)));}catch(BlinkSessionUnavailableException){throw;}catch(Exception){readError="Configuration read unavailable; showing homescreen values.";}
        try{signals=JsonSerializer.SerializeToElement(await Call(c=>c.GetCameraSensorsAsync(network,camera)));}catch(BlinkSessionUnavailableException){throw;}catch(Exception){readError??="Signal details unavailable.";}
        var settings=DeviceFeatures.CameraFields.Select(field=>{
            string value=DeviceFeatures.Value(config,field.Id);if(value=="Unknown")value=DeviceFeatures.Value(signals,field.Id);if(value=="Unknown")value=DeviceFeatures.Value(item,field.Id);
            bool motion=field.Id=="enabled";string product=DeviceFeatures.Value(item,"product_type");if(field.Id=="illuminator_enable"&&product=="catalina"&&int.TryParse(value,out int mode)&&mode>=0&&mode<=2)value=new[]{"off","on","auto"}[mode];bool night=field.Id=="illuminator_enable"&&(product is "catalina" or "owl")&&new[]{"on","off","auto"}.Contains(value);
            return new DeviceSetting(field.Group,field.Id,field.Label,value,motion||night,motion?null:night?"Uses Blink cloud; read back after saving.":value=="Unknown"?readError??"Placeholder · control is not implemented or this device does not expose it.":"Reported value · editing is not implemented.",field.Choices);
        }).ToArray();return new("blink",network,camera,DeviceFeatures.Value(item,"name"),settings);
    }
    public async Task SetNightVisionAsync(int network,int camera,string value,CancellationToken ct){ct.ThrowIfCancellationRequested();var dashboard=await Call(c=>c.GetDashboardAsync());var device=dashboard.Cameras.FirstOrDefault(c=>c.TryGetProperty("id",out var id)&&id.ToString()==camera.ToString()&&c.TryGetProperty("network_id",out var n)&&n.ToString()==network.ToString());string product=DeviceFeatures.Value(device,"product_type");if(product is not("catalina" or "owl"))throw new NotSupportedException("Night vision editing is not verified for this model.");if(!await Call(c=>c.SetCameraNightVisionAsync(network,camera,value,product=="catalina")))throw new IOException("Blink did not confirm night vision.");}
}
