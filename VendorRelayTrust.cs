using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace DontBlink.Services;

public static class VendorRelayTrust
{
    private static readonly Lazy<X509Certificate2Collection> Certificates = new(() =>
    {
        string resource = typeof(VendorRelayTrust).Assembly.GetManifestResourceNames().Single(n => n.EndsWith("immis_ca.pem"));
        using var stream = typeof(VendorRelayTrust).Assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        var certificates = new X509Certificate2Collection(); certificates.ImportFromPem(reader.ReadToEnd()); return certificates;
    });
    public static string TlsIdentity(string host) => IPAddress.TryParse(host, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork ? "*.immedia-semi.com" : host;
    public static bool IsKnownCertificate(X509Certificate2 certificate)
    {
        if (certificate.NotBefore.ToUniversalTime() > DateTime.UtcNow || certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow) return false;
        if (!certificate.GetNameInfo(X509NameType.DnsName, false).Equals("*.immedia-semi.com", StringComparison.OrdinalIgnoreCase)) return false;
        byte[] fingerprint = certificate.GetCertHash(HashAlgorithmName.SHA256);
        return Certificates.Value.Cast<X509Certificate2>().Any(known => CryptographicOperations.FixedTimeEquals(fingerprint, known.GetCertHash(HashAlgorithmName.SHA256)));
    }
}
