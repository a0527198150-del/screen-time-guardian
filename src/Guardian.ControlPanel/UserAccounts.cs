using System.IO;
using System.Security.Principal;
using Microsoft.Win32;

namespace ScreenTimeGuardian.ControlPanel;

public sealed class LocalUser
{
    public string Sid { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public bool IsCurrentUser { get; init; }

    public override string ToString() => IsCurrentUser ? $"{DisplayName}  (המשתמש הנוכחי)" : DisplayName;
}

/// <summary>
/// Lists the Windows accounts on this machine, so a rule can be scoped to one person.
/// Reads ProfileList rather than requiring an extra package or administrator rights.
/// </summary>
public static class UserAccounts
{
    private const string ProfileListPath =
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList";

    public static string CurrentUserSid
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.User?.Value ?? string.Empty;
        }
    }

    public static IReadOnlyList<LocalUser> Discover()
    {
        var currentSid = CurrentUserSid;
        var users = new List<LocalUser>();

        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var profileList = baseKey.OpenSubKey(ProfileListPath);
        if (profileList is null)
        {
            return users;
        }

        foreach (var sid in profileList.GetSubKeyNames())
        {
            // Skip the built in service accounts (S-1-5-18/19/20).
            if (!sid.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string displayName;
            try
            {
                var account = (NTAccount)new SecurityIdentifier(sid).Translate(typeof(NTAccount));
                displayName = account.Value;
            }
            catch (Exception exception) when (exception is IdentityNotMappedException or SystemException)
            {
                using var profileKey = profileList.OpenSubKey(sid);
                var imagePath = profileKey?.GetValue("ProfileImagePath") as string ?? sid;
                displayName = Path.GetFileName(imagePath.TrimEnd('\\'));
            }

            users.Add(new LocalUser
            {
                Sid = sid,
                DisplayName = displayName,
                IsCurrentUser = string.Equals(sid, currentSid, StringComparison.OrdinalIgnoreCase)
            });
        }

        return users
            .OrderByDescending(user => user.IsCurrentUser)
            .ThenBy(user => user.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
