using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;

namespace ScreenTimeGuardian.Updater;

public sealed record UpdateArguments(
    string PackagePath,
    string InstallDirectory,
    string ServiceName,
    string ExpectedSha256)
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
            GetRequired(values, "--sha256"));
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
    // SHA-256 detects corruption but does not prove who published the package.
    // Keep updates disabled until a publisher public key is embedded and verified.
    private const bool SignedUpdatesEnabled = false;

    public void Apply(UpdateArguments options)
    {
        if (!SignedUpdatesEnabled)
        {
            throw new InvalidOperationException(
                "עדכונים אוטומטיים מושבתים: יש להטמיע ולאמת חתימת מפרסם לפני הפעלה.");
        }

        if (!File.Exists(options.PackagePath))
        {
            throw new FileNotFoundException("Update package not found.", options.PackagePath);
        }

        var actualHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(options.PackagePath)));
        if (!string.Equals(actualHash, options.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Update package hash does not match the expected value.");
        }

        var stagingDirectory = Path.Combine(Path.GetTempPath(), "ScreenTimeGuardian-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDirectory);
        try
        {
            ValidateArchiveEntries(options.PackagePath, stagingDirectory);
            ZipFile.ExtractToDirectory(options.PackagePath, stagingDirectory);

            StopService(options.ServiceName);
            CopyDirectory(stagingDirectory, options.InstallDirectory);
            StartService(options.ServiceName);
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, recursive: true);
            }
        }
    }

    private static void StopService(string serviceName)
    {
        RunSc("stop", serviceName);
    }

    private static void StartService(string serviceName)
    {
        RunSc("start", serviceName);
    }

    private static void RunSc(string action, string serviceName)
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
        if (process.ExitCode != 0 && action == "start")
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
}
