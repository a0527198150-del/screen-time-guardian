using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using ScreenTimeGuardian.Contracts;

namespace ScreenTimeGuardian.Service;

public sealed record SafetyState(bool EnforcementAllowed, bool SafeMode, string Reason);

/// <summary>
/// The guard rail around every enforcement action.
///
/// The previous version of this service ran enforcement immediately at boot with
/// StartupType=Automatic. When enforcement destabilised Windows the machine restarted,
/// the service started again, and the cycle repeated. That is the reboot loop.
///
/// This class breaks the loop with five independent brakes. Any one of them being
/// engaged disables all enforcement.
/// </summary>
public sealed class SafetyEnvelope
{
    private const int SM_CLEANBOOT = 67;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private readonly ILogger<SafetyEnvelope> _logger;
    private readonly object _sync = new();
    private readonly Queue<DateTimeOffset> _recentActions = new();

    private long _serviceStartTicks;
    private bool _breakerTripped;
    private string _breakerReason = string.Empty;

    public SafetyEnvelope(ILogger<SafetyEnvelope> logger)
    {
        _logger = logger;
    }

    public DateTimeOffset ServiceStartedUtc { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>Called once when the service starts, before any enforcement runs.</summary>
    public void Initialize()
    {
        _serviceStartTicks = Environment.TickCount64;
        ServiceStartedUtc = DateTimeOffset.UtcNow;

        try
        {
            Directory.CreateDirectory(ConfigPaths.RuntimeDirectory);

            // BRAKE 1: crash detection. If the marker survived, the previous run did not
            // shut down cleanly. Refuse to enforce anything until a human clears it.
            if (File.Exists(ConfigPaths.BootMarkerFile))
            {
                var previous = File.ReadAllText(ConfigPaths.BootMarkerFile).Trim();
                TripSafeMode(
                    $"ההפעלה הקודמת של השירות ({previous}) לא הסתיימה בצורה תקינה. ייתכן שהמחשב קרס. " +
                    "האכיפה מושבתת עד לאישור ידני מלוח הבקרה.");
            }

            File.WriteAllText(ConfigPaths.BootMarkerFile, DateTimeOffset.Now.ToString("u"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(exception, "Could not initialise the safety envelope; enforcement will stay disabled");
            TripSafeMode("לא ניתן לכתוב לתיקיית ה־runtime. האכיפה מושבתת מטעמי בטיחות.");
        }
    }

    /// <summary>Called on a clean service stop. Removing the marker says "this run ended well".</summary>
    public void Shutdown()
    {
        try
        {
            if (File.Exists(ConfigPaths.BootMarkerFile))
            {
                File.Delete(ConfigPaths.BootMarkerFile);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Could not remove the boot marker on shutdown");
        }
    }

    public SafetyState Evaluate(SafetySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        lock (_sync)
        {
            if (_breakerTripped)
            {
                return new SafetyState(false, true, _breakerReason);
            }
        }

        // BRAKE 2: automatic safe mode flag left by a previous trip.
        if (File.Exists(ConfigPaths.SafeModeFlagFile))
        {
            var reason = TryReadFlag(ConfigPaths.SafeModeFlagFile);
            return new SafetyState(false, true, reason);
        }

        // BRAKE 3: manual panic switch. An administrator drops a file called SAFEMODE
        // into C:\ProgramData\ScreenTimeGuardian and everything stops at once.
        if (File.Exists(ConfigPaths.ManualKillSwitchFile))
        {
            var owner = TryReadOwner(ConfigPaths.ManualKillSwitchFile);
            _logger.LogWarning(
                "SAFEMODE kill switch is present. File owner: {Owner}, created: {Created:u}",
                owner,
                File.GetCreationTimeUtc(ConfigPaths.ManualKillSwitchFile));
            return new SafetyState(false, true,
                "קובץ SAFEMODE קיים בתיקיית ההגדרות. כל האכיפה מושבתת ידנית.");
        }

        // BRAKE 4: Windows Safe Mode. Recovery must never be fought by this service.
        if (GetSystemMetrics(SM_CLEANBOOT) != 0)
        {
            return new SafetyState(false, false, "Windows פועל במצב בטוח. האכיפה מושבתת.");
        }

        // BRAKE 5: grace periods. There is always a window after boot and after a service
        // start in which you can log in, open the control panel and fix a bad rule.
        var systemUptimeSeconds = Environment.TickCount64 / 1000d;
        if (systemUptimeSeconds < settings.BootGraceSeconds)
        {
            var remaining = (int)(settings.BootGraceSeconds - systemUptimeSeconds);
            return new SafetyState(false, false, $"תקופת חסד לאחר הפעלת המחשב. האכיפה תתחיל בעוד {remaining} שניות.");
        }

        var serviceUptimeSeconds = (Environment.TickCount64 - _serviceStartTicks) / 1000d;
        if (serviceUptimeSeconds < settings.ServiceGraceSeconds)
        {
            var remaining = (int)(settings.ServiceGraceSeconds - serviceUptimeSeconds);
            return new SafetyState(false, false, $"תקופת חסד לאחר הפעלת השירות. האכיפה תתחיל בעוד {remaining} שניות.");
        }

        return new SafetyState(true, false, "האכיפה פעילה.");
    }

    /// <summary>
    /// Every enforcement action must be registered here. Too many in a short window
    /// means something is wrong, and the circuit breaker shuts enforcement down rather
    /// than letting a loop run away.
    /// </summary>
    public bool RegisterAction(SafetySettings settings, string description)
    {
        lock (_sync)
        {
            if (_breakerTripped)
            {
                return false;
            }

            var now = DateTimeOffset.UtcNow;
            _recentActions.Enqueue(now);
            while (_recentActions.Count > 0 && now - _recentActions.Peek() > TimeSpan.FromMinutes(1))
            {
                _recentActions.Dequeue();
            }

            if (_recentActions.Count > settings.MaxActionsPerMinute)
            {
                TripSafeMode(
                    $"מפסק הבטיחות נכנס לפעולה: יותר מ־{settings.MaxActionsPerMinute} פעולות אכיפה בדקה אחת " +
                    $"(האחרונה: {description}). האכיפה מושבתת עד לאישור ידני.");
                return false;
            }

            return true;
        }
    }

    public void TripSafeMode(string reason)
    {
        lock (_sync)
        {
            _breakerTripped = true;
            _breakerReason = reason;
        }

        _logger.LogCritical("SAFE MODE: {Reason}", reason);

        try
        {
            Directory.CreateDirectory(ConfigPaths.RuntimeDirectory);
            File.WriteAllText(ConfigPaths.SafeModeFlagFile, $"{DateTimeOffset.Now:u}{Environment.NewLine}{reason}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(exception, "Could not persist the safe mode flag");
        }
    }

    /// <summary>Called from the control panel after the password is verified.</summary>
    public void ClearSafeMode()
    {
        lock (_sync)
        {
            _breakerTripped = false;
            _breakerReason = string.Empty;
            _recentActions.Clear();
        }

        try
        {
            if (File.Exists(ConfigPaths.SafeModeFlagFile))
            {
                File.Delete(ConfigPaths.SafeModeFlagFile);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(exception, "Could not clear the safe mode flag");
        }

        _logger.LogWarning("Safe mode cleared by an authenticated operator");
    }

    private static string TryReadOwner(string path)
    {
        try
        {
            return new FileInfo(path).GetAccessControl().GetOwner(typeof(NTAccount))?.ToString() ?? "לא ידוע";
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return "לא זמין";
        }
    }

    private static string TryReadFlag(string path)
    {
        try
        {
            var content = File.ReadAllText(path).Trim();
            return content.Length == 0 ? "האכיפה מושבתת במצב בטוח." : content;
        }
        catch (IOException)
        {
            return "האכיפה מושבתת במצב בטוח.";
        }
    }
}
