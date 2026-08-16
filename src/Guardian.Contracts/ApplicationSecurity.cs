using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace ScreenTimeGuardian.Contracts;

public sealed class ApplicationSecurity
{
    public string Salt { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public int Iterations { get; set; } = 210_000;

    [JsonIgnore]
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Salt)
        && !string.IsNullOrWhiteSpace(PasswordHash);
}

public static class ApplicationPassword
{
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const int DefaultIterations = 210_000;

    public static ApplicationSecurity Create(string password)
    {
        Validate(password);
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Derive(password, salt, DefaultIterations);
        return new ApplicationSecurity
        {
            Salt = Convert.ToBase64String(salt),
            PasswordHash = Convert.ToBase64String(hash),
            Iterations = DefaultIterations
        };
    }

    public static bool Verify(string password, ApplicationSecurity security)
    {
        if (!security.IsConfigured || string.IsNullOrEmpty(password))
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(security.Salt);
            var expected = Convert.FromBase64String(security.PasswordHash);
            var actual = Derive(password, salt, security.Iterations);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static void Validate(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
        {
            throw new ArgumentException("סיסמת האפליקציה חייבת להכיל לפחות 8 תווים.", nameof(password));
        }
    }

    private static byte[] Derive(string password, byte[] salt, int iterations)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            Math.Max(iterations, 100_000),
            HashAlgorithmName.SHA256,
            HashBytes);
    }
}

public sealed class GuardianCommand
{
    public string Type { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
    public ConfigurationDocument? Configuration { get; set; }
}

public sealed class GuardianCommandResponse
{
    public bool Ok { get; set; }
    public bool NeedsInitialization { get; set; }
    public string Error { get; set; } = string.Empty;
    public ConfigurationDocument? Configuration { get; set; }
}
