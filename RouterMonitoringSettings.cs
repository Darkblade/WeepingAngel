using System.Text.Json;
namespace DontBlink.Services;
public record RouterMonitoringSettings(string Router="http://fritz.box",string Username="",string Interface="1-ath0",string Filter="",bool RememberCredentials=false,bool StartWithApp=false)
{
    public static string Folder=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DontBlink");
    public static string PathName=>Path.Combine(Folder,"router-monitoring.json");
    public static CredentialStore Secrets()=>new(Path.Combine(Folder,"router-monitoring-credentials.dat"));
    public static RouterMonitoringSettings Load(){try{return JsonSerializer.Deserialize<RouterMonitoringSettings>(File.ReadAllText(PathName))??new();}catch(Exception ex)when(ex is IOException or JsonException or UnauthorizedAccessException){return new();}}
    public void Save(){Directory.CreateDirectory(Folder);File.WriteAllText(PathName+".partial",JsonSerializer.Serialize(this));File.Move(PathName+".partial",PathName,true);}
    public string SavedPassword(){if(!RememberCredentials)return "";try{var saved=Secrets().Load();return saved?.Email==Router+"\n"+Username?saved.Password:"";}catch{return "";}}
}