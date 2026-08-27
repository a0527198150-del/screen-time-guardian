using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using ScreenTimeGuardian.Contracts;

namespace ScreenTimeGuardian.Service;

/// <summary>
/// Applies an explicit Deny ACE to the service's own security descriptor so that
/// stopping, reconfiguring, or deleting it fails — for administrators too.
///
/// Windows evaluates a DACL in order and stops at the first match, so a Deny entry
/// placed before the Allow entries wins over them. That is the whole mechanism.
///
/// This is friction, not a wall. An administrator holding SeTakeOwnershipPrivilege
/// can seize the service and rewrite this descriptor; nothing in user mode prevents
/// that. What it buys is that removal becomes four deliberate steps instead of one,
/// which is exactly the point for a tool someone applies to themselves.
///
/// SYSTEM keeps full control unconditionally. Locking out SYSTEM would leave the
/// service unable to manage itself and the machine unable to repair it.
/// </summary>
public sealed class ServiceLock
{
    private const string ServiceName = "ScreenTimeGuardian";

    // Deny Administrators: WP = stop, DC = change config, SD = delete,
    // WD = rewrite this very descriptor, WO = change owner.
    // The Deny entry must come FIRST — order decides the outcome.
    //
    // IMPORTANT: The exact SDDL must be read from the real machine with:
    //   sc.exe sdshow ScreenTimeGuardian
    // and the Deny entry prepended before the existing Allow entries.
    // The strings below are the canonical form; adjust if the real output
    // includes additional ACEs (e.g. Interactive Users, Service Accounts).
    private const string LockedSddl =
        "D:(D;;WPDCSDWDWO;;;BA)" +
        "(A;;CCLCSWRPWPDTLOCRRC;;;SY)" +
        "(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)" +
        "(A;;CCLCSWLOCRRC;;;IU)" +
        "(A;;CCLCSWLOCRRC;;;SU)";

    private const string UnlockedSddl =
        "D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)" +
        "(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)" +
        "(A;;CCLCSWLOCRRC;;;IU)" +
        "(A;;CCLCSWLOCRRC;;;SU)";

    private readonly ILogger<ServiceLock> _logger;
    private bool _lastAppliedLocked;
    private bool _hasApplied;

    public ServiceLock(ILogger<ServiceLock> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Apply or remove the Deny ACE on the service. Caches the last state to avoid
    /// redundant sc.exe calls. The guide requires calling this every cycle, but
    /// only invoking sc.exe when the desired state differs from the last applied state.
    /// </summary>
    public void Apply(bool locked)
    {
        if (_hasApplied && _lastAppliedLocked == locked)
        {
            return;
        }

        var sddl = locked ? LockedSddl : UnlockedSddl;
        var info = new ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = $"sdset {ServiceName} \"{sddl}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(info);
        if (process is null)
        {
            _logger.LogError("Could not start sc.exe to set the service descriptor");
            return;
        }

        process.WaitForExit(15000);

        if (process.ExitCode != 0)
        {
            _logger.LogError(
                "sc sdset failed with exit code {Code}: {Error}",
                process.ExitCode,
                process.StandardError.ReadToEnd().Trim());
            return;
        }

        _lastAppliedLocked = locked;
        _hasApplied = true;
        _logger.LogInformation("Service descriptor set to {State}", locked ? "locked" : "unlocked");
    }

    /// <summary>
    /// Open a maintenance window: unlock first, then record the window.
    /// If the write fails, the service re-locks on the next cycle — failing
    /// closed rather than open.
    /// </summary>
    public void OpenMaintenanceWindow(MaintenanceWindow maintenance, DateTimeOffset until)
    {
        Apply(locked: false);
        maintenance.Open(until);
    }

    /// <summary>
    /// Lock the installation folder against deletion by administrators.
    /// Does not affect SYSTEM or the service process itself.
    /// </summary>
    public void LockInstallFolder()
    {
        try
        {
            var installRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "ScreenTimeGuardian");

            if (!Directory.Exists(installRoot))
            {
                return;
            }

            var directoryInfo = new DirectoryInfo(installRoot);
            var security = directoryInfo.GetAccessControl();

            // Only add the Deny rule if it's not already present
            var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            var denyRule = new FileSystemAccessRule(
                administrators,
                FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Deny);

            var existing = security.GetAccessRules(true, false, typeof(SecurityIdentifier))
                .OfType<FileSystemAccessRule>()
                .Any(r =>
                    r.AccessControlType == AccessControlType.Deny
                    && r.IdentityReference.Translate(typeof(SecurityIdentifier)) is SecurityIdentifier sid
                    && sid.Value == administrators.Value
                    && r.FileSystemRights.HasFlag(FileSystemRights.Delete));

            if (!existing)
            {
                security.AddAccessRule(denyRule);
                directoryInfo.SetAccessControl(security);
                _logger.LogInformation("Install folder locked against administrator deletion");
            }
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PrivilegeNotHeldException)
        {
            _logger.LogWarning(exception, "Could not lock installation folder");
        }
    }

    /// <summary>
    /// Remove the Deny rule from the installation folder so the installer can proceed.
    /// </summary>
    public void UnlockInstallFolder()
    {
        try
        {
            var installRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "ScreenTimeGuardian");

            if (!Directory.Exists(installRoot))
            {
                return;
            }

            var directoryInfo = new DirectoryInfo(installRoot);
            var security = directoryInfo.GetAccessControl();

            var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            var denyRule = new FileSystemAccessRule(
                administrators,
                FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Deny);

            security.RemoveAccessRule(denyRule);
            directoryInfo.SetAccessControl(security);
            _logger.LogInformation("Install folder unlocked for maintenance");
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PrivilegeNotHeldException)
        {
            _logger.LogWarning(exception, "Could not unlock installation folder");
        }
    }
}
