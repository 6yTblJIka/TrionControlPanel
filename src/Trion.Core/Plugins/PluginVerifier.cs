using System.Security.Cryptography;

namespace Trion.Core.Plugins;

/// <summary>
/// Verifies RSA-PKCS1v15-SHA256 signatures produced by the Phoenix server
/// at plugin upload time.  The matching private key lives only on the Phoenix
/// server and is never distributed.
/// </summary>
public static class PluginVerifier
{
    // Public key generated for the Trion plugin store.
    // The corresponding private key is stored at PLUGIN_SIGNING_KEY_PATH on the Phoenix server.
    private const string PublicKeyPem = """
        -----BEGIN PUBLIC KEY-----
        MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA7lT1KhRnNvb5nAWbwmbk
        foUXub4oClIoK8iEEHCJegQmIiIBdGzoCSDCTIERlywHbXs5Qw4GAL1YVQPfodTQ
        HpApZ12XifBHoJTK60y7vK038H9ztUmvl5Ln9NRHJ0J84gnpK8OexOzAzE1PeiHK
        LtVlzWoFaJS5JE07LQfJmg3jhFm2Crge55Qa+BHx2taniTpiqclVaFd67Dzoqexy
        rPxvuWN+AwOwcyJOvG2ZFg/ZHlY7RONvfbgivX8IpuRbwYn/1WTAQRBTjt8b7rLq
        NB5y6iJ829b+Lm+pviR7w5abllNw+M+CqOz5T79pD955iRkLVioC/wCTMKdcLFcb
        MwIDAQAB
        -----END PUBLIC KEY-----
        """;

    /// <summary>Verifies <paramref name="signature"/> against <paramref name="dllBytes"/>.</summary>
    public static bool Verify(byte[] dllBytes, byte[] signature)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(PublicKeyPem);
        return rsa.VerifyData(dllBytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }

    /// <summary>
    /// Verifies a DLL file using its companion <c>.sig</c> file (base64 signature).
    /// Returns false if the .sig file is missing.
    /// </summary>
    public static bool VerifyFile(string dllPath)
    {
        var sigPath = dllPath + ".sig";
        if (!File.Exists(sigPath)) return false;

        try
        {
            var dllBytes = File.ReadAllBytes(dllPath);
            var sigBytes = Convert.FromBase64String(File.ReadAllText(sigPath).Trim());
            return Verify(dllBytes, sigBytes);
        }
        catch
        {
            return false;
        }
    }
}
