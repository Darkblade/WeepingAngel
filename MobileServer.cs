using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;

namespace DontBlink.Services;

public sealed class MobileServer(ClipService clips, int port = 5187, ClientAccessStore? clients = null, CameraMediaService? images = null, LiveViewService? live = null, DeviceRegistry? devices = null) : IAsyncDisposable
{
    private sealed record DeviceRequest(Guid Id);
    private WebApplication? _app;
    private readonly DeviceRegistry _devices = devices ?? new();
    public event Action<string>? Log;
    public string PairingCode { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    public string Addresses { get; private set; } = "";
    public string LocalAddress { get; private set; } = "";
    public string DesktopToken() => _clients.Pair("DontBlink desktop player", "127.0.0.1", false);
    private readonly ClientAccessStore _clients = clients ?? new();
    private readonly ThumbnailService _thumbnails = new(clips);
    private readonly CameraMediaService _images = images ?? new(clips);
    private readonly LiveViewService _live = live ?? new(clips);
    private System.Security.Cryptography.X509Certificates.X509Certificate2? _httpsCertificate;

    public async Task StartAsync(bool lan, CancellationToken ct)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [] });
        builder.Logging.ClearProviders();
        bool secure = lan && AppSettings.Load().SecurePhone && port != 0;
        if (secure) _httpsCertificate = PhoneHttps.CreateServerCertificate();
        builder.WebHost.UseKestrel(o => {
            o.Listen(lan ? IPAddress.Any : IPAddress.Loopback, port);
            if(secure)o.Listen(IPAddress.Any,port+1,https=>https.UseHttps(_httpsCertificate!));
        });
        var app = builder.Build();
        app.UseWebSockets(new Microsoft.AspNetCore.Builder.WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(15) });
        _app = app;
        string resource = typeof(MobileServer).Assembly.GetManifestResourceNames().Single(n => n.EndsWith("Mobile.index.html"));
        using var reader = new StreamReader(typeof(MobileServer).Assembly.GetManifestResourceStream(resource)!);
        string html = await reader.ReadToEndAsync(ct);
        using var playerReader = new StreamReader(typeof(MobileServer).Assembly.GetManifestResourceStream(
            typeof(MobileServer).Assembly.GetManifestResourceNames().Single(n => n.EndsWith("Mobile.live-player.js")))!);
        string playerScript = await playerReader.ReadToEndAsync(ct);
        using var liveReader = new StreamReader(typeof(MobileServer).Assembly.GetManifestResourceStream(typeof(MobileServer).Assembly.GetManifestResourceNames().Single(n => n.EndsWith("Mobile.live.html")))!);
        string livePage = await liveReader.ReadToEndAsync(ct);
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; media-src 'self' blob:; img-src 'self' blob:; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
            var remote = context.Connection.RemoteIpAddress;
            if (remote == null || !IsPrivate(remote)) { context.Response.StatusCode = 403; return; }
            // Custom header makes cross-origin form posts unable to invoke state changes.
            if (context.Request.Method != "GET" && context.Request.Headers["X-DontBlink"] != "1")
            { context.Response.StatusCode = 403; return; }
            var client = _clients.Authenticate(context.Request.Cookies["dontblink"], remote.ToString());
            if (context.Request.Path != "/" && context.Request.Path != "/guard-post" && context.Request.Path != "/phone-certificate" && !context.Request.Path.StartsWithSegments("/assets") && context.Request.Path != "/api/pair" && client == null)
            { context.Response.StatusCode = 401; return; }
            if (client?.Trusted == true)
                context.Response.Cookies.Append("dontblink", context.Request.Cookies["dontblink"]!, Cookie(context, client.ExpiresUtc - DateTime.UtcNow));
            bool blinkRoute=context.Request.Path=="/api/modules"||context.Request.Path.StartsWithSegments("/api/clips")||context.Request.Path=="/api/batch"||context.Request.Path=="/api/camera-video"||context.Request.Path=="/api/camera-refresh"||context.Request.Path=="/api/camera-motion"||context.Request.Path.StartsWithSegments("/api/live")||context.Request.Path.StartsWithSegments("/api/talk");
            if(blinkRoute&&!clips.ApiAvailable){context.Response.StatusCode=424;await context.Response.WriteAsJsonAsync(new{code="blink_auth_required",error=BlinkSessionUnavailableException.UserMessage});return;}
            try { await next(context); }
            catch(BlinkSessionUnavailableException){if(!context.Response.HasStarted){context.Response.StatusCode=424;await context.Response.WriteAsJsonAsync(new{code="blink_auth_required",error=BlinkSessionUnavailableException.UserMessage});}}
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
            catch (Exception ex)
            {
                Log?.Invoke($"Phone request {context.Request.Path} failed: {ex.Message}");
                if (!context.Response.HasStarted) { context.Response.StatusCode = 500; await context.Response.WriteAsJsonAsync(new { error = "Operation failed. Check the Windows app log and Blink connection." }); }
            }
        });
        app.MapGet("/", () => Results.Content(html, "text/html; charset=utf-8"));
        string EmbeddedPage(string name){using var page=new StreamReader(typeof(MobileServer).Assembly.GetManifestResourceStream(typeof(MobileServer).Assembly.GetManifestResourceNames().Single(n=>n.EndsWith("Mobile."+name+".html")))!);return page.ReadToEnd();}
        app.MapGet("/phone-certificate",()=>Results.Content(EmbeddedPage("phone-certificate"),"text/html; charset=utf-8"));
        app.MapGet("/guard-post",()=>Results.Content(EmbeddedPage("guard-post"),"text/html; charset=utf-8"));
        object CertificateState(){bool ready=File.Exists(PhoneHttps.CertificatePath);string? fingerprint=null;if(ready){using var cert=System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadCertificateFromFile(PhoneHttps.CertificatePath);fingerprint=cert.GetCertHashString(HashAlgorithmName.SHA256);}return new{ready,fingerprint,httpsEnabled=secure,httpsPort=port==0?(int?)null:port+1};}
        app.MapGet("/management",()=>Results.Content(EmbeddedPage("management"),"text/html; charset=utf-8"));
        app.MapGet("/api/management",async(bool? refresh,HttpContext context)=>Results.Json(await clips.ManagementAsync(refresh==true,context.RequestAborted)));
        app.MapGet("/api/archive-progress",()=>Results.Json(clips.Progress));
        app.MapPost("/api/system-arm",async(SystemArmRequest request,HttpContext context)=>Results.Json(await clips.SetArmedAsync(request.Service,request.System,request.Armed,context.RequestAborted)));
        app.MapGet("/api/device-settings",async(string? service,int network,int camera,HttpContext context)=>Results.Json(await clips.CameraSettingsAsync(service??"blink",network,camera,context.RequestAborted)));
        app.MapPost("/api/device-setting",async(DeviceSettingRequest request,HttpContext context)=>Results.Json(await clips.SetCameraSettingAsync(request.Service,request.Network,request.Camera,request.Setting,request.Value,context.RequestAborted)));
        app.MapGet("/devices",()=>Results.Content(EmbeddedPage("devices"),"text/html; charset=utf-8"));
        app.MapPost("/api/local-activity/events",(ExternalActivityEvent request)=>{try{_devices.ActivityMonitor.AcceptExternal(request);return Results.Ok();}catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}catch(InvalidOperationException ex){return Results.Conflict(new{error=ex.Message});}});
        app.MapGet("/api/local-activity",()=>Results.Json(_devices.LiveOverview()));
        app.MapGet("/api/devices/export",(HttpContext context)=>{context.Response.Headers.CacheControl="no-store";return Results.File(Encoding.UTF8.GetBytes(_devices.ExportConfiguration()),"application/json","weeping-angel-devices.json");});
        app.MapPost("/api/devices/import",async Task<IResult>(HttpContext context,bool? replace)=>{
            using var input=new MemoryStream();byte[] buffer=new byte[8192];int count;
            while((count=await context.Request.Body.ReadAsync(buffer,context.RequestAborted))>0){if(input.Length+count>DeviceRegistry.MaximumConfigurationBytes)return Results.Json(new{error="Configuration file exceeds 2 MiB."},statusCode:413);input.Write(buffer,0,count);}
            try{var imported=_devices.ImportConfiguration(Encoding.UTF8.GetString(input.ToArray()).TrimStart('\uFEFF'),replace==true);return Results.Json(new{devices=imported.Devices.Length,links=imported.Links.Length});}catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}
        });
        app.MapGet("/api/devices",()=>Results.Json(_devices.Overview()));
        app.MapPost("/api/devices/save",(LanDevice request)=>{try{return Results.Json(_devices.Save(request));}catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}});
        app.MapPost("/api/devices/remove",(DeviceRequest request)=>{try{_devices.Remove(request.Id);return Results.Ok();}catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}});
        app.MapPost("/api/devices/check",async Task<IResult> (DeviceRequest request,CancellationToken token)=>{try{return Results.Json(await _devices.CheckAsync(request.Id,token));}catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}});
        app.MapGet("/api/phone-certificate",()=>Results.Json(CertificateState()));
        app.MapPost("/api/phone-certificate/prepare",()=>{using var cert=PhoneHttps.CreateServerCertificate();(AppSettings.Load() with{SecurePhone=true}).Save();return Results.Json(CertificateState());});
        app.MapGet("/phone-certificate/download",()=>File.Exists(PhoneHttps.CertificatePath)?Results.File(PhoneHttps.CertificatePath,"application/pkix-cert","WeepingAngel-phone-CA.cer"):Results.Redirect("/phone-certificate"));
        app.MapGet("/assets/live-player.js", () => Results.Content(playerScript, "text/javascript; charset=utf-8"));
        foreach (var asset in new[]{ "branding.css", "themes.css", "mobile.css", "theme.js", "guard-post.js", "table-sort.js", "management.js", "device-ui.js", "guard-activity.js" })
        {
            using var assetReader = new StreamReader(typeof(MobileServer).Assembly.GetManifestResourceStream(typeof(MobileServer).Assembly.GetManifestResourceNames().Single(n => n.EndsWith("Mobile." + asset)))!);
            string contents = await assetReader.ReadToEndAsync(ct);
            string type = asset.EndsWith(".css") ? "text/css; charset=utf-8" : "text/javascript; charset=utf-8";
            app.MapGet("/assets/" + asset, () => Results.Content(contents, type));
        }
        foreach(var asset in new[]{("angel.ico","Assets.Branding.favicon.ico","image/x-icon"),("angel.png","Assets.Branding.gui.png","image/png"),("CinzelDecorative-Bold.ttf","Assets.Fonts.CinzelDecorative-Bold.ttf","font/ttf")})
        {
            byte[] data=DontBlink.Branding.Read(asset.Item2);
            app.MapGet("/assets/"+asset.Item1,()=>Results.File(data,asset.Item3));
        }
        app.MapGet("/assets/talk.js", async () => {
            using var script = new StreamReader(typeof(MobileServer).Assembly.GetManifestResourceStream(typeof(MobileServer).Assembly.GetManifestResourceNames().Single(n => n.EndsWith("Mobile.talk.js")))!);
            return Results.Content(await script.ReadToEndAsync(), "text/javascript; charset=utf-8");
        });
        app.MapPost("/api/live-client-log", (LiveClientLog request) =>
        {
            string message = request.Message ?? "";
            message = message.Replace('\r',' ').Replace('\n',' ');
            Log?.Invoke($"Live browser camera {request.Camera}: {message[..Math.Min(300,message.Length)]}");
            return Results.Ok();
        });
        app.MapPost("/api/pair", (PairRequest request, HttpContext context) =>
        {
            if (!Equal(request.Code, PairingCode)) return Results.Unauthorized();
            string token = _clients.Pair(request.Name ?? "Browser", context.Connection.RemoteIpAddress!.ToString(), request.Trust);
            context.Response.Cookies.Append("dontblink", token, Cookie(context, request.Trust ? TimeSpan.FromDays(30) : TimeSpan.FromHours(12)));
            return Results.Ok();
        });
        // Keep the browser contract independent of Blink models' JSON field names.
        app.MapGet("/api/modules", async Task<IResult> (HttpContext context) => Results.Json(
            (await clips.ModulesAsync(context.RequestAborted)).Select(m => new { id = m.Id, networkId = m.NetworkId })));
        app.MapGet("/api/clips/{module:int}", async (int module, HttpContext context) => Results.Json(await clips.ListAsync(module, context.RequestAborted)));
        app.MapPost("/api/batch", async (BatchRequest request, HttpContext context) =>
        {
            if (request.Keys == null || request.Keys.Length > 1000 || request.Keys.Any(k => k == null)) return Results.BadRequest();
            return Results.Json(await clips.BatchAsync(request.ModuleId, request.Keys, request.Delete, context.RequestAborted, request.ArchiveAndRemove));
        });
        app.MapGet("/api/archive", (HttpContext context) => { context.Response.Headers.CacheControl = "no-store"; return Results.Json(clips.Archive()); });
        app.MapGet("/api/events", async Task (HttpContext context) =>
        {
            context.Response.ContentType = "text/event-stream";
            context.Response.Headers["X-Accel-Buffering"] = "no";
            using var subscription = clips.Notifications.Subscribe();
            await context.Response.WriteAsync(": connected\n\n", context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
            while (!context.RequestAborted.IsCancellationRequested)
            {
                using var poll = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
                poll.CancelAfter(TimeSpan.FromSeconds(15));
                ArchiveNotifications.Delivery? delivery = null;
                try { delivery = await subscription.Reader.ReadAsync(poll.Token); }
                catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested) { }
                try
                {
                    if (_clients.Authenticate(context.Request.Cookies["dontblink"], context.Connection.RemoteIpAddress!.ToString()) == null)
                        throw new IOException("Client access was revoked.");
                    string data = delivery == null ? ": heartbeat\n\n" : "data: " + System.Text.Json.JsonSerializer.Serialize(delivery.Notice, System.Text.Json.JsonSerializerOptions.Web) + "\n\n";
                    await context.Response.WriteAsync(data, context.RequestAborted);
                    await context.Response.Body.FlushAsync(context.RequestAborted);
                    delivery?.Sent.TrySetResult();
                }
                catch (Exception ex) { delivery?.Sent.TrySetException(ex); throw; }
            }
        });
        app.MapGet("/thumbnail/{key}", async Task<IResult> (string key, HttpContext context) =>
            await _thumbnails.GetAsync(key, context.RequestAborted) is byte[] bytes ? Results.File(bytes, "image/jpeg") : Results.NotFound());
        app.MapGet("/api/blink-session",()=>Results.Json(new{authenticated=clips.ApiAvailable}));
        app.MapGet("/api/camera-overview",async Task<IResult> (HttpContext context)=>Results.Json(await clips.CameraOverviewAsync(context.RequestAborted)));
        app.MapGet("/api/cameras", async Task<IResult> (HttpContext context) => Results.Json(await clips.CamerasAsync(context.RequestAborted)));
        app.MapPost("/api/camera-motion", async Task<IResult> (MotionRequest request, HttpContext context) =>
        {
            if (_live.IsActive(request.Network, request.Camera)) return Results.Conflict(new { error = "Stop live viewing before changing motion detection." });
            _devices.ActivityMonitor.TestLog.Marker("Motion recording "+(request.Enabled?"On":"Off"),"requested",request.Network,request.Camera);
            try{await clips.CameraOperationAsync(async source => { await source.SetMotionAsync(request.Network, request.Camera, request.Enabled); return true; }, context.RequestAborted, request.Network);_devices.ActivityMonitor.TestLog.Marker("Motion recording "+(request.Enabled?"On":"Off"),"command_completed",request.Network,request.Camera);}catch{_devices.ActivityMonitor.TestLog.Marker("Motion recording","failed",request.Network,request.Camera);throw;}
            return Results.Ok();
        });
        app.MapGet("/camera-image/{network:int}/{camera:int}", async Task<IResult> (int network,int camera,HttpContext context)=>{
            if(!clips.ApiAvailable)return _images.CachedImage(network,camera) is byte[] cached?Results.File(cached,"image/jpeg"):Results.NotFound();
            return Results.File(await _images.ImageAsync(network,camera,false,context.RequestAborted),"image/jpeg");
        });
        app.MapPost("/api/camera-video", async Task<IResult> (CameraRequest request,HttpContext context)=>{
            _devices.ActivityMonitor.TestLog.Marker("Request video clip","requested",request.Network,request.Camera);
            try{
                var result=await clips.RequestCameraVideoAsync(request.Network,request.Camera,context.RequestAborted);
                _devices.ActivityMonitor.TestLog.Marker("Request video clip","command_completed",request.Network,request.Camera);
                return Results.Json(result);
            }
            catch(Exception ex) when(ex is ArgumentException or NotSupportedException or CameraRequestConflictException or IOException){
                _devices.ActivityMonitor.TestLog.Marker("Request video clip","failed",request.Network,request.Camera);
                return ex is ArgumentException?Results.BadRequest(new{error=ex.Message}):ex is NotSupportedException?Results.Json(new{error=ex.Message},statusCode:422):Results.Conflict(new{error=ex.Message});
            }
        });
        app.MapPost("/api/camera-refresh", async Task<IResult> (CameraRequest request, HttpContext context) =>
        {
            if (_live.IsActive(request.Network, request.Camera)) return Results.Conflict(new { error = "Stop live viewing before requesting a snapshot." });
            _devices.ActivityMonitor.TestLog.Marker("Update Image","requested",request.Network,request.Camera);
            byte[] captured;try{captured=await _images.ImageAsync(request.Network,request.Camera,true,context.RequestAborted);}catch{_devices.ActivityMonitor.TestLog.Marker("Update Image","failed",request.Network,request.Camera);throw;}
            _devices.ActivityMonitor.TestLog.Marker("Update Image","completed",request.Network,request.Camera);
            context.Response.Headers.CacheControl="no-store"; return Results.File(captured,"image/jpeg");
        });
        app.MapGet("/api/live/{network:int}/{camera:int}", async Task (int network, int camera, bool? record, string? session, HttpContext context) =>
        {
            context.Response.ContentType = "video/mp4";
            _devices.ActivityMonitor.TestLog.Marker("Live View","requested",network,camera);
            try{await _live.StreamAsync(network, camera, record == true, context.Response.Body, context.RequestAborted, session);}finally{_devices.ActivityMonitor.TestLog.Marker("Live View","ended",network,camera);}
        });
        app.MapGet("/api/talk/{network:int}/{camera:int}", async (int network, int camera, string session, int rate, HttpContext context) =>
        {
            string origin = context.Request.Headers.Origin.ToString();
            if (!context.WebSockets.IsWebSocketRequest || origin != $"{context.Request.Scheme}://{context.Request.Host}" || session.Length is <32 or >64 || rate is <8000 or >96000)
            { context.Response.StatusCode=403; return; }
            using var socket=await context.WebSockets.AcceptWebSocketAsync();
            await _live.TalkAsync(network,camera,session,socket,rate,context.RequestAborted);
        });
        app.MapGet("/api/live-status/{network:int}/{camera:int}", (int network, int camera) => Results.Json(new
        {
            active = _live.IsActive(network,camera), error = _live.LastError(network,camera), canRecord = _live.CanRecord(network,camera),
            recording = _live.CaptureStatus(network,camera).Recording, savedFile = _live.CaptureStatus(network,camera).SavedFile, recordingError = _live.CaptureStatus(network,camera).Error, finalizing = _live.CaptureStatus(network,camera).Finalizing
        }));
        app.MapPost("/api/live-record", async Task<IResult> (LiveRecordRequest request) =>
            Results.Json(await _live.SetRecordingAsync(request.Network,request.Camera,request.Recording)));
        app.MapGet("/live", () => Results.Content(livePage, "text/html; charset=utf-8"));
        app.MapGet("/recording-settings", async () => {
            using var page = new StreamReader(typeof(MobileServer).Assembly.GetManifestResourceStream(typeof(MobileServer).Assembly.GetManifestResourceNames().Single(n => n.EndsWith("Mobile.recording-settings.html")))!);
            return Results.Content(await page.ReadToEndAsync(), "text/html; charset=utf-8");
        });
        app.MapGet("/api/recording-overlay", () => Results.Json((AppSettings.Load().Overlay ?? new()).Validated()));
        app.MapPost("/api/recording-overlay", (RecordingOverlay overlay) => { (AppSettings.Load() with { Overlay = overlay.Validated() }).Save(); return Results.Ok(); });
        app.MapGet("/archive-player", (string key) => ClipService.ValidKey(key)
            ? Results.Content("<!doctype html><meta name=viewport content='width=device-width,initial-scale=1'><title>DontBlink recording</title><style>body{margin:0;background:#101820;color:white;font:16px system-ui}video{width:100%;height:90vh}</style><video controls autoplay playsinline src='/media/" + key + "'></video><script>addEventListener('pagehide',()=>{const v=document.querySelector('video');v.pause();v.removeAttribute('src');v.load();});</script>", "text/html; charset=utf-8")
            : Results.BadRequest());
        app.MapGet("/media/{key}", (string key, bool? download) => clips.ArchivePath(key) is string path
            ? Results.File(path, "video/mp4", fileDownloadName: download == true ? Path.GetFileName(path) : null, enableRangeProcessing: true)
            : Results.NotFound());
        await app.StartAsync(ct);
        int boundPort = new Uri(app.Urls.First(u=>u.StartsWith("http://"))).Port;
        LocalAddress = $"http://127.0.0.1:{boundPort}";
        Addresses = lan ? string.Join("   ", NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address).Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !IPAddress.IsLoopback(a) && IsPrivate(a))
            .SelectMany(a => secure ? new[]{ $"https://{a}:{port+1}", $"http://{a}:{boundPort}" } : new[]{ $"http://{a}:{boundPort}" })) : $"http://127.0.0.1:{boundPort}";
        if (string.IsNullOrEmpty(Addresses)) Addresses = "No LAN IPv4 address found; http://127.0.0.1:5187 is available on this PC.";
    }

    private static bool Equal(string? actual, string expected) => actual != null && CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(actual), Encoding.UTF8.GetBytes(expected));
    private static CookieOptions Cookie(HttpContext context, TimeSpan age) => new()
    { HttpOnly = true, SameSite = SameSiteMode.Strict, Secure = context.Request.IsHttps, MaxAge = age, Path = "/" };
    private static bool IsPrivate(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return true;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var b = address.GetAddressBytes();
        return b.Length == 4 && (b[0] == 10 || (b[0] == 192 && b[1] == 168) || (b[0] == 172 && b[1] >= 16 && b[1] <= 31));
    }
    public async ValueTask DisposeAsync()
    {
        if (_app != null) { await _app.StopAsync(); await _app.DisposeAsync(); _app = null; }
        _clients.ClearTemporary();
        _httpsCertificate?.Dispose(); _httpsCertificate=null;
    }
    public record SystemArmRequest(string Service,int System,bool Armed);
    public record DeviceSettingRequest(string Service,int Network,int Camera,string Setting,string Value);
    public record PairRequest(string Code, string? Name = null, bool Trust = false);
    public record BatchRequest(int ModuleId, string[] Keys, bool Delete, bool ArchiveAndRemove = false);
    public record CameraRequest(int Network, int Camera);
    public record MotionRequest(int Network, int Camera, bool Enabled);
    public record LiveClientLog(int Camera, string? Message);
    public record LiveRecordRequest(int Network, int Camera, bool Recording);
}

