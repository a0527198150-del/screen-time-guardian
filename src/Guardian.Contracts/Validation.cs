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
}
