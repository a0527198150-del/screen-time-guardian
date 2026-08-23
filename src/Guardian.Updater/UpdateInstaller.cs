using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using ScreenTimeGuardian.Contracts;

namespace ScreenTimeGuardian.Updater;

public sealed record UpdateArguments(
    string PackagePath,
    string InstallDirectory,
    string ServiceName,
    string ExpectedSha256,
    string Signature,
    string PublicKeyPem,
    string Version,
    string PackageUrl)
{
    public static UpdateArguments Parse(string[] args)
    {
        if (args.Length == 0 || args.Length % 2 != 0)
        {
            throw new ArgumentException("Invalid updater arguments.");
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(args[index + 1]))
            {
                throw new ArgumentException("Invalid updater arguments.");
            }

            values[args[index]] = args[index + 1];
        }

        return new UpdateArguments(
            GetRequired(values, "--package"),
            GetRequired(values, "--install-dir"),
            GetRequired(values, "--service"),
            GetRequired(values, "--sha256"),
            GetRequired(values, "--signature"),
            GetRequired(values, "--public-key"),
            GetRequired(values, "--version"),
            GetRequired(values, "--package-url"));
    }

    private static string GetRequired(IReadOnlyDictionary<string, string> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Missing updater argument {key}.");
        }

        return value;
    }
}

public sealed class UpdateInstaller
{
    public void Apply(UpdateArguments options)
    {
        if (!File.Exists(options.PackagePath))
        {
            throw new FileNotFoundException("Update package not found.", options.PackagePath);
        }

        if (!UpdateSecurity.IsSha256(options.ExpectedSha256)
            || !UpdateSecurity.VerifySignature(
                options.Version,
                options.PackageUrl,
                options.ExpectedSha256,
                options.Signature,
                options.PublicKeyPem))
        {
            throw new InvalidOperationException("עדכון נדחה: חתימת המפרסם אינה תקינה.");
        }

        string actualHash;
        using (var packageStream = File.OpenRead(options.PackagePath))
        using (var sha256 = SHA256.Create())
        {
            actualHash = Convert.ToHexString(sha256.ComputeHash(packageStream));
        }
        if (!string.Equals(actualHash, options.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Update package hash does not match the expected value.");
        }

        var installDirectory = Path.GetFullPath(options.InstallDirectory);
        if (!Directory.Exists(installDirectory))
        {
            throw new DirectoryNotFoundException($"Install directory not found: {installDirectory}");
        }

        var stagingDirectory = Path.Combine(Path.GetTempPath(), "ScreenTimeGuardian-update-" + Guid.NewGuid().ToString("N"));
        var backupDirectory = Path.Combine(Path.GetTempPath(), "ScreenTimeGuardian-backup-" + Guid.NewGuid().ToString("N"));
        var updateSucceeded = false;
        var backupComplete = false;
        var rollbackSucceeded = false;
        Directory.CreateDirectory(stagingDirectory);
        Directory.CreateDirectory(backupDirectory);
        try
        {
            ValidateArchiveEntries(options.PackagePath, stagingDirectory);
            ZipFile.ExtractToDirectory(options.PackagePath, stagingDirectory);
            CopyDirectory(installDirectory, backupDirectory);
            backupComplete = true;

            StopService(options.ServiceName);
            if (!WaitForServiceState(options.ServiceName, "STOPPED", TimeSpan.FromSeconds(30)))
            {
                throw new InvalidOperationException("השירות לא נעצר בזמן; העדכון בוטל.");
            }

            ClearDirectory(installDirectory);
            CopyDirectory(stagingDirectory, installDirectory);
            StartService(options.ServiceName);
            if (!WaitForServiceState(options.ServiceName, "RUNNING", TimeSpan.FromSeconds(30)))
            {
                throw new InvalidOperationException("השירות לא חזר לפעול לאחר העדכון.");
            }

            updateSucceeded = true;
        }
        catch
        {
            try
            {
                if (!backupComplete)
                {
                    throw new InvalidOperationException("לא נוצר גיבוי מלא לפני העדכון; לא יבוצע שחזור אוטומטי.");
                }

                StopService(options.ServiceName);
                WaitForServiceState(options.ServiceName, "STOPPED", TimeSpan.FromSeconds(30));
                ClearDirectory(installDirectory);
                CopyDirectory(backupDirectory, installDirectory);
                StartService(options.ServiceName);
                rollbackSucceeded = WaitForServiceState(options.ServiceName, "RUNNING", TimeSpan.FromSeconds(30));
            }
            catch
            {
                // Preserve the original failure and keep the backup for manual recovery.
            }

            throw;
        }
        finally
        {
            TryDeleteDirectory(stagingDirectory);
            if (updateSucceeded || rollbackSucceeded)
            {
                TryDeleteDirectory(backupDirectory);
            }
        }
    }

    private static void StopService(string serviceName) => RunSc("stop", serviceName, throwOnFailure: false);

    private static void StartService(string serviceName) => RunSc("start", serviceName, throwOnFailure: true);

    private static bool WaitForServiceState(string serviceName, string expectedState, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "sc.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                ArgumentList = { "query", serviceName }
            });

            if (process is not null)
            {
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode == 0
                    && output.Contains($"{expectedState}", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            Thread.Sleep(TimeSpan.FromMilliseconds(500));
        }

        return false;
    }

    private static void RunSc(string action, string serviceName, bool throwOnFailure)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "sc.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            ArgumentList = { action, serviceName }
        }) ?? throw new InvalidOperationException("Could not start the Windows service controller.");

        process.WaitForExit();
        if (throwOnFailure && process.ExitCode != 0)
        {
            throw new InvalidOperationException(process.StandardError.ReadToEnd());
        }
    }

    private static void ValidateArchiveEntries(string packagePath, string stagingDirectory)
    {
        var root = Path.GetFullPath(stagingDirectory + Path.DirectorySeparatorChar);
        using var archive = ZipFile.OpenRead(packagePath);
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.Length > 512)
            {
                throw new InvalidOperationException("Update package contains an excessively long path.");
            }

            var target = Path.GetFullPath(Path.Combine(stagingDirectory, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Update package contains an unsafe path.");
            }
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static void ClearDirectory(string directory)
    {
        foreach (var file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
            File.Delete(file);
        }

        foreach (var child in Directory.GetDirectories(directory, "*", SearchOption.AllDirectories)
                     .OrderByDescending(path => path.Length))
        {
            Directory.Delete(child, recursive: false);
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException or UnauthorizedAccessException)
        {
            // Best effort cleanup only.
        }
    }
}
