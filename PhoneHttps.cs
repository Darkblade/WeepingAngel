using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace DontBlink.Services;

public static class PhoneHttps
{
    private static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DontBlink","https");
    public static string CertificatePath => Path.Combine(Folder,"DontBlink-phone-CA.cer");
    // The private CA key stays encrypted with the current Windows user's DPAPI key.
    public static X509Certificate2 CreateServerCertificate(string? folder = null)
    {
        folder ??= Folder; Directory.CreateDirectory(folder);
        string keyFile=Path.Combine(folder,"ca.dpapi"),publicFile=Path.Combine(folder,"DontBlink-phone-CA.cer");
        X509Certificate2 ca;
        if(File.Exists(keyFile))
        {
            byte[] plain=CredentialStore.Transform(File.ReadAllBytes(keyFile),false);
            try{ca=X509CertificateLoader.LoadPkcs12(plain,null,X509KeyStorageFlags.EphemeralKeySet);}finally{CryptographicOperations.ZeroMemory(plain);}
        }
        else
        {
            using var rootKey=RSA.Create(3072);
            var request=new CertificateRequest("CN=DontBlink Local CA "+Guid.NewGuid().ToString("N")[..8],rootKey,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true,false,0,true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign|X509KeyUsageFlags.CrlSign,true));
            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey,false));
            ca=request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddYears(5));
            byte[] plain=ca.Export(X509ContentType.Pfx);
            try{File.WriteAllBytes(keyFile,CredentialStore.Transform(plain,true));}finally{CryptographicOperations.ZeroMemory(plain);}
        }
        using(ca)
        {
            if(ca.NotAfter.ToUniversalTime()<DateTime.UtcNow.AddDays(2))throw new IOException("Phone HTTPS CA has expired. Remove its trust from clients and recreate the HTTPS setup.");
            File.WriteAllBytes(publicFile,ca.Export(X509ContentType.Cert));
            using var leafKey=RSA.Create(2048);
            var request=new CertificateRequest("CN=DontBlink",leafKey,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false,false,0,true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature|X509KeyUsageFlags.KeyEncipherment,true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection {new Oid("1.3.6.1.5.5.7.3.1")},false));
            var san=new SubjectAlternativeNameBuilder();san.AddDnsName("localhost");san.AddDnsName(Environment.MachineName);san.AddIpAddress(IPAddress.Loopback);
            foreach(var address in NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==OperationalStatus.Up).SelectMany(n=>n.GetIPProperties().UnicastAddresses).Select(a=>a.Address).Where(a=>a.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork && !IPAddress.IsLoopback(a)).Distinct())san.AddIpAddress(address);
            request.CertificateExtensions.Add(san.Build());
            var end=DateTimeOffset.UtcNow.AddDays(365);if(end>ca.NotAfter.ToUniversalTime())end=ca.NotAfter.ToUniversalTime();
            using var certificate=request.Create(ca,DateTimeOffset.UtcNow.AddHours(-1),end,RandomNumberGenerator.GetBytes(16));
            return certificate.CopyWithPrivateKey(leafKey);
        }
    }
}
