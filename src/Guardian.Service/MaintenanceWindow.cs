using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using ScreenTimeGuardian.Contracts;

namespace ScreenTimeGuardian.Service;

/// <summary>
/// Reads and writes the maintenance window file. When the file exists and its
/// timestamp has not passed, the service and folder locks are temporarily removed
/// so that updates and configuration changes can proceed.
///
/// The file is written only by the service (running as SYSTEM) after password
/// verification. The control panel never writes it directly.
/// </summary>
public sealed class MaintenanceWindow
{
    private readonly ILogger<MaintenanceWindow> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    public MaintenanceWindow(ILogger<MaintenanceWindow> logger)
    {
        _logger = logger;
    }

    /// <summary>Check whether a valid (non-expired) maintenance window is open.</summary>
    public bool IsOpen()
    {
        try
        {
            if (!File.Exists(ConfigPaths.UnlockFile))
            {
                return false;
            }

            var json = File.ReadAllText(ConfigPaths.UnlockFile);
            var data = JsonSerializer.Deserialize<UnlockData>(json, JsonOptions);
            if (data is null || string.IsNullOrEmpty(data.UnlockedUntilUtc))
            {
                return false;
            }

            if (DateTimeOffset.TryParse(data.UnlockedUntilUtc, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var until))
            {
                return DateTimeOffset.UtcNow < until;
            }

            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Could not read maintenance window file");
            return false;
        }
    }

    /// <summary>Open a maintenance window for the given duration. Called after password verification.</summary>
    public void Open(DateTimeOffset until)
    {
        try
        {
            Directory.CreateDirectory(ConfigPaths.RuntimeDirectory);
            var data = new UnlockData
            {
                UnlockedUntilUtc = until.ToString("O"),
                Reason = "maintenance"
            };
            File.WriteAllText(ConfigPaths.UnlockFile, JsonSerializer.Serialize(data, JsonOptions));
            _logger.LogWarning("Maintenance window opened until {Until:u}", until);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(exception, "Could not write maintenance window file");
        }
    }

    /// <summary>Close the maintenance window immediately. Called on clean shutdown.</summary>
    public void Close()
    {
        try
        {
            if (File.Exists(ConfigPaths.UnlockFile))
            {
                File.Delete(ConfigPaths.UnlockFile);
                _logger.LogInformation("Maintenance window closed");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Could not remove maintenance window file");
        }
    }

    private sealed class UnlockData
    {
        public string UnlockedUntilUtc { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
    }
}
