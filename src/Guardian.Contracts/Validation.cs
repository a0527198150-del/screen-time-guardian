using System.Net.Mail;
using System.Text.RegularExpressions;

namespace ScreenTimeGuardian.Contracts;

public static partial class ConfigurationValidation
{
    [GeneratedRegex("^(?=.{1,253}$)([a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\\.)+[a-z]{2,63}$", RegexOptions.IgnoreCase)]
    private static partial Regex DomainPattern();

    public static bool IsValidDomain(string value)
    {
        var normalized = PolicyEngine.NormalizeDomain(value);
        return DomainPattern().IsMatch(normalized);
    }

    public static bool IsValidEmail(string value)
    {
        try
        {
            var address = new MailAddress(value.Trim());
            return string.Equals(address.Address, value.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        catch (FormatException)
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
        return trimmed.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase) && trimmed.Length is > 8 and < 200;
    }
}
