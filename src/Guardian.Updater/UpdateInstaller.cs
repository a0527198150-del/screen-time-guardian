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
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length - 1; index += 2)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal))
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
    public void Apply(UpdateArguments options)
    {
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
        ZipFile.ExtractToDirectory(options.PackagePath, stagingDirectory);

        StopService(options.ServiceName);
        CopyDirectory(stagingDirectory, options.InstallDirectory);
        StartService(options.ServiceName);
        Directory.Delete(stagingDirectory, recursive: true);
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
