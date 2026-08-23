using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ScreenTimeGuardian.Contracts;

namespace ScreenTimeGuardian.Service;

public sealed class UpdateCoordinator : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
    private readonly ConfigurationStore _store;
    private readonly ILogger<UpdateCoordinator> _logger;
    private readonly HttpClient _httpClient = new(new HttpClientHandler
    {
        // A signed HTTPS URL must not silently redirect to HTTP or another host.
        AllowAutoRedirect = false
    })
    {
        Timeout = RequestTimeout
    };

    public UpdateCoordinator(ConfigurationStore store, ILogger<UpdateCoordinator> logger)
    {
        _store = store;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Signed update check failed; the installed version was left unchanged");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task CheckOnceAsync(CancellationToken cancellationToken)
    {
        var configuration = _store.Load();
        if (!configuration.AutomaticUpdatesEnabled
            || !ConfigurationValidation.IsValidHttpsUrl(configuration.UpdateManifestUrl)
            || !ConfigurationValidation.IsValidRsaPublicKeyPem(configuration.UpdatePublicKeyPem))
        {
            return;
        }

        using var response = await _httpClient.GetAsync(configuration.UpdateManifestUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        var manifest = await response.Content.ReadFromJsonAsync<UpdateManifest>(
            new JsonSerializerOptions(ConfigurationStore.JsonOptions), cancellationToken);

        if (manifest is null
            || string.IsNullOrWhiteSpace(manifest.Version)
            || !ConfigurationValidation.IsValidHttpsUrl(manifest.PackageUrl)
            || !UpdateSecurity.IsSha256(manifest.Sha256)
            || string.IsNullOrWhiteSpace(manifest.Signature)
            || !SameHttpsHost(configuration.UpdateManifestUrl, manifest.PackageUrl))
        {
            _logger.LogWarning("Ignoring malformed or cross-host update manifest");
            return;
        }

        if (!UpdateSecurity.VerifySignature(
                manifest.Version,
                manifest.PackageUrl,
                manifest.Sha256,
                manifest.Signature,
                configuration.UpdatePublicKeyPem))
        {
            _logger.LogWarning("Ignoring update with an invalid publisher signature");
            return;
        }

        if (!Version.TryParse(manifest.Version.TrimStart('v', 'V'), out var candidateVersion)
            || !Version.TryParse(ServiceStatusHolder.Version, out var installedVersion)
            || candidateVersion <= installedVersion)
        {
            return;
        }

        var updaterPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "Updater",
            "ScreenTimeGuardian.Updater.exe"));
        if (!File.Exists(updaterPath))
        {
            _logger.LogWarning("Signed update is valid but the updater is missing at {Path}", updaterPath);
            return;
        }

        var packagePath = Path.Combine(Path.GetTempPath(), "ScreenTimeGuardian-update-" + Guid.NewGuid().ToString("N") + ".zip");
        var temporaryUpdaterDirectory = Path.Combine(Path.GetTempPath(), "ScreenTimeGuardian-updater-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var packageResponse = await _httpClient.GetAsync(
                manifest.PackageUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            packageResponse.EnsureSuccessStatusCode();
            await using (var input = await packageResponse.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = File.Create(packagePath))
            {
                await input.CopyToAsync(output, cancellationToken);
            }

            // The updater must not load from the installation tree it will replace.
            // Copy its complete publish directory so its DLL, deps and runtimeconfig
            // remain available after the service directory is stopped and cleared.
            CopyDirectory(Path.GetDirectoryName(updaterPath)!, temporaryUpdaterDirectory);
            var temporaryUpdaterPath = Path.Combine(
                temporaryUpdaterDirectory,
                Path.GetFileName(updaterPath));

            var startInfo = new ProcessStartInfo
            {
                FileName = temporaryUpdaterPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = temporaryUpdaterDirectory
            };
            foreach (var argument in new[]
            {
                "--package", packagePath,
                "--install-dir", Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..")),
                "--service", "ScreenTimeGuardian",
                "--sha256", manifest.Sha256,
                "--signature", manifest.Signature,
                "--public-key", configuration.UpdatePublicKeyPem,
                "--version", manifest.Version,
                "--package-url", manifest.PackageUrl
            })
            {
                startInfo.ArgumentList.Add(argument);
            }

            var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Could not start the updater.");
            _logger.LogInformation("Started signed update to version {Version}", manifest.Version);
            _ = CleanupAfterExitAsync(process, packagePath, temporaryUpdaterDirectory);
        }
        catch
        {
            TryDelete(packagePath);
            TryDeleteDirectory(temporaryUpdaterDirectory);
            throw;
        }
    }

    private static async Task CleanupAfterExitAsync(
        Process process,
        string packagePath,
        string temporaryUpdaterDirectory)
    {
        try
        {
            await process.WaitForExitAsync();
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            process.Dispose();
            TryDelete(packagePath);
            TryDeleteDirectory(temporaryUpdaterDirectory);
        }
    }

    private static bool SameHttpsHost(string manifestUrl, string packageUrl)
    {
        if (!Uri.TryCreate(manifestUrl, UriKind.Absolute, out var manifestUri)
            || !Uri.TryCreate(packageUrl, UriKind.Absolute, out var packageUri))
        {
            return false;
        }

        return manifestUri.Scheme == Uri.UriSchemeHttps
            && packageUri.Scheme == Uri.UriSchemeHttps
            && string.Equals(manifestUri.Host, packageUri.Host, StringComparison.OrdinalIgnoreCase)
            && manifestUri.Port == packageUri.Port;
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

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }
}
