using System.Text.Json;

namespace DontBlink.Services;

public record RelayCertificate(string Host, string Fingerprint, string Subject, string Issuer, DateTime ValidFromUtc, DateTime ValidToUtc, string Errors);
public sealed class RelayTrustStore(string? path = null)
{
    private readonly string _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DontBlink", "relay-certificate-pins.json");
    private readonly object _lock = new();
    private sealed record Pin(string Host, string Fingerprint, DateTime ExpiresUtc);
    public bool IsTrusted(RelayCertificate certificate)
    {
        if (certificate.ValidFromUtc > DateTime.UtcNow || certificate.ValidToUtc <= DateTime.UtcNow) return false;
        lock (_lock) return Load().Any(p => p.Host.Equals(certificate.Host, StringComparison.OrdinalIgnoreCase)
            && p.Fingerprint == certificate.Fingerprint && p.ExpiresUtc > DateTime.UtcNow);
    }
    public void Approve(RelayCertificate certificate)
    {
        if (certificate.ValidFromUtc > DateTime.UtcNow || certificate.ValidToUtc <= DateTime.UtcNow)
            throw new IOException("An expired or not-yet-valid relay certificate cannot be pinned.");
        lock (_lock)
        {
            var pins = Load().Where(p => !p.Host.Equals(certificate.Host, StringComparison.OrdinalIgnoreCase) && p.ExpiresUtc > DateTime.UtcNow).ToList();
            DateTime expiry = new[] { certificate.ValidToUtc, DateTime.UtcNow.AddDays(30) }.Min();
            pins.Add(new(certificate.Host, certificate.Fingerprint, expiry));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            File.WriteAllText(_path + ".partial", JsonSerializer.Serialize(pins)); File.Move(_path + ".partial", _path, true);
        }
    }
    private Pin[] Load()
    {
        try { return JsonSerializer.Deserialize<Pin[]>(File.ReadAllText(_path)) ?? []; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return []; }
    }
}
