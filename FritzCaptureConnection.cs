using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DontBlink.Services;

public sealed record FritzCaptureOptions(string Router,string Username,string Password,string Interface="1-ath0",string Filter="",int SnapLength=160)
{
    public Uri BaseUri(){if(!Uri.TryCreate(Router,UriKind.Absolute,out var uri)||uri.Scheme is not("http" or "https")||uri.UserInfo!=""||uri.Query!=""||uri.Fragment!=""||uri.AbsolutePath!="/")throw new ArgumentException("Enter a router origin such as http://fritz.box (no session URL).");bool local=uri.Host.Equals("fritz.box",StringComparison.OrdinalIgnoreCase);if(IPAddress.TryParse(uri.Host.Trim('[',']'),out var ip)){var b=ip.GetAddressBytes();local=IPAddress.IsLoopback(ip)||ip.IsIPv6LinkLocal||ip.IsIPv6UniqueLocal||b.Length==4&&(b[0]==10||b[0]==192&&b[1]==168||b[0]==172&&b[1]>=16&&b[1]<=31);}if(!local)throw new ArgumentException("Use fritz.box or the router's private LAN IP address.");if(!Regex.IsMatch(Interface,"^[0-9]+-[a-zA-Z0-9_.-]{1,40}$")||Filter.Length>1024||SnapLength is <160 or >1600)throw new ArgumentException("Invalid capture interface, filter or snapshot length.");return uri;}
    public override string ToString()=>"FRITZ!Box capture (credentials redacted)";
}
public sealed class RouterLoginException(string message):IOException(message);

