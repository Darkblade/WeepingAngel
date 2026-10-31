using System.Text.Json;
namespace DontBlink.Services;
public record CameraActivityLink(Guid DeviceId,string ServiceId,int NetworkId,int CameraId);
public sealed partial class DeviceRegistry
{
    private LiveActivityFollower? activity;
    private CameraActivityLink[]? links;
    public LiveActivityFollower ActivityMonitor{get{lock(_gate)return activity??=new(List);}}
    public CameraActivityLink[] ActivityLinks(){lock(_gate){if(links==null){try{links=JsonSerializer.Deserialize<CameraActivityLink[]>(File.ReadAllText(_path+".activity-links.json"))??[];}catch(Exception ex)when(ex is IOException or JsonException or UnauthorizedAccessException){links=[];}}return links.ToArray();}}
    public void LinkCamera(Guid device,string service,int? network,int? camera)
    {
        lock(_gate){if(!_devices.Any(d=>d.Id==device&&d.Type=="blink-outdoor-3"))throw new ArgumentException("Select a registered Blink camera first.");var current=ActivityLinks().Where(l=>l.DeviceId!=device).ToList();if(network!=null||camera!=null){if(string.IsNullOrWhiteSpace(service)||network is null or <=0||camera is null or <=0)throw new ArgumentException("Choose a camera and system.");if(current.Any(l=>l.ServiceId==service&&l.NetworkId==network&&l.CameraId==camera))throw new ArgumentException("That camera is already linked to another LAN device.");current.Add(new(device,service,network.Value,camera.Value));}Persist(_devices,current.ToArray());}
    }
    public object LiveOverview()
    {
        var saved=ActivityLinks();var registered=List();var snapshot=ActivityMonitor.Snapshot();return new{snapshot.Running,snapshot.FileName,snapshot.State,snapshot.LastPacketUtc,thresholdBytes=LiveActivityFollower.Threshold,activities=snapshot.Activities.Where(a=>registered.Any(d=>d.Id==a.DeviceId)).Select(a=>{var link=saved.FirstOrDefault(l=>l.DeviceId==a.DeviceId);return new{a.DeviceId,a.Name,a.StartedUtc,a.LastTrafficUtc,a.ObservedBytes,a.State,a.Source,pulsing=a.Pulsing&&link!=null,serviceId=link?.ServiceId,networkId=link?.NetworkId,cameraId=link?.CameraId};}).ToArray()};
    }
}