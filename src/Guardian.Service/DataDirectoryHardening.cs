using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using ScreenTimeGuardian.Contracts;

namespace ScreenTimeGuardian.Service;

public static class DataDirectoryHardening
{
    public static bool Apply(ILogger logger)
    {
        try
        {
            Directory.CreateDirectory(ConfigPaths.RootDirectory);
            Directory.CreateDirectory(ConfigPaths.RuntimeDirectory);
            Harden(ConfigPaths.RootDirectory, allowUsersRead: true);
            Harden(ConfigPaths.RuntimeDirectory, allowUsersRead: false);
            logger.LogInformation("Data directory ACL verified: {Path}", ConfigPaths.RootDirectory);
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PrivilegeNotHeldException)
        {
            logger.LogError(exception,
                "Could not secure the data directory. Enforcement remains disabled because standard users may be able to modify {Path}",
                ConfigPaths.RootDirectory);
            return false;
        }
    }

    private static void Harden(string path, bool allowUsersRead)
    {
        var security = new DirectoryInfo(path).GetAccessControl();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, false, typeof(SecurityIdentifier)))
        {
            security.RemoveAccessRuleSpecific(rule);
        }

        const InheritanceFlags inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);

        security.AddAccessRule(new FileSystemAccessRule(
            system, FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            administrators, FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));

        if (allowUsersRead)
        {
            var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
            security.AddAccessRule(new FileSystemAccessRule(
                users, FileSystemRights.ReadAndExecute, inheritance, PropagationFlags.None, AccessControlType.Allow));
        }

        new DirectoryInfo(path).SetAccessControl(security);
    }
}
