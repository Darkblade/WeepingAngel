using System.Security.Cryptography;
using System.Text.Json;

namespace DontBlink.Services;

internal sealed class BlinkSessionStore(string? path=null)
{
    private readonly string _path=path??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DontBlink","session.dat");
    internal record Session(string HardwareId,string? RefreshToken,string? Email);
    public Session LoadOrCreate()
    {
        if(File.Exists(_path)) {
            byte[] plain=CredentialStore.Transform(File.ReadAllBytes(_path),false);
            try {var value=JsonSerializer.Deserialize<Session>(plain)??throw new InvalidDataException("Invalid saved Blink session.");if(string.IsNullOrWhiteSpace(value.HardwareId))throw new InvalidDataException("Missing device identity.");return value;}
            finally {CryptographicOperations.ZeroMemory(plain);}
        }
        var created=new Session(Guid.NewGuid().ToString().ToUpperInvariant(),null,null);Save(created);return created;
    }
    public void Save(Session session)
    {
        byte[] plain=JsonSerializer.SerializeToUtf8Bytes(session);
        try {byte[] encrypted=CredentialStore.Transform(plain,true);Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);File.WriteAllBytes(_path+".partial",encrypted);File.Move(_path+".partial",_path,true);}
        finally {CryptographicOperations.ZeroMemory(plain);}
    }
}
