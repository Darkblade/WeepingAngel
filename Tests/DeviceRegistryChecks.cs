using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DontBlink.Services;

internal static class DeviceRegistryChecks
{
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message);}
    static void Reject(Action action,string message){try{action();throw new Exception("Expected rejection: "+message);}catch(ArgumentException){Check(true,message);}}
    public static async Task RunAsync(string root)
    {
        string path=Path.Combine(root,"lan-devices.json");var store=new DeviceRegistry(path);var initial=store.List();var hub=initial.Single(d=>d.Type=="blink-sync-2");var camera=initial.Single(d=>d.Type=="blink-outdoor-3");
        Check(initial.Length==2&&hub.IpAddress=="192.168.50.100"&&camera.ParentId==hub.Id,"registry seeds the supplied module and Garden-camera relationship");
        var edited=store.Save(camera with{Name="Garden-camera updated",MacAddress="02:00:00:00:10:02"});
        Check(new DeviceRegistry(path).List().Single(d=>d.Id==camera.Id).Name==edited.Name&&edited.MacAddress=="02:00:00:00:10:02","device edits and normalized MAC persist across restart");
        Reject(()=>store.Save(camera with{Id=Guid.Empty,Name="Duplicate"}),"duplicate IP/MAC is rejected");
        Reject(()=>store.Save(camera with{IpAddress="8.8.8.8"}),"connection tests cannot target public addresses");
        Reject(()=>store.Save(camera with{MacAddress="FF:FF:FF:FF:FF:FF"}),"broadcast MAC is rejected");
        Reject(()=>store.Save(camera with{ParentId=camera.Id}),"camera cannot be its own parent");
        Reject(()=>store.Remove(hub.Id),"module removal cannot orphan cameras");
        using(var capture=Synthetic(initial)){var replay=store.ReplayCapture(capture,"synthetic.pcap");var activity=replay.Activities.Single();Check(activity.DeviceId==camera.Id&&activity.ObservedBytes==400&&activity.ConnectionClosed,"replay identifies an IP/MAC-matched camera upload and deduplicates retransmission");Check(store.Replay!=null&&replay.Note.Contains("Historical replay"),"replay is explicitly historical");}
        using(var capture=Synthetic(initial)){var wrong=initial.Select(d=>d.Id==camera.Id?d with{MacAddress="74:AB:93:00:00:01"}:d).ToArray();Check(CaptureActivityReader.Read(capture,"wrong-mac.pcap",wrong).Activities.Length==0,"IP-only match does not bypass configured MAC identity");}
        using(var broken=new MemoryStream(new byte[5])){try{store.ReplayCapture(broken,"broken.eth");throw new Exception("Expected incomplete capture rejection");}catch(InvalidDataException){Check(true,"truncated capture produces a clear validation error");}}
        store.ClearReplay();Check(store.Replay==null,"replay state can be cleared without modifying devices");
        string archive=Path.Combine(root,"device-api-archive");Directory.CreateDirectory(archive);await using var server=new MobileServer(new ClipService(new OfflineSource(),archive),0,new ClientAccessStore(Path.Combine(root,"device-clients.json")),devices:store);
        await server.StartAsync(false,default);using var http=new HttpClient(new HttpClientHandler{CookieContainer=new CookieContainer()}){BaseAddress=new Uri(server.LocalAddress)};http.DefaultRequestHeaders.Add("X-DontBlink","1");
        Check((await http.GetAsync("/api/devices")).StatusCode==HttpStatusCode.Unauthorized,"device registry requires browser pairing");
        Check((await http.GetAsync("/api/local-activity")).StatusCode==HttpStatusCode.Unauthorized,"live local activity requires browser pairing");
        Check((await http.PostAsJsonAsync("/api/local-activity/events",new ExternalActivityEvent(camera.Id,"x","upload",40000,DateTimeOffset.UtcNow,"test"))).StatusCode==HttpStatusCode.Unauthorized,"external event input requires pairing");
        await http.PostAsJsonAsync("/api/pair",new{code=server.PairingCode});
        using(var response=await http.GetAsync("/api/devices")){var data=await response.Content.ReadFromJsonAsync<JsonElement>();Check(response.IsSuccessStatusCode&&data.GetProperty("devices").GetArrayLength()==2,"paired device registry is available without Blink authentication");Check(data.GetProperty("checks").GetArrayLength()==0,"registering devices does not automatically ping them");}
        Check((await http.GetStringAsync("/devices")).Contains("Test saved connection"),"browser device management page is served");
        var externalEvent=new ExternalActivityEvent(camera.Id,"first","upload",40000,DateTimeOffset.UtcNow,"paired test sensor");
        Check((await http.PostAsJsonAsync("/api/local-activity/events",externalEvent)).StatusCode==HttpStatusCode.Conflict,"external reports cannot activate an unselected provider");
        await store.ActivityMonitor.StartExternalAsync();Check((await http.PostAsJsonAsync("/api/local-activity/events",externalEvent)).IsSuccessStatusCode,"paired external producer can submit metadata without Blink login");await store.ActivityMonitor.StopAsync();
        var other=new LanDevice(Guid.Empty,"Local camera","rtsp-camera","192.168.50.200","02:00:00:00:00:01");var saved=await (await http.PostAsJsonAsync("/api/devices/save",other)).Content.ReadFromJsonAsync<LanDevice>();Check(saved!=null&&store.List().Length==3,"browser creates a device in the shared Windows registry");
        Check((await http.PostAsJsonAsync("/api/devices/save",other)).StatusCode==HttpStatusCode.BadRequest,"device API returns actionable validation errors");
        await http.PostAsJsonAsync("/api/devices/remove",new{id=saved!.Id});Check(store.List().Length==2,"browser removes registered devices");
        store.Remove(camera.Id);store.Remove(hub.Id);Check(new DeviceRegistry(path).List().Length==0,"removing all devices persists an empty registry without reseeding");
    }
    public static CaptureReplayResult ReplayFile(string file,string root){using var stream=File.OpenRead(file);var result=new DeviceRegistry(Path.Combine(root,"capture-devices.json")).ReplayCapture(stream,file);Console.WriteLine(JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));Check(result.Activities.Length==1&&result.Activities[0].ObservedBytes>1000000,"supplied ath0 capture identifies Garden-camera's local upload");return result;}
    private static MemoryStream Synthetic(LanDevice[] devices)
    {
        var camera=devices.Single(d=>d.Type=="blink-outdoor-3");var hub=devices.Single(d=>d.Type=="blink-sync-2");var stream=new MemoryStream();using var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true);writer.Write(0xa1b2c3d4u);writer.Write((ushort)2);writer.Write((ushort)4);writer.Write(0u);writer.Write(0u);writer.Write(65535u);writer.Write(1u);
        void Packet(int seconds,byte flags,uint sequence,int size,bool reverse=false){byte[] f=new byte[54+size];Convert.FromHexString((reverse?camera:hub).MacAddress.Replace(":","")).CopyTo(f,0);Convert.FromHexString((reverse?hub:camera).MacAddress.Replace(":","")).CopyTo(f,6);BinaryPrimitives.WriteUInt16BigEndian(f.AsSpan(12),0x800);f[14]=0x45;BinaryPrimitives.WriteUInt16BigEndian(f.AsSpan(16),(ushort)(40+size));f[23]=6;IPAddress.Parse((reverse?hub:camera).IpAddress).GetAddressBytes().CopyTo(f,26);IPAddress.Parse((reverse?camera:hub).IpAddress).GetAddressBytes().CopyTo(f,30);BinaryPrimitives.WriteUInt16BigEndian(f.AsSpan(34),(ushort)(reverse?443:60000));BinaryPrimitives.WriteUInt16BigEndian(f.AsSpan(36),(ushort)(reverse?60000:443));BinaryPrimitives.WriteUInt32BigEndian(f.AsSpan(38),sequence);f[46]=0x50;f[47]=flags;writer.Write(1793278800u+(uint)seconds);writer.Write(0u);writer.Write((uint)f.Length);writer.Write((uint)f.Length);writer.Write(f);}
        Packet(0,2,100,0);Packet(1,0x12,200,0,true);Packet(2,0x18,101,400);Packet(3,0x18,101,400);Packet(4,0x11,501,0);stream.Position=0;return stream;
    }
    private sealed class OfflineSource:IClipSource
    {
        public bool IsAuthenticated=>false;
        public Task<Blink.Models.SyncModule[]> ModulesAsync()=>throw new Exception("Device registry must not invoke Blink");
        public Task<IEnumerable<Blink.Models.BlinkVideoInfo>> ListAsync(Blink.Models.SyncModule module)=>throw new Exception("Device registry must not invoke Blink");
        public Task<byte[]> DownloadAsync(Blink.Models.BlinkVideoInfo video)=>throw new NotSupportedException();
        public Task DeleteAsync(Blink.Models.BlinkVideoInfo video)=>throw new NotSupportedException();
    }
}
