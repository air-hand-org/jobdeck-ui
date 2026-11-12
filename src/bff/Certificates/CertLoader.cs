using System.Security.Cryptography.X509Certificates;

namespace ui.Certificates;

public sealed class CertLoader
{
    public static X509Certificate2 LoadFromBase64Env(IConfiguration configuration, string envName)
    {
        var base64 = configuration[envName] ?? throw new InvalidOperationException($"{envName} is required");
        return LoadFromBase64(base64);
    }

    public static X509Certificate2 LoadFromBase64(string base64)
    {
        return X509CertificateLoader.LoadPkcs12(
            Convert.FromBase64String(base64),
            string.Empty,
            X509KeyStorageFlags.EphemeralKeySet
        );
    }

    public static X509Certificate2 LoadFromFile(string path)
    {
        return X509CertificateLoader.LoadPkcs12FromFile(
            path,
            string.Empty,
            X509KeyStorageFlags.EphemeralKeySet
        );
    }
}
