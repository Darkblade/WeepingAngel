using System.Text.Json;
namespace DontBlink.Services;

public sealed record LanDeviceConfiguration(int SchemaVersion,LanDevice[] Devices,CameraActivityLink[] Links);
public sealed partial class DeviceRegistry
{
    private static readonly JsonSerializerOptions ConfigurationJson=new(JsonSerializerDefaults.Web){WriteIndented=true};
    public const int MaximumConfigurationBytes=2*1024*1024;
    public string ExportConfiguration(){lock(_gate)return JsonSerializer.Serialize(new LanDeviceConfiguration(1,_devices,ActivityLinks()),ConfigurationJson);}
    public LanDeviceConfiguration PreviewConfiguration(string json,bool replace=false){lock(_gate)return PrepareConfiguration(json,replace);}
    public LanDeviceConfiguration ImportConfiguration(string json,bool replace=false)
    {
        lock(_gate){var next=PrepareConfiguration(json,replace);Persist(next.Devices,next.Links);_devices=next.Devices;_checks.Clear();_replay=null;return next;}
    }
    private LanDeviceConfiguration PrepareConfiguration(string json,bool replace)
    {
        if(System.Text.Encoding.UTF8.GetByteCount(json)>MaximumConfigurationBytes)throw new ArgumentException("Configuration file exceeds 2 MiB.");
        LanDeviceConfiguration incoming;
        try{incoming=JsonSerializer.Deserialize<LanDeviceConfiguration>(json,ConfigurationJson)??throw new ArgumentException("Configuration is empty.");}
        catch(JsonException){throw new ArgumentException("Choose a valid Weeping Angel device configuration JSON file.");}
        incoming=ValidateConfiguration(incoming);
        if(replace)return incoming;
        var ids=incoming.Devices.Select(d=>d.Id).ToHashSet();
        var linkIds=incoming.Links.Select(l=>l.DeviceId).ToHashSet();
        // Matching IDs are explicit edits; unrelated registered devices and links survive a merge.
        var devices=_devices.Where(d=>!ids.Contains(d.Id)).Concat(incoming.Devices).ToArray();
        var mergedLinks=ActivityLinks().Where(l=>!linkIds.Contains(l.DeviceId)).Concat(incoming.Links).ToArray();
        return ValidateConfiguration(new(1,devices,mergedLinks));
    }
    private static LanDeviceConfiguration ValidateConfiguration(LanDeviceConfiguration input)
    {
        if(input.SchemaVersion!=1)throw new ArgumentException("Unsupported device configuration version.");
        if(input.Devices==null||input.Links==null||input.Devices.Length>2048||input.Links.Length>2048)throw new ArgumentException("Configuration must contain devices and links (maximum 2048 each).");
        if(input.Devices.Any(d=>d==null||d.Id==Guid.Empty||!Types.Any(t=>t.Id==d.Type)))throw new ArgumentException("Every imported device needs a valid ID and supported type.");
        if(input.Devices.Select(d=>d.Id).Distinct().Count()!=input.Devices.Length)throw new ArgumentException("Configuration contains duplicate device IDs.");
        var normalized=input.Devices.Select(d=>NormalizeDevice(d,input.Devices)).ToArray();
        // Normalize first so alternate MAC spellings cannot conceal an address collision.
        foreach(var device in normalized)NormalizeDevice(device,normalized);
        if(input.Links.Any(l=>l==null||!normalized.Any(d=>d.Id==l.DeviceId&&d.Type=="blink-outdoor-3")||string.IsNullOrWhiteSpace(l.ServiceId)||l.ServiceId.Length>80||l.ServiceId.Any(char.IsControl)||l.NetworkId<=0||l.CameraId<=0))throw new ArgumentException("Every link must reference a registered Blink camera and valid provider/system/camera IDs.");
        if(input.Links.Select(l=>l.DeviceId).Distinct().Count()!=input.Links.Length||input.Links.Select(l=>(l.ServiceId,l.NetworkId,l.CameraId)).Distinct().Count()!=input.Links.Length)throw new ArgumentException("Configuration contains duplicate camera links.");
        return new(1,normalized,input.Links);
    }
}
