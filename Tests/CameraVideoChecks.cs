using Blink.Models;
using DontBlink.Services;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

internal static class CameraVideoChecks
{
    public static async Task RunAsync(string root)
    {
        using(var metadata=JsonDocument.Parse("{\"id\":3,\"network_id\":2,\"name\":\"Garden camera\",\"type\":\"catalina\"}")){
            var camera=CameraOverview.Parse(metadata.RootElement)!;Require(camera.Model=="catalina"&&camera.CanRequestVideo,"homescreen type enables Outdoor capture when product_type is absent");
        }
        using(var metadata=JsonDocument.Parse("{\"id\":3,\"network_id\":2,\"product_type\":\"OWL\",\"type\":\"catalina\"}")){
            var camera=CameraOverview.Parse(metadata.RootElement)!;Require(camera.Model=="owl"&&CameraVideoCommands.Family(camera)==BlinkCameraType.Mini,"explicit product type takes precedence and is normalized");
        }
        using(var metadata=JsonDocument.Parse("{\"id\":3,\"network_id\":2}"))Require(!CameraOverview.Parse(metadata.RootElement)!.CanRequestVideo,"missing camera family is not guessed");
        await ClientCompletionAsync();
        var source=new Source();var clips=new ClipService(source,Path.Combine(root,"video-command"));
        var logs=new List<string>();clips.Log+=logs.Add;
        var running=clips.RequestCameraVideoAsync(2,3,default);await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        try{await clips.RequestCameraVideoAsync(2,3,default);throw new Exception("Duplicate capture accepted");}catch(CameraRequestConflictException){}
        source.Resume.SetResult();var result=await running;
        Require(result.CommandCompleted&&source.Requests==1&&clips.Archive().Count==0&&result.Message.Contains("Refresh USB"),"video command completion does not invent an archived recording; duplicate requests rejected");
        Require(logs.Any(l=>l.Contains("requested"))&&logs.Any(l=>l.Contains("complete")),"capture has requested and command-completed diagnostics");
        using(var lease=await clips.ReserveLiveNetworkAsync(2,default))
            await Rejected(()=>clips.RequestCameraVideoAsync(2,3,default),typeof(IOException));
        Require(source.Requests==1,"active live session prevents a competing capture on the same module");
        await Rejected(()=>clips.RequestCameraVideoAsync(9,3,default),typeof(ArgumentException));
        source.Model="unknown";await Rejected(()=>clips.RequestCameraVideoAsync(2,3,default),typeof(NotSupportedException));
        Require(source.Requests==1,"wrong system and unsupported models never send a capture command");
        source.Model="catalina";source.Fail=true;await Rejected(()=>clips.RequestCameraVideoAsync(2,3,default),typeof(IOException));source.Fail=false;
        Require((await clips.RequestCameraVideoAsync(2,3,default)).CommandCompleted,"failed capture releases duplicate protection for an explicit retry");
        using(var cancelled=new CancellationTokenSource()){cancelled.Cancel();await Rejected(()=>clips.RequestCameraVideoAsync(2,3,cancelled.Token),typeof(OperationCanceledException));}
        source.IsAuthenticated=false;await Rejected(()=>clips.RequestCameraVideoAsync(2,3,default),typeof(BlinkSessionUnavailableException));source.IsAuthenticated=true;
        Require(CameraVideoCommands.Family(new(3,2,"Mini","owl","online","ok","Enabled"))==BlinkCameraType.Mini&&CameraVideoCommands.Family(new(3,2,"Doorbell","lotus","online","ok","Enabled"))==BlinkCameraType.Doorbell&&!CameraVideoCommands.IsSupported(new(3,2,"Other","catalina","online","ok","Enabled",ServiceId:"rtsp")),"video capture capabilities are provider and model scoped");
        await using var server=new MobileServer(clips,0,new ClientAccessStore(Path.Combine(root,"video-clients.json")));await server.StartAsync(false,default);
        using var http=new HttpClient(new HttpClientHandler{CookieContainer=new CookieContainer()}){BaseAddress=new Uri(server.LocalAddress)};
        http.DefaultRequestHeaders.Add("X-DontBlink","1");
        Require((await http.PostAsJsonAsync("/api/camera-video",new{network=2,camera=3})).StatusCode==HttpStatusCode.Unauthorized,"video capture requires browser pairing");
        await http.PostAsJsonAsync("/api/pair",new{code=server.PairingCode});
        http.DefaultRequestHeaders.Remove("X-DontBlink");
        Require((await http.PostAsJsonAsync("/api/camera-video",new{network=2,camera=3})).StatusCode==HttpStatusCode.Forbidden,"video capture requires the mutation header");http.DefaultRequestHeaders.Add("X-DontBlink","1");
        int before=source.Requests;source.IsAuthenticated=false;
        Require((int)(await http.PostAsJsonAsync("/api/camera-video",new{network=2,camera=3})).StatusCode==424&&source.Requests==before,"signed-out capture reports authentication required without calling the camera");source.IsAuthenticated=true;
        using(var response=await http.PostAsJsonAsync("/api/camera-video",new{network=2,camera=3})){
            var body=await response.Content.ReadFromJsonAsync<CameraVideoRequestResult>();Require(response.IsSuccessStatusCode&&body?.CommandCompleted==true,"paired HTTP capture returns command completion");}
        Require((await http.PostAsJsonAsync("/api/camera-video",new{network=9,camera=3})).StatusCode==HttpStatusCode.BadRequest,"HTTP capture rejects a mismatched system");
        using(var lease=await clips.ReserveLiveNetworkAsync(2,default))Require((await http.PostAsJsonAsync("/api/camera-video",new{network=2,camera=3})).StatusCode==HttpStatusCode.Conflict,"HTTP capture reports a live-module conflict");
    }
    private static async Task ClientCompletionAsync()
    {
        foreach(var kind in new[]{BlinkCameraType.Default,BlinkCameraType.Mini,BlinkCameraType.Doorbell})
        foreach(bool tracked in new[]{false,true}){
            var handler=new CaptureHandler(tracked);using var http=new HttpClient(handler){BaseAddress=new Uri("https://example.invalid")};
            var client=new Blink.BlinkClient{GeneralSleepTime=0};
            void Set(string name,object value)=>typeof(Blink.BlinkClient).GetField(name,System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.SetValue(client,value);
            Set("_tier","test");Set("_accountId",1);Set("_lastLoginResult",new LoginResult{AccessToken="synthetic",RefreshToken="synthetic",ValidUntil=DateTime.UtcNow.AddHours(1)});Set("_http",http);
            bool completed=await client.RequestNewVideoAsync(2,3,kind);
            string expected=kind==BlinkCameraType.Default?"/network/2/camera/3/clip":kind==BlinkCameraType.Mini?"/api/v1/accounts/1/networks/2/owls/3/clip":"/api/v1/accounts/1/networks/2/doorbells/3/clip";
            Require(completed==tracked&&handler.Path==expected&&handler.Polls==(tracked?1:0),"capture uses "+kind+" routing and requires a tracked successful command");
        }
    }
    private sealed class CaptureHandler(bool tracked):HttpMessageHandler
    {
        public string? Path;public int Polls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){
            string body;if(request.Method==HttpMethod.Post){Path=request.RequestUri!.AbsolutePath;body=tracked?"{\"id\":17,\"network_id\":2}":"{}";}else{Polls++;body="{\"complete\":true,\"status_code\":908}";}
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body,System.Text.Encoding.UTF8,"application/json")});
        }
    }
    private static async Task Rejected(Func<Task> action,Type expected){try{await action();}catch(Exception e)when(expected.IsAssignableFrom(e.GetType())){return;}throw new Exception("Expected "+expected.Name);}
    private static void Require(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message);}
    private sealed class Source:IClipSource
    {
        public bool IsAuthenticated{get;set;}=true;public bool SupportsVideoRequest=>true;public int Requests;public string Model="catalina";public bool Fail;
        public TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously),Resume=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<CameraOverview[]> CamerasAsync()=>Task.FromResult(new[]{new CameraOverview(3,2,"Garden",Model,"online","ok","Enabled")});
        public async Task RequestVideoAsync(int n,int c){Requests++;Started.TrySetResult();await Resume.Task;if(Fail)throw new IOException("Synthetic capture failure");}
        public Task<SyncModule[]> ModulesAsync()=>Task.FromResult(Array.Empty<SyncModule>());
        public Task<IEnumerable<BlinkVideoInfo>> ListAsync(SyncModule module)=>throw new NotSupportedException();
        public Task<byte[]> DownloadAsync(BlinkVideoInfo video)=>throw new NotSupportedException();public Task DeleteAsync(BlinkVideoInfo video)=>throw new NotSupportedException();
    }
}