internal sealed class FritzCaptureConnection:IAsyncDisposable
{
    private readonly HttpClient client;
    private readonly Uri origin;
    private readonly FritzCaptureOptions options;
    private string sid="";
    private HttpResponseMessage? response;
    private bool captureIssued;
    public Stream Stream {get;private set;}=System.IO.Stream.Null;
    private FritzCaptureConnection(FritzCaptureOptions settings,HttpMessageHandler? handler){options=settings;origin=settings.BaseUri();client=handler==null?new(new HttpClientHandler{AllowAutoRedirect=false}):new(handler,false);client.Timeout=Timeout.InfiniteTimeSpan;}
    public static async Task<FritzCaptureConnection> OpenAsync(FritzCaptureOptions options,CancellationToken ct,HttpMessageHandler? handler=null)
    {
        var connection=new FritzCaptureConnection(options,handler);try{await connection.OpenCoreAsync(ct);return connection;}catch{await connection.DisposeAsync();throw;}
    }
    private async Task OpenCoreAsync(CancellationToken ct)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(20));var token=timeout.Token;
        using var initial=await client.GetAsync(new Uri(origin,"/login_sid.lua?version=2"),token);if(!initial.IsSuccessStatusCode)throw new RouterLoginException("Router login page unavailable; check address and permissions.");
        var challenge=ParseXml(await initial.Content.ReadAsStringAsync(token));int block=int.TryParse(challenge.Root?.Element("BlockTime")?.Value,out int delay)?delay:0;if(block>0)throw new RouterLoginException($"Router login is blocked for {block} seconds. Wait, then restart monitoring.");
        string value=challenge.Root?.Element("Challenge")?.Value??throw new RouterLoginException("Router returned no login challenge.");string answer=ChallengeResponse(value,options.Password);string username=ResolveUsername(challenge,options.Username,value.StartsWith("2$"));
        using var form=new FormUrlEncodedContent(new Dictionary<string,string>{{"username",username},{"response",answer}});using var login=await client.PostAsync(new Uri(origin,"/login_sid.lua?version=2"),form,token);
        if(!login.IsSuccessStatusCode)throw new RouterLoginException("Router login failed; check username and password.");sid=ParseXml(await login.Content.ReadAsStringAsync(token)).Root?.Element("SID")?.Value??"";if(!Regex.IsMatch(sid,"^[0-9a-fA-F]{16}$")||sid=="0000000000000000")throw new RouterLoginException("Router rejected the credentials or requires additional confirmation. Monitoring paused.");
        string path="/cgi-bin/capture_notimeout?sid="+Uri.EscapeDataString(sid)+"&capture=Start&snaplen="+options.SnapLength+"&filter="+Uri.EscapeDataString(options.Filter)+"&ifaceorminor="+Uri.EscapeDataString(options.Interface);
        captureIssued=true;using var request=new HttpRequestMessage(HttpMethod.Get,new Uri(origin,path));response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);
        if(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)throw new RouterLoginException("Router capture permission denied. Monitoring paused.");if(!response.IsSuccessStatusCode)throw new IOException("Router capture request failed.");
        if(response.Content.Headers.ContentType?.MediaType!="application/octet-stream")throw new RouterLoginException("Router did not return a capture stream. Check capture rights, interface and filter.");Stream=await response.Content.ReadAsStreamAsync(token);
    }
    internal static string ResolveUsername(XDocument login,string supplied,bool modern)
    {
        if(!string.IsNullOrWhiteSpace(supplied))return supplied.Trim();
        var users=login.Root?.Element("Users")?.Elements("User").Select(u=>u.Value.Trim()).Where(u=>u.Length>0).Distinct(StringComparer.Ordinal).ToArray()??[];
        if(users.Length==1)return users[0];
        var automatic=users.Where(u=>Regex.IsMatch(u,"^fritz[0-9]{4}$",RegexOptions.IgnoreCase)).ToArray();
        if(automatic.Length==1)return automatic[0];
        if(users.Length==0&&!modern)return "";
        throw new RouterLoginException("Router password-only user could not be selected automatically. Enter a FRITZ!Box username explicitly; no login attempt was made.");
    }
    private static XDocument ParseXml(string text){if(text.Length>65536)throw new RouterLoginException("Unexpected router login response.");try{return XDocument.Parse(text);}catch(System.Xml.XmlException){throw new RouterLoginException("Router did not return a valid login response.");}}
    internal static string ChallengeResponse(string challenge,string password)
    {
        if(challenge.StartsWith("2$")){var fields=challenge.Split('$');if(fields.Length!=5||!int.TryParse(fields[1],out int first)||!int.TryParse(fields[3],out int second)||first is <1 or >2000000||second is <1 or >2000000)throw new RouterLoginException("Unsupported router login challenge.");try{byte[] hash=Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password),Convert.FromHexString(fields[2]),first,HashAlgorithmName.SHA256,32);try{return fields[4]+"$"+Convert.ToHexString(Rfc2898DeriveBytes.Pbkdf2(hash,Convert.FromHexString(fields[4]),second,HashAlgorithmName.SHA256,32)).ToLowerInvariant();}finally{CryptographicOperations.ZeroMemory(hash);}}catch(FormatException){throw new RouterLoginException("Invalid router login challenge.");}}
        string legacy=challenge+"-"+string.Concat(password.EnumerateRunes().Select(r=>r.Value>255?".":r.ToString()));return challenge+"-"+Convert.ToHexString(MD5.HashData(Encoding.Unicode.GetBytes(legacy))).ToLowerInvariant();
    }
    public async ValueTask DisposeAsync()
    {
        response?.Dispose();if(captureIssued&&sid!=""){using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(3));try{string iface=options.Interface[(options.Interface.IndexOf('-')+1)..];using var stopped=await client.GetAsync(new Uri(origin,"/cgi-bin/capture_notimeout?sid="+Uri.EscapeDataString(sid)+"&iface="+Uri.EscapeDataString(iface)+"&minor=-1&type=1&capture=Stop&useajax=1&xhr=1"),timeout.Token);}catch(Exception ex)when(ex is HttpRequestException or OperationCanceledException){}}
        if(sid!=""){using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(3));try{using var form=new FormUrlEncodedContent(new Dictionary<string,string>{{"sid",sid},{"logout","1"}});using var loggedOut=await client.PostAsync(new Uri(origin,"/login_sid.lua?version=2"),form,timeout.Token);}catch(Exception ex)when(ex is HttpRequestException or OperationCanceledException){}}
        client.Dispose();sid="";
    }
}
