using System.Diagnostics;
using System.IO;
using System.Security.Principal;

namespace ScreenTimeGuardian.ControlPanel;

/// <summary>
/// Detects and performs installation of the Screen Time Guardian service from
/// within the control panel EXE. This enables a single-EXE distribution:
/// the user right-clicks the EXE, chooses "Run as administrator", and the
/// installer bootstraps everything automatically.
/// </summary>
public static class Installer
{
    private const string ServiceName = "ScreenTimeGuardian";

    public static bool IsServiceInstalled()
    {
        return System.ServiceProcess.ServiceController.GetServices()
            .Any(s => string.Equals(s.ServiceName, ServiceName, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsServiceRunning()
    {
        try
        {
            using var controller = new System.ServiceProcess.ServiceController(ServiceName);
            return controller.Status == System.ServiceProcess.ServiceControllerStatus.Running;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Returns the directory where this EXE is located.
    /// For self-contained single-file publishes, this is the actual EXE path,
    /// not the temp extraction directory.
    /// </summary>
    public static string GetExeDirectory()
    {
        var exePath = Environment.ProcessPath ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
        return Path.GetDirectoryName(exePath) ?? throw new InvalidOperationException("Cannot determine EXE directory.");
    }

    /// <summary>
    /// Installs the service, agent, and sets up data directory permissions.
    /// Must be run as administrator.
    /// </summary>
    public static (bool Success, string Message) Install(string serviceExePath)
    {
        if (!IsAdministrator())
        {
            return (false, "נדרשות הרשאות מנהל להתקנה. לחץ לחיצה ימנית על הקובץ ובחר 'הפעל כמנהל'.");
        }

        var dataDirectory = @"C:\ProgramData\ScreenTimeGuardian";
        var runtimeDirectory = Path.Combine(dataDirectory, "runtime");

        try
        {
            // Create directories
            Directory.CreateDirectory(dataDirectory);
            Directory.CreateDirectory(runtimeDirectory);

            // Set ACL on data directory
            SetDataDirectoryAcl(dataDirectory, allowUsersRead: true);
            SetDataDirectoryAcl(runtimeDirectory, allowUsersRead: false);

            // Remove existing service if present
            var existing = System.ServiceProcess.ServiceController.GetServices()
                .FirstOrDefault(s => string.Equals(s.ServiceName, ServiceName, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                if (existing.Status != System.ServiceProcess.ServiceControllerStatus.Stopped)
                {
                    using var controller = new System.ServiceProcess.ServiceController(ServiceName);
                    controller.Stop();
                    controller.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Stopped,
                        TimeSpan.FromSeconds(30));
                }

                RunProcess("sc.exe", $"delete {ServiceName}");
                Thread.Sleep(2000);
            }

            // Install the service using sc.exe (works without New-Service cmdlet)
            var result = RunProcess("sc.exe",
                $"create {ServiceName} binPath= \"{serviceExePath}\" start= delayed-auto DisplayName= \"Screen Time Guardian\"");

            if (!result.Success)
            {
                return (false, $"התקנת השירות נכשלה: {result.Output}");
            }

            // Disable auto-restart on failure (prevents reboot loops)
            RunProcess("sc.exe", $"failure {ServiceName} reset= 0 actions= ''");

            // Register the agent for auto-start (look for Agent subfolder)
            var agentDir = Path.Combine(GetExeDirectory(), "Agent");
            var agentExe = Path.Combine(agentDir, "ScreenTimeGuardian.Agent.exe");
            if (File.Exists(agentExe))
            {
                RegisterAgent(agentExe);
            }

            // Create Start Menu and Desktop shortcuts
            CreateShortcuts(serviceExePath);

            return (true, "ההתקנה הושלמה בהצלחה. נוצרו קיצורי דרך בתפריט ההתחל ובשולחן העבודה.");
        }
        catch (Exception ex)
        {
            return (false, $"שגיאה בהתקנה: {ex.Message}");
        }
    }

    public static void StartService()
    {
        try
        {
            using var controller = new System.ServiceProcess.ServiceController(ServiceName);
            controller.Start();
            controller.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Running,
                TimeSpan.FromSeconds(30));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"לא ניתן להפעיל את השירות: {ex.Message}", ex);
        }
    }

    private static void SetDataDirectoryAcl(string path, bool allowUsersRead)
    {
        var directoryInfo = new DirectoryInfo(path);
        var security = directoryInfo.GetAccessControl();

        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        // Remove all existing rules
        foreach (var rule in security.GetAccessRules(true, false, typeof(SecurityIdentifier)).Cast<System.Security.AccessControl.FileSystemAccessRule>().ToList())
        {
            security.RemoveAccessRuleSpecific(rule);
        }

        const System.Security.AccessControl.InheritanceFlags inheritance =
            System.Security.AccessControl.InheritanceFlags.ContainerInherit |
            System.Security.AccessControl.InheritanceFlags.ObjectInherit;

        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);

        // Set owner to Administrators
        security.SetOwner(administrators);

        // Grant full control to SYSTEM and Administrators
        security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
            system, System.Security.AccessControl.FileSystemRights.FullControl,
            inheritance, System.Security.AccessControl.PropagationFlags.None,
            System.Security.AccessControl.AccessControlType.Allow));

        security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
            administrators, System.Security.AccessControl.FileSystemRights.FullControl,
            inheritance, System.Security.AccessControl.PropagationFlags.None,
            System.Security.AccessControl.AccessControlType.Allow));

        if (allowUsersRead)
        {
            var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
            security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                users, System.Security.AccessControl.FileSystemRights.ReadAndExecute,
                inheritance, System.Security.AccessControl.PropagationFlags.None,
                System.Security.AccessControl.AccessControlType.Allow));
        }

        directoryInfo.SetAccessControl(security);
    }

    private static void RegisterAgent(string agentExePath)
    {
        try
        {
            // Add to HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
                Microsoft.Win32.RegistryKeyPermissionCheck.ReadWriteSubTree,
                System.Security.AccessControl.RegistryRights.SetValue);

            key?.SetValue("ScreenTimeGuardianAgent", $"\"{agentExePath}\"");
        }
        catch
        {
            // Non-critical: agent registration may fail if registry is locked
        }
    }

    private static void CreateShortcuts(string targetPath)
    {
        var exeDir = GetExeDirectory();
        var iconPath = Path.Combine(exeDir, "icon.ico");
        var hasIcon = File.Exists(iconPath);
        var iconArg = hasIcon ? $", {iconPath}" : string.Empty;

        // Start Menu shortcut
        var startMenuDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
            "Screen Time Guardian");
        Directory.CreateDirectory(startMenuDir);
        var startMenuShortcut = Path.Combine(startMenuDir, "שומר זמן מסך.lnk");
        CreateShortcut(startMenuShortcut, targetPath, "שומר זמן מסך", iconPath);

        // Desktop shortcut
        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        var desktopShortcut = Path.Combine(desktopPath, "שומר זמן מסך.lnk");
        CreateShortcut(desktopShortcut, targetPath, "שומר זמן מסך", iconPath);
    }

    private static void CreateShortcut(string shortcutPath, string targetPath, string description, string? iconPath)
    {
        // Use PowerShell's WScript.Shell COM object via a temporary script
        var iconArg = !string.IsNullOrEmpty(iconPath) && File.Exists(iconPath)
            ? $"\n$sc.IconLocation = '{iconPath}'"
            : string.Empty;
        var workDir = Path.GetDirectoryName(targetPath) ?? string.Empty;
        var script = $"""
$sc = (New-Object -ComObject WScript.Shell).CreateShortcut('{shortcutPath}')
$sc.TargetPath = '{targetPath}'
$sc.WorkingDirectory = '{workDir}'
$sc.Description = '{description}'{iconArg}
$sc.Save()
""";
        var psFile = Path.Combine(Path.GetTempPath(), "stg_shortcut.ps1");
        File.WriteAllText(psFile, script, System.Text.Encoding.UTF8);
        try
        {
            RunProcess("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -File \"{psFile}\"");
        }
        finally
        {
            try { File.Delete(psFile); } catch { /* non-critical */ }
        }
    }

    private static (bool Success, string Output) RunProcess(string fileName, string arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process is null) return (false, "Failed to start process.");

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit(TimeSpan.FromSeconds(30));

            var success = process.ExitCode == 0;
            return (success, success ? output : $"{output}\n{error}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
