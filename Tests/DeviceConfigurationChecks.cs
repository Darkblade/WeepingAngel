using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DontBlink.Services;
internal static class DeviceConfigurationChecks
{
    public static async Task RunAsync(string root)
    {
        string folder=Path.Combine(root,"device-configuration");Directory.CreateDirectory(folder);string path=Path.Combine(folder,"source.json");var store=new DeviceRegistry(path);var original=store.List();var hub=original.Single(d=>d.Type=="blink-sync-2");var camera=original.Single(d=>d.Type=="blink-outdoor-3");
        store.LinkCamera(camera.Id,"blink",22,33);string exported=store.ExportConfiguration();var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);var bundle=JsonSerializer.Deserialize<LanDeviceConfiguration>(exported,options)!;
        Require(bundle.Devices.Length==2&&bundle.Links.Single().CameraId==33&&!exported.Contains("Password")&&!exported.Contains("Token"),"export contains identities, parent modules and links without credential fields");
        var target=new DeviceRegistry(Path.Combine(folder,"target.json"));target.ImportConfiguration(exported,true);Require(target.List().SequenceEqual(original)&&target.ActivityLinks().Single()==store.ActivityLinks().Single(),"replace import preserves original device IDs and camera bindings");
        var restarted=new DeviceRegistry(Path.Combine(folder,"target.json"));Require(restarted.List().SequenceEqual(original)&&restarted.ActivityLinks().Single().CameraId==33,"device/link import is persisted together and survives restart");
        var extra=target.Save(new(Guid.Empty,"Extra","generic-camera","192.168.50.200","02:00:00:00:00:01"));target.ImportConfiguration(exported);Require(target.List().Length==3&&target.List().Contains(extra),"merge preserves unrelated devices");
        string stable=target.ExportConfiguration();string disk=File.ReadAllText(Path.Combine(folder,"target.json"));
        void Reject(LanDeviceConfiguration value,string message){try{target.ImportConfiguration(JsonSerializer.Serialize(value,options),true);throw new Exception("Invalid import accepted");}catch(ArgumentException){}Require(target.ExportConfiguration()==stable&&File.ReadAllText(Path.Combine(folder,"target.json"))==disk,message);}
        Reject(bundle with{SchemaVersion=2},"unsupported schema leaves registry and links unchanged");
        Reject(bundle with{Devices=[hub,camera with{ParentId=Guid.NewGuid()}]},"orphaned parent rejects the entire import");
        Reject(bundle with{Devices=[hub,camera with{IpAddress=hub.IpAddress}]},"duplicate IP rejects the entire import");
        Reject(bundle with{Devices=[hub,camera with{MacAddress=hub.MacAddress.ToLowerInvariant().Replace(":","-")}]},"normalized MAC collision rejects the entire import");
        Reject(bundle with{Devices=[hub,camera with{Id=hub.Id}]},"duplicate device IDs reject the entire import");
        Reject(bundle with{Links=[new(Guid.NewGuid(),"blink",22,33)]},"unknown linked device rejects the entire import");
        Reject(bundle with{Links=[new(camera.Id,"blink",22,33),new(camera.Id,"blink",22,34)]},"duplicate source links reject the entire import");
        var duplicateCamera=camera with{Id=Guid.NewGuid(),IpAddress="192.168.50.210",MacAddress="02:00:00:00:00:02"};Reject(bundle with{Devices=[hub,camera,duplicateCamera],Links=[new(camera.Id,"blink",22,33),new(duplicateCamera.Id,"blink",22,33)]},"duplicate Guard Post target rejects the entire import");
        string legacy=Path.Combine(folder,"legacy.json");File.WriteAllText(legacy,JsonSerializer.Serialize(original));File.WriteAllText(legacy+".activity-links.json",JsonSerializer.Serialize(bundle.Links));var migrated=new DeviceRegistry(legacy);Require(migrated.ActivityLinks().Single().CameraId==33,"legacy separate device/link files remain readable");migrated.Save(camera with{Name="Legacy updated"});Require(new DeviceRegistry(legacy).ActivityLinks().Single().CameraId==33,"legacy files migrate together on the first save");migrated.Remove(camera.Id);Require(migrated.ActivityLinks().Length==0,"removing a camera removes its saved link");
        await using var server=new MobileServer(new ClipService(new Source(),Path.Combine(folder,"archive")),0,new ClientAccessStore(Path.Combine(folder,"clients.json")),devices:target);await server.StartAsync(false,default);using var http=new HttpClient(new HttpClientHandler{CookieContainer=new CookieContainer()}){BaseAddress=new Uri(server.LocalAddress)};http.DefaultRequestHeaders.Add("X-DontBlink","1");
        Require((await http.GetAsync("/api/devices/export")).StatusCode==HttpStatusCode.Unauthorized,"configuration export requires pairing");Require((await http.PostAsync("/api/devices/import",new StringContent(exported,Encoding.UTF8,"application/json"))).StatusCode==HttpStatusCode.Unauthorized,"configuration import requires pairing");await http.PostAsJsonAsync("/api/pair",new{code=server.PairingCode});
        using(var response=await http.GetAsync("/api/devices/export"))Require(response.IsSuccessStatusCode&&response.Content.Headers.ContentDisposition?.FileName?.Contains("weeping-angel-devices")==true,"paired export downloads a named JSON configuration");
        http.DefaultRequestHeaders.Remove("X-DontBlink");Require((await http.PostAsync("/api/devices/import",new StringContent(exported,Encoding.UTF8,"application/json"))).StatusCode==HttpStatusCode.Forbidden,"configuration import requires the mutation header");http.DefaultRequestHeaders.Add("X-DontBlink","1");
        Require((await http.PostAsync("/api/devices/import?replace=true",new StringContent(exported,Encoding.UTF8,"application/json"))).IsSuccessStatusCode&&target.List().Length==2,"paired browser can restore the complete configuration without Blink login");
        Require((await http.PostAsync("/api/devices/import",new StringContent("invalid",Encoding.UTF8,"application/json"))).StatusCode==HttpStatusCode.BadRequest,"malformed import returns an actionable validation error");
        Require((int)(await http.PostAsync("/api/devices/import",new StringContent(new string('x',DeviceRegistry.MaximumConfigurationBytes+1),Encoding.UTF8,"application/json"))).StatusCode==413,"oversized configuration is rejected before persistence");
    }
    private static void Require(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message);}
    private sealed class Source:IClipSource{public bool IsAuthenticated=>false;public Task<Blink.Models.SyncModule[]> ModulesAsync()=>throw new NotSupportedException();public Task<IEnumerable<Blink.Models.BlinkVideoInfo>> ListAsync(Blink.Models.SyncModule module)=>throw new NotSupportedException();public Task<byte[]> DownloadAsync(Blink.Models.BlinkVideoInfo video)=>throw new NotSupportedException();public Task DeleteAsync(Blink.Models.BlinkVideoInfo video)=>throw new NotSupportedException();}
}
