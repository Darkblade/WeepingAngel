using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DontBlink.Services;

public record ConnectedClient(string Id, string Name, string Address, DateTime LastSeenUtc, bool Trusted, DateTime ExpiresUtc);
public sealed class ClientAccessStore
{
    private readonly string _path;
    private readonly object _lock = new();
    private readonly Dictionary<string, ConnectedClient> _clients = new();
    public ClientAccessStore(string? path = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DontBlink", "trusted-clients.json");
        try
        {
            foreach (var client in JsonSerializer.Deserialize<ConnectedClient[]>(File.ReadAllText(_path)) ?? [])
                if (client.Trusted && client.ExpiresUtc > DateTime.UtcNow)
                    _clients[client.Id] = client with { LastSeenUtc = DateTime.MinValue };
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
    }
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public string Pair(string name, string address, bool trusted)
    {
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var client = new ConnectedClient(Hash(token), string.IsNullOrWhiteSpace(name) ? "Browser" : name.Trim()[..Math.Min(name.Trim().Length, 80)], address,
            DateTime.UtcNow, trusted, DateTime.UtcNow.AddDays(trusted ? 30 : 0.5));
        lock (_lock)
        {
            _clients[client.Id] = client;
            try { if (trusted) Save(); }
            catch { _clients.Remove(client.Id); throw; }
        }
        return token;
    }
    public ConnectedClient? Authenticate(string? token, string address)
    {
        if (token == null || token.Length != 64) return null;
        lock (_lock)
        {
            string key = Hash(token);
            if (!_clients.TryGetValue(key, out var client) || client.ExpiresUtc <= DateTime.UtcNow) return null;
            return _clients[key] = client with { LastSeenUtc = DateTime.UtcNow, Address = address };
        }
    }
    public List<ConnectedClient> Clients()
    {
        lock (_lock) return _clients.Values.Where(c => c.ExpiresUtc > DateTime.UtcNow).OrderByDescending(c => c.LastSeenUtc).ToList();
    }
    public void SetTrusted(string id, bool trust)
    {
        lock (_lock)
        {
            if (!_clients.TryGetValue(id, out var client)) return;
            _clients[id] = client with { Trusted = trust, ExpiresUtc = DateTime.UtcNow.AddDays(trust ? 30 : 0.5) };
            try { Save(); } catch { _clients[id] = client; throw; }
        }
    }
    public void Revoke(string id)
    {
        lock (_lock)
        {
            if (!_clients.Remove(id, out var client)) return;
            try { Save(); } catch { _clients[id] = client; throw; }
        }
    }
    public void ClearTemporary()
    {
        lock (_lock) foreach (var id in _clients.Values.Where(c => !c.Trusted).Select(c => c.Id).ToArray()) _clients.Remove(id);
    }
    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        File.WriteAllText(_path + ".partial", JsonSerializer.Serialize(_clients.Values.Where(c => c.Trusted && c.ExpiresUtc > DateTime.UtcNow)));
        File.Move(_path + ".partial", _path, true);
    }
}
