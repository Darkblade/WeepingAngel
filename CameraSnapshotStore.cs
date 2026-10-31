using System.Text.Json;
namespace DontBlink.Services;

public sealed class BlinkSessionUnavailableException(Exception? inner=null):InvalidOperationException(UserMessage,inner)
{
    public const string UserMessage="Sign in to Blink in Windows to use live view, USB and camera controls. Local archive videos remain available.";
    public static bool IsAuthenticationFailure(Exception error){
        if(error is BlinkSessionUnavailableException)return true;
        if(error is HttpRequestException http&&http.StatusCode==System.Net.HttpStatusCode.Unauthorized)return true;
        if(error is Blink.Exceptions.BlinkClientException){
            string m=error.Message;
            return m.Contains("Unauthorized",StringComparison.OrdinalIgnoreCase)||m.Contains("HTTP 401",StringComparison.OrdinalIgnoreCase)||m.Contains("access token",StringComparison.OrdinalIgnoreCase)||m.Contains("refresh token",StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }
}
public record CameraSnapshot(CameraOverview[] Cameras,bool ApiAvailable,bool Cached,string? Message);
public sealed class CameraSnapshotStore(string? path)
{
    public static string DefaultPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DontBlink","camera-overview.json");
    private CameraOverview[]? cameras;
    public CameraOverview[] Read(){
        if(cameras!=null)return cameras.ToArray();
        try{cameras=path==null?[]:JsonSerializer.Deserialize<CameraOverview[]>(File.ReadAllText(path))??[];}
        catch(Exception e) when(e is IOException or JsonException or UnauthorizedAccessException){cameras=[];}
        return cameras.ToArray();
    }
    public void Write(CameraOverview[] value){
        cameras=value.ToArray();if(path==null)return;
        try{Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);File.WriteAllText(path+".partial",JsonSerializer.Serialize(cameras));File.Move(path+".partial",path,true);}
        catch(Exception e) when(e is IOException or UnauthorizedAccessException){ /* The in-memory overview remains available if disk persistence is unavailable. */ }
    }
}
