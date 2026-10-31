using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DontBlink.Services;

public sealed record LanDevice(Guid Id,string Name,string Type,string IpAddress,string MacAddress,Guid? ParentId=null);
public sealed record LanDeviceType(string Id,string Label,bool Hub,bool Battery,string Capabilities);
public sealed record LanReachability(Guid DeviceId,DateTimeOffset CheckedUtc,bool Reachable,string Status);
public sealed record LocalActivity(Guid DeviceId,string Name,DateTimeOffset StartedUtc,DateTimeOffset LastTrafficUtc,long ObservedBytes,bool ConnectionClosed,string Source="Capture replay");
public sealed record CaptureReplayResult(string FileName,DateTimeOffset? StartedUtc,DateTimeOffset? EndedUtc,int Packets,LocalActivity[] Activities,string Note);

public sealed partial class DeviceRegistry
{
    public static readonly LanDeviceType[] Types=[
        new("blink-sync-2","Blink Sync Module — Version 2",true,false,"Local reachability; Blink control requires a signed-in session"),
        new("blink-outdoor-3","Blink Outdoor — Generation 3",false,true,"Passive local activity; video and control currently use Blink"),
        new("rtsp-camera","IP camera — RTSP",false,false,"Registered for future RTSP integration; playback is not configured yet"),
        new("onvif-camera","IP camera — ONVIF",false,false,"Registered for future ONVIF integration; discovery and control are not configured yet"),
        new("generic-camera","Other security camera",false,false,"Registration and reachability only")];
    private readonly string _path;
    private readonly object _gate=new();
    private LanDevice[] _devices;
    private readonly Dictionary<Guid,LanReachability> _checks=[];
    private CaptureReplayResult? _replay;
    public DeviceRegistry(string? path=null)
    {
        _path=path??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DontBlink","lan-devices.json");
        if(File.Exists(_path)){
            using var document=JsonDocument.Parse(File.ReadAllText(_path));
            if(document.RootElement.ValueKind==JsonValueKind.Array)_devices=document.RootElement.Deserialize<LanDevice[]>()??throw new InvalidDataException("The device registry is invalid.");
            else{var configuration=document.RootElement.Deserialize<LanDeviceConfiguration>(ConfigurationJson)??throw new InvalidDataException("The device registry is invalid.");var validated=ValidateConfiguration(configuration);_devices=validated.Devices;links=validated.Links;}
        }
        else {Guid hub=Guid.NewGuid();_devices=[new(hub,"Blink Sync Module","blink-sync-2","192.168.50.100","02:00:00:00:10:01"),new(Guid.NewGuid(),"Garden-camera","blink-outdoor-3","192.168.50.112","02:00:00:00:10:02",hub)];Persist(_devices);}
    }
    public LanDevice[] List(){lock(_gate)return _devices.ToArray();}
    public object Overview(){var live=ActivityMonitor.Snapshot();lock(_gate)return new{devices=_devices.ToArray(),types=Types,checks=_checks.Values.ToArray(),replay=_replay,monitoring=live.Running?"Live capture: "+live.State:"Live capture not connected; historical replay is separate"};}
    public CaptureReplayResult? Replay {get{lock(_gate)return _replay;}}
    public LanDevice Save(LanDevice input)
    {
        lock(_gate){
            var saved=NormalizeDevice(input,_devices);var next=_devices.Where(d=>d.Id!=saved.Id).Append(saved).ToArray();Persist(next);_devices=next;_checks.Remove(saved.Id);_replay=null;return saved;
        }
    }
    private static LanDevice NormalizeDevice(LanDevice input,LanDevice[] devices)
    {
            string name=(input.Name??"").Trim();if(name.Length is <1 or >80)throw new ArgumentException("Enter a device name of 1–80 characters.");
            if(!Types.Any(t=>t.Id==input.Type))throw new ArgumentException("Choose an available device type.");
            string address=(input.IpAddress??"").Trim();if(!IPAddress.TryParse(address,out var ip)||ip.AddressFamily!=System.Net.Sockets.AddressFamily.InterNetwork)throw new ArgumentException("Enter a private IPv4 address.");
            byte[] octets=ip.GetAddressBytes();if(!(octets[0]==10||octets[0]==172&&octets[1]>=16&&octets[1]<=31||octets[0]==192&&octets[1]==168)||octets[3] is 0 or 255)throw new ArgumentException("Use a private LAN host address, such as 192.168.50.112.");
            string mac=NormalizeMac(input.MacAddress);if(!Regex.IsMatch(mac,"^([0-9A-F]{2}:){5}[0-9A-F]{2}$")||(Convert.ToInt32(mac[..2],16)&1)!=0||mac=="00:00:00:00:00:00")throw new ArgumentException("Enter a unicast MAC address, such as 02:00:00:00:10:02.");
            Guid id=input.Id==Guid.Empty?Guid.NewGuid():input.Id;
            if(devices.Any(d=>d.Id!=id&&(d.IpAddress==ip.ToString()||d.MacAddress==mac)))throw new ArgumentException("Another device already uses that IP or MAC address.");
            var type=Types.Single(t=>t.Id==input.Type);
            if(input.ParentId!=null&&(!devices.Any(d=>d.Id==input.ParentId&&Types.Single(t=>t.Id==d.Type).Hub)||input.ParentId==id))throw new ArgumentException("Choose a registered Sync Module as the parent.");
            if(type.Hub&&input.ParentId!=null)throw new ArgumentException("A Sync Module cannot have a parent module.");
            if(devices.Any(d=>d.ParentId==id)&&!type.Hub)throw new ArgumentException("Reassign this module's cameras before changing its type.");
            if(input.Type=="blink-outdoor-3"&&input.ParentId==null)throw new ArgumentException("Choose the camera's parent Sync Module.");
            return input with{Id=id,Name=name,IpAddress=ip.ToString(),MacAddress=mac};
    }
    public void Remove(Guid id){lock(_gate){if(_devices.Any(d=>d.ParentId==id))throw new ArgumentException("Reassign or remove this module's cameras first.");if(!_devices.Any(d=>d.Id==id))throw new ArgumentException("Device was not found.");var next=_devices.Where(d=>d.Id!=id).ToArray();Persist(next);_devices=next;_checks.Remove(id);_replay=null;}}
    private void Persist(LanDevice[] devices,CameraActivityLink[]? activityLinks=null){
        var nextLinks=(activityLinks??ActivityLinks()).Where(l=>devices.Any(d=>d.Id==l.DeviceId&&d.Type=="blink-outdoor-3")).ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        File.WriteAllText(_path+".partial",JsonSerializer.Serialize(new LanDeviceConfiguration(1,devices,nextLinks),ConfigurationJson));
        File.Move(_path+".partial",_path,true);links=nextLinks;
    }
    public static string NormalizeMac(string? value){string raw=(value??"").Trim().Replace(":","").Replace("-","").ToUpperInvariant();return raw.Length==12?string.Join(":",Enumerable.Range(0,6).Select(i=>raw.Substring(i*2,2))):raw;}
    public async Task<LanReachability> CheckAsync(Guid id,CancellationToken ct=default)
    {
        LanDevice device=List().SingleOrDefault(d=>d.Id==id)??throw new ArgumentException("Device was not found.");bool reachable=false;
        try{using var ping=new Ping();var reply=await ping.SendPingAsync(IPAddress.Parse(device.IpAddress),TimeSpan.FromSeconds(2),new byte[16],null,ct);reachable=reply.Status==IPStatus.Success;}catch(PingException){}
        string status=reachable?"IP reachable — MAC identity not verified":Types.Single(t=>t.Id==device.Type).Battery?"Not responding / possibly asleep":"Not responding — availability unknown";
        var result=new LanReachability(id,DateTimeOffset.UtcNow,reachable,status);lock(_gate){if(_devices.Any(d=>d.Id==id&&d.IpAddress==device.IpAddress))_checks[id]=result;}return result;
    }
    public CaptureReplayResult ReplayCapture(Stream stream,string name){var devices=List();var result=CaptureActivityReader.Read(stream,Path.GetFileName(name),devices);lock(_gate)_replay=result;return result;}
    public void ClearReplay(){lock(_gate)_replay=null;}
}
