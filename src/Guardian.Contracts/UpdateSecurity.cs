using System.Security.Cryptography;
using System.Text;

namespace ScreenTimeGuardian.Contracts;

public static class UpdateSecurity
{
    public static string CanonicalPayload(string version, string packageUrl, string sha256)
        => $"{version.Trim()}\n{packageUrl.Trim()}\n{sha256.Trim().ToUpperInvariant()}";

    public static bool VerifySignature(
        string version,
        string packageUrl,
        string sha256,
        string signatureBase64,
        string publicKeyPem)
    {
        if (string.IsNullOrWhiteSpace(signatureBase64)
            || !ConfigurationValidation.IsValidHttpsUrl(packageUrl)
            || !IsSha256(sha256)
            || !ConfigurationValidation.IsValidRsaPublicKeyPem(publicKeyPem))
        {
            return false;
        }

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);
            var signature = Convert.FromBase64String(signatureBase64.Trim());
            return rsa.VerifyData(
                Encoding.UTF8.GetBytes(CanonicalPayload(version, packageUrl, sha256)),
                signature,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException)
        {
            return false;
        }
    }

    public static bool IsSha256(string value)
        => value.Trim().Length == 64
            && value.Trim().All(character => Uri.IsHexDigit(character));
}
