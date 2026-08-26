using System.Net.Mail;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace ScreenTimeGuardian.Contracts;

public static partial class ConfigurationValidation
{
    [GeneratedRegex("^(?=.{1,253}$)([a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\\.)+[a-z]{2,63}$", RegexOptions.IgnoreCase)]
    private static partial Regex DomainPattern();

    [GeneratedRegex("^S-1-\\d+(?:-\\d+)+$", RegexOptions.IgnoreCase)]
    private static partial Regex SidPattern();

    public static bool IsValidDomain(string value)
    {
        var normalized = PolicyEngine.NormalizeDomain(value);
        return DomainPattern().IsMatch(normalized);
    }

    public static bool IsValidEmail(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            var address = new MailAddress(value.Trim());
            return string.Equals(address.Address, value.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            // MailAddress throws ArgumentException on an empty input, not
            // FormatException. Treating both as "invalid" keeps the sanitizer
            // from crashing the whole save over one empty email field.
            return false;
        }
    }

    public static bool IsValidHttpsUrl(string value)
    {
        return Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && string.IsNullOrWhiteSpace(uri.UserInfo)
            && uri.Port == -1
            && !string.IsNullOrWhiteSpace(uri.Host);
    }

    public static bool IsValidRsaPublicKeyPem(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 16_384)
        {
            return false;
        }

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(value);
            return rsa.KeySize >= 2048;
        }
        catch (Exception exception) when (exception is ArgumentException or CryptographicException)
        {
            return false;
        }
    }

    public static bool IsValidOrigin(string value)
    {
        return Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && string.IsNullOrWhiteSpace(uri.UserInfo)
            && uri.Port == -1
            && string.IsNullOrWhiteSpace(uri.Query)
            && string.IsNullOrWhiteSpace(uri.Fragment)
            && !string.IsNullOrWhiteSpace(uri.Host);
    }

    public static string NormalizeOrigin(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrWhiteSpace(uri.UserInfo)
            || uri.Port != -1
            || !string.IsNullOrWhiteSpace(uri.Query)
            || !string.IsNullOrWhiteSpace(uri.Fragment)
            || string.IsNullOrWhiteSpace(uri.Host))
        {
            return string.Empty;
        }

        return $"{uri.Scheme}://{uri.Host}".ToLowerInvariant();
    }

    public static bool IsValidUserSid(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length is > 8 and < 200 && SidPattern().IsMatch(trimmed);
    }
}
