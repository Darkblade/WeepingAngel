using System.Net;
using System.Net.Http.Headers;
using System.Text;
using DontBlink.Services;
internal static class RouterMonitoringChecks
{
    public static async Task RunAsync(string root)
    {
        Check(FritzCaptureConnection.ChallengeResponse("2$10000$5A1711$2000$5A1722","1example!")=="5A1722$1798a1672bca7c6463d6b245f82b53703b0f50813401b03e4045a5861e689adb","FRITZ PBKDF2 matches the vendor's published vector");
        Check(FritzCaptureConnection.ChallengeResponse("1234567z","äbc")=="1234567z-9e224a41eeefa284df7bb0f26c2913e2","FRITZ legacy challenge matches the vendor vector");
        try{new FritzCaptureOptions("http://public.example","u","p").BaseUri();throw new Exception("Public router accepted");}catch(ArgumentException){Check(true,"router capture restricts its destination to configured LAN origins");}
        Check(!new FritzCaptureOptions("http://fritz.box","u","sensitive-password").ToString().Contains("sensitive-password"),"router options redact credentials");
        var registry=new DeviceRegistry(Path.Combine(root,"router-devices.json"));var camera=registry.List().Single(d=>d.Type=="blink-outdoor-3");var hub=registry.List().Single(d=>d.Type=="blink-sync-2");
        byte[] Frame(byte flags,uint seq,int size,bool reverse=false){var packet=LiveActivityChecks.Packet(camera,hub,61000,flags,seq,size,reverse);if(size>0){byte[] shortPacket=packet[..(16+160)];System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(shortPacket.AsSpan(8),160);return shortPacket;}return packet;}
        byte[] bytes=LiveActivityChecks.Header().Concat(Frame(2,100,0)).Concat(Frame(0x12,200,0,true)).Concat(Frame(0x18,101,40000)).ToArray();
        using var handler=new RouterHandler(bytes);await using var follower=new LiveActivityFollower(registry.List);await follower.StartRouterAsync(new("http://fritz.box","test-user","test-password",Filter:"tcp port 443"),handler);await Wait(()=>follower.Snapshot().Activities.Any(a=>a.Pulsing));
        Check(follower.Snapshot().Activities.Single().ObservedBytes==40000&&handler.Starts==1,"header-only router stream produces live upload activity without capture files");
        await follower.StopAsync();Check(handler.Stops==1&&!follower.Snapshot().Running,"stopping router monitoring cancels the stream and sends capture Stop");
        using(var passwordOnly=new RouterHandler(bytes){AdvertisedUsers="<Users><User last='0'>another-user</User><User last='1'>fritz1234</User></Users>",ExpectedUsername="fritz1234"}){await follower.StartRouterAsync(new("http://fritz.box","","test-password"),passwordOnly);await Wait(()=>follower.Snapshot().Activities.Any(a=>a.Pulsing));await follower.StopAsync();Check(passwordOnly.Logins==1,"password-only login discovers the generated router username before one login attempt");}
        Check(FritzCaptureConnection.ResolveUsername(System.Xml.Linq.XDocument.Parse("<SessionInfo><Users><User>only-user</User></Users></SessionInfo>"),"",true)=="only-user","blank username selects a single advertised account");
        Check(FritzCaptureConnection.ResolveUsername(System.Xml.Linq.XDocument.Parse("<SessionInfo/>"),"",false)=="","legacy password-only login can still send an empty username");
        try{FritzCaptureConnection.ResolveUsername(System.Xml.Linq.XDocument.Parse("<SessionInfo><Users><User>a</User><User>b</User></Users></SessionInfo>"),"",true);throw new Exception("Ambiguous user accepted");}catch(RouterLoginException){Check(true,"ambiguous account discovery never guesses or cycles through accounts");}
        using var rejected=new RouterHandler(bytes){RejectLogin=true};await follower.StartRouterAsync(new("http://fritz.box","test-user","wrong"),rejected);await Wait(()=>!follower.Snapshot().Running);await Task.Delay(1200);Check(rejected.Logins==1&&rejected.Starts==0,"rejected router credentials pause without repeated login attempts");
        using var ended=new RouterHandler(LiveActivityChecks.Header()){EndImmediately=true};await follower.StartRouterAsync(new("http://fritz.box","test-user","test-password"),ended);await Wait(()=>ended.Starts>=2);await follower.StopAsync();int starts=ended.Starts;await Task.Delay(1200);Check(starts==ended.Starts&&ended.Stops>=2,"ended router streams reconnect with backoff and stop cancels retrying");
        await follower.StartExternalAsync();var report=new ExternalActivityEvent(camera.Id,"one","upload",40000,DateTimeOffset.UtcNow,"test sensor");follower.AcceptExternal(report);Check(follower.Snapshot().Activities.Single().Pulsing&&follower.Snapshot().Activities.Single().Source.Contains("External report"),"external metadata is explicitly source-labelled");follower.AcceptExternal(report);Check(follower.Snapshot().Activities.Length==1,"external event retries are deduplicated");
        try{follower.AcceptExternal(report with{EventId="old",ObservedUtc=DateTimeOffset.UtcNow.AddMinutes(-1)});throw new Exception("Stale accepted");}catch(ArgumentException){Check(true,"stale external reports are rejected");}
        follower.AcceptExternal(report with{EventId="end",Kind="end"});Check(!follower.Snapshot().Activities.Single().Pulsing,"external end event clears the pulse");await follower.StopAsync();
    }
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message);}
    private static async Task Wait(Func<bool> condition){using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(6));while(!condition())await Task.Delay(20,timeout.Token);}
    private sealed class RouterHandler(byte[] capture):HttpMessageHandler
    {
        public int Starts,Stops,Logins;public bool RejectLogin,EndImmediately;public string AdvertisedUsers="",ExpectedUsername="test-user";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            var uri=request.RequestUri!;if(uri.AbsolutePath=="/login_sid.lua"){if(request.Content!=null&&(await request.Content.ReadAsStringAsync(ct)).Contains("logout=1"))return Xml("<SessionInfo><SID>0000000000000000</SID></SessionInfo>");if(request.Method==HttpMethod.Get)return Xml("<SessionInfo><SID>0000000000000000</SID><Challenge>2$1$11$1$22</Challenge><BlockTime>0</BlockTime>"+AdvertisedUsers+"</SessionInfo>");Logins++;string form=await request.Content!.ReadAsStringAsync(ct);Check(form.Contains("username="+ExpectedUsername)&&!form.Contains("test-password"),"router password is sent only as challenge response");return Xml("<SessionInfo><SID>"+(RejectLogin?"0000000000000000":"0123456789abcdef")+"</SID></SessionInfo>");}
            if(uri.AbsolutePath=="/cgi-bin/capture_notimeout"&&uri.Query.Contains("capture=Stop")){Stops++;return new(HttpStatusCode.OK){Content=new StringContent("stopped")};}
            if(uri.AbsolutePath=="/cgi-bin/capture_notimeout"&&uri.Query.Contains("capture=Start")){Starts++;Check(uri.Query.Contains("snaplen=160"),"router capture requests bounded short snapshots");var response=new HttpResponseMessage(HttpStatusCode.OK){Content=new StreamContent(EndImmediately?new MemoryStream(capture):new WaitingStream(capture))};response.Content.Headers.ContentType=new MediaTypeHeaderValue("application/octet-stream");return response;}
            throw new Exception("Unexpected router request");
        }
        private static HttpResponseMessage Xml(string text)=>new(HttpStatusCode.OK){Content=new StringContent(text,Encoding.UTF8,"text/xml")};
    }
    private sealed class WaitingStream(byte[] bytes):Stream
    {
        private int position;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken ct=default){if(position<bytes.Length){int n=Math.Min(buffer.Length,Math.Min(7,bytes.Length-position));bytes.AsMemory(position,n).CopyTo(buffer);position+=n;return n;}await Task.Delay(Timeout.Infinite,ct);return 0;}
        public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;public override long Length=>throw new NotSupportedException();public override long Position{get=>position;set=>throw new NotSupportedException();}public override int Read(byte[] b,int o,int n)=>ReadAsync(b.AsMemory(o,n)).AsTask().GetAwaiter().GetResult();public override void Flush(){}public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException();public override void SetLength(long n)=>throw new NotSupportedException();public override void Write(byte[] b,int o,int n)=>throw new NotSupportedException();
    }
}