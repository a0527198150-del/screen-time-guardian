// Compile-only stand-ins for Windows-only APIs, so the service source can be
// type-checked on Linux. Signatures mirror the real ones; behaviour is irrelevant.
namespace Microsoft.Win32
{
    public enum RegistryHive { ClassesRoot, CurrentUser, LocalMachine, Users, CurrentConfig, PerformanceData }
    public enum RegistryView { Default, Registry64, Registry32 }
    public enum RegistryValueKind { String, ExpandString, Binary, DWord, MultiString, QWord, Unknown, None }

    public sealed class RegistryKey : IDisposable
    {
        public static RegistryKey OpenBaseKey(RegistryHive hive, RegistryView view) => null!;
        public RegistryKey? OpenSubKey(string name) => null;
        public RegistryKey? OpenSubKey(string name, bool writable) => null;
        public RegistryKey? CreateSubKey(string subkey) => null;
        public RegistryKey? CreateSubKey(string subkey, bool writable) => null;
        public string[] GetSubKeyNames() => Array.Empty<string>();
        public string[] GetValueNames() => Array.Empty<string>();
        public object? GetValue(string? name) => null;
        public void SetValue(string? name, object value) { }
        public void SetValue(string? name, object value, RegistryValueKind valueKind) { }
        public void DeleteSubKeyTree(string subkey, bool throwOnMissingSubKey) { }
        public void Dispose() { }
    }

    public static class Registry
    {
        public static RegistryKey LocalMachine => null!;
        public static RegistryKey CurrentUser => null!;
    }
}

namespace System.Security.Principal
{
    public enum WellKnownSidType { BuiltinUsersSid, LocalSystemSid, BuiltinAdministratorsSid }

    public abstract class IdentityReference { public abstract string Value { get; } }

    public sealed class SecurityIdentifier : IdentityReference
    {
        public SecurityIdentifier(string sddlForm) { }
        public SecurityIdentifier(WellKnownSidType type, SecurityIdentifier? domainSid) { }
        public override string Value => string.Empty;
        public IdentityReference Translate(Type targetType) => null!;
    }

    public sealed class NTAccount : IdentityReference
    {
        public NTAccount(string name) { }
        public NTAccount(string domainName, string accountName) { }
        public override string Value => string.Empty;
        public IdentityReference Translate(Type targetType) => null!;
    }

    public class IdentityNotMappedException : SystemException { }

    public class WindowsIdentity : IDisposable
    {
        public static WindowsIdentity GetCurrent() => null!;
        public SecurityIdentifier? User => null;
        public void Dispose() { }
    }

    public class WindowsPrincipal
    {
        public WindowsPrincipal(WindowsIdentity identity) { }
        public bool IsInRole(WindowsBuiltInRole role) => false;
    }

    public enum WindowsBuiltInRole { Administrator }

    public class PrivilegeNotHeldException : SystemException { }
}

namespace System.Security.AccessControl
{
    public enum AccessControlType { Allow, Deny }

    [Flags]
    public enum FileSystemRights
    {
        ReadData = 1, ReadAndExecute = 32, Write = 274, FullControl = 2032127,
        Delete = 65536, DeleteSubdirectoriesAndFiles = 256, ReadPermissions = 131072
    }

    [Flags]
    public enum InheritanceFlags { None = 0, ContainerInherit = 1, ObjectInherit = 2 }

    [Flags]
    public enum PropagationFlags { None = 0, InheritOnly = 1, NoPropagateInherit = 2 }

    public sealed class FileSystemAccessRule
    {
        public FileSystemAccessRule(IdentityReference identity, FileSystemRights rights,
            InheritanceFlags inheritance, PropagationFlags propagation, AccessControlType type) { }
        public IdentityReference IdentityReference => null!;
        public FileSystemRights FileSystemRights => default;
        public AccessControlType AccessControlType => default;
    }

    public abstract class ObjectSecurity
    {
        public void SetAccessRuleProtection(bool isProtected, bool preserveInheritance) { }
        public AuthorizationRuleCollection GetAccessRules(bool includeExplicit, bool includeInherited, Type targetType) => new();
        public void AddAccessRule(AccessRule rule) { }
        public void RemoveAccessRuleSpecific(AccessRule rule) { }
        public void SetOwner(IdentityReference identity) { }
    }

    public sealed class DirectorySecurity : ObjectSecurity { }

    public sealed class AccessRule
    {
        public AccessRule(IdentityReference identity, FileSystemRights rights, InheritanceFlags inheritance, PropagationFlags propagation, AccessControlType type) { }
    }

    public sealed class AuthorizationRuleCollection : System.Collections.ArrayList { }
}

namespace System.IO.Pipes
{
    using System.Security.AccessControl;
    using System.Security.Principal;

    [Flags]
    public enum PipeAccessRights { ReadWrite = 3, FullControl = 2032031 }

    public sealed class PipeAccessRule
    {
        public PipeAccessRule(IdentityReference identity, PipeAccessRights rights, AccessControlType type) { }
    }

    public sealed class PipeSecurity
    {
        public void AddAccessRule(PipeAccessRule rule) { }
    }

    public static class NamedPipeServerStreamAcl
    {
        public static NamedPipeServerStream Create(
            string pipeName, PipeDirection direction, int maxNumberOfServerInstances,
            PipeTransmissionMode transmissionMode, PipeOptions options,
            int inBufferSize, int outBufferSize, PipeSecurity? pipeSecurity) => null!;
    }
}

namespace System.Diagnostics
{
    public sealed class ProcessStartInfo
    {
        public string FileName { get; set; } = string.Empty;
        public string Arguments { get; set; } = string.Empty;
        public bool UseShellExecute { get; set; }
        public bool RedirectStandardOutput { get; set; }
        public bool RedirectStandardError { get; set; }
        public bool CreateNoWindow { get; set; }
    }

    public sealed class Process : IDisposable
    {
        public static Process? Start(ProcessStartInfo info) => null;
        public void WaitForExit(int milliseconds) { }
        public int ExitCode => 0;
        public StreamReader StandardError => null!;
        public void Dispose() { }
    }
}

namespace Microsoft.Extensions.Hosting
{
    public sealed class WindowsServiceLifetimeOptions { public string ServiceName { get; set; } = string.Empty; }

    public static class WindowsServiceLifetimeHostBuilderExtensions
    {
        public static Microsoft.Extensions.DependencyInjection.IServiceCollection AddWindowsService(
            this Microsoft.Extensions.DependencyInjection.IServiceCollection services,
            Action<WindowsServiceLifetimeOptions> configure) => services;
    }
}
