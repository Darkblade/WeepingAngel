using Blink.Models;
using DontBlink.Services;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
internal static class OfflineChecks
{
    public static async Task RunAsync(string root){
        string folder=Path.Combine(root,"offline"),archive=Path.Combine(folder,"archive"),cache=Path.Combine(folder,"camera-overview.json");Directory.CreateDirectory(archive);
        var source=new OfflineSource();var clips=new ClipService(source,archive,cache);var imageService=new CameraMediaService(clips);
        var first=await clips.CameraOverviewAsync(default);Require(first.ApiAvailable&&first.Cameras.Length==1,"authenticated camera overview is saved");
        await imageService.ImageAsync(2,3,false,default);source.IsAuthenticated=false;
        var restarted=new ClipService(source,archive,cache);int calls=source.Calls;
        var saved=await restarted.CameraOverviewAsync(default);Require(!saved.ApiAvailable&&saved.Cached&&saved.Cameras.Single().Name=="Saved garden"&&source.Calls==calls,"last camera overview survives restart without a Blink call");
        string key=new('C',64);File.WriteAllBytes(Path.Combine(archive,"saved.mp4"),new byte[1024]);File.WriteAllText(Path.Combine(archive,key+".json"),JsonSerializer.Serialize(new ArchiveItem(key,"Saved garden",DateTime.UtcNow,"saved.mp4",1024)));
        await using var server=new MobileServer(restarted,0,new ClientAccessStore(Path.Combine(folder,"clients.json")));
        await server.StartAsync(false,default);using var http=new HttpClient(new HttpClientHandler{CookieContainer=new CookieContainer()}){BaseAddress=new Uri(server.LocalAddress)};http.DefaultRequestHeaders.Add("X-DontBlink","1");
        Require((await http.GetAsync("/api/archive")).StatusCode==HttpStatusCode.Unauthorized,"local archive still requires browser pairing");
        Require((await http.PostAsJsonAsync("/api/pair",new{code=server.PairingCode})).IsSuccessStatusCode,"browser pairs while Blink is signed out");
        Require((await http.GetFromJsonAsync<ArchiveItem[]>("/api/archive"))!.Length==1,"paired archive listing works without Blink login");
        using(var media=await http.GetAsync("/media/"+key))Require(media.IsSuccessStatusCode&&(await media.Content.ReadAsByteArrayAsync()).Length==1024,"archive video stays playable while signed out");
        var snapshot=await http.GetFromJsonAsync<CameraSnapshot>("/api/camera-overview");Require(snapshot is{ApiAvailable:false,Cached:true}&&snapshot.Cameras.Length==1,"browser receives disabled cached camera entries without a 500 error");
        Require((await http.GetAsync("/camera-image/2/3")).IsSuccessStatusCode,"saved camera thumbnail survives restart without Blink login");
        foreach(string route in new[]{"/api/modules","/api/clips/1","/api/live/2/3?record=false"})Require((int)(await http.GetAsync(route)).StatusCode==424,"API operation requires Blink session: "+route);
        Require((int)(await http.PostAsJsonAsync("/api/camera-motion",new{network=2,camera=3,enabled=true})).StatusCode==424,"camera state cannot change while Blink is signed out");
        Require(source.Calls==calls,"offline archive and camera reads never invoke Blink");
        Require((await http.GetStringAsync("/phone-certificate")).Contains("Phone microphone setup"),"certificate link serves setup guidance instead of 404");
        Require((await http.GetStringAsync("/guard-post")).Contains("Guard Post"),"Guard Post page is available locally");
        Require((await http.GetAsync("/api/phone-certificate")).IsSuccessStatusCode,"certificate readiness check works without Blink login");
        source.IsAuthenticated=true;Require((await restarted.CameraOverviewAsync(default)).ApiAvailable,"camera overview becomes operational after Blink sign-in");
        source.Expire=true;Require(!(await restarted.CameraOverviewAsync(default)).ApiAvailable&&restarted.LastKnownCameras.Length==1,"session expiry preserves the camera overview");
    }
    private static void Require(bool pass,string message){if(!pass)throw new Exception(message);Console.WriteLine("PASS "+message);}
    private sealed class OfflineSource:IClipSource{
        public bool IsAuthenticated{get;set;}=true;public bool Expire;public int Calls;
        public Task<CameraOverview[]> CamerasAsync(){Calls++;if(Expire){IsAuthenticated=false;throw new BlinkSessionUnavailableException();}return Task.FromResult(new[]{new CameraOverview(3,2,"Saved garden","Outdoor","online","ok","Enabled")});}
        public Task<byte[]> CameraImageAsync(int network,int camera,bool capture){Calls++;using var bitmap=new Bitmap(32,18);using var stream=new MemoryStream();bitmap.Save(stream,System.Drawing.Imaging.ImageFormat.Jpeg);return Task.FromResult(stream.ToArray());}
        public Task<SyncModule[]> ModulesAsync(){Calls++;throw new Exception("Offline source must not be called.");}
        public Task<IEnumerable<BlinkVideoInfo>> ListAsync(SyncModule module){Calls++;throw new Exception("Offline source must not be called.");}
        public Task<byte[]> DownloadAsync(BlinkVideoInfo video)=>throw new NotSupportedException();
        public Task DeleteAsync(BlinkVideoInfo video)=>throw new NotSupportedException();
    }
}
