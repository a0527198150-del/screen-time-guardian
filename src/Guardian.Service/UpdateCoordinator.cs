using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ScreenTimeGuardian.Contracts;

namespace ScreenTimeGuardian.Service;

public sealed class UpdateCoordinator
{
    private const string CurrentVersion = "0.1.0";
    private readonly HttpClient _httpClient = new();
    private readonly ILogger<UpdateCoordinator> _logger;
    private DateTimeOffset _lastCheck = DateTimeOffset.MinValue;

    public UpdateCoordinator(ILogger<UpdateCoordinator> logger)
    {
        _logger = logger;
    }

    public async Task CheckAsync(ConfigurationDocument configuration, CancellationToken cancellationToken)
    {
        if (!configuration.AutomaticUpdatesEnabled
            || string.IsNullOrWhiteSpace(configuration.UpdateManifestUrl)
            || DateTimeOffset.UtcNow - _lastCheck < TimeSpan.FromHours(6))
        {
            return;
        }

        _lastCheck = DateTimeOffset.UtcNow;
        if (!Uri.TryCreate(configuration.UpdateManifestUrl, UriKind.Absolute, out var manifestUri)
            || manifestUri.Scheme != Uri.UriSchemeHttps)
        {
            _logger.LogWarning("Update manifest URL must use HTTPS");
            return;
        }

        var manifest = await _httpClient.GetFromJsonAsync<UpdateManifest>(manifestUri, cancellationToken);
        if (manifest is null || !Version.TryParse(manifest.Version, out var version)
            || !Version.TryParse(CurrentVersion, out var current)
            || version <= current
            || string.IsNullOrWhiteSpace(manifest.PackageUrl)
            || !Uri.TryCreate(manifest.PackageUrl, UriKind.Absolute, out var packageUri)
            || packageUri.Scheme != Uri.UriSchemeHttps)
        {
            return;
        }

        var package = await _httpClient.GetByteArrayAsync(packageUri, cancellationToken);
        var hash = Convert.ToHexString(SHA256.HashData(package));
        if (!string.Equals(hash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError("Update package hash mismatch for version {Version}", manifest.Version);
            return;
        }

        var updateDirectory = Path.Combine(ConfigPaths.RootDirectory, "updates");
        Directory.CreateDirectory(updateDirectory);
        var packagePath = Path.Combine(updateDirectory, $"{manifest.Version}.zip");
        await File.WriteAllBytesAsync(packagePath, package, cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(updateDirectory, "pending.json"),
            JsonSerializer.Serialize(manifest),
            cancellationToken);

        _logger.LogInformation("Update {Version} staged at {Path}; administrator approval is required to apply it", manifest.Version, packagePath);
    }
}
