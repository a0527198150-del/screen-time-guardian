using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScreenTimeGuardian.Contracts;

public static class ConfigPaths
{
    public static string RootDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ScreenTimeGuardian");

    public static string ConfigurationFile => Path.Combine(RootDirectory, "config.json");

    /// <summary>Service owned state. Only SYSTEM and Administrators may write here.</summary>
    public static string RuntimeDirectory => Path.Combine(RootDirectory, "runtime");

    /// <summary>Written on service start, deleted on clean stop. Its presence at start means the last run ended badly.</summary>
    public static string BootMarkerFile => Path.Combine(RuntimeDirectory, "boot.marker");

    /// <summary>Set automatically when the safety envelope trips. Enforcement stays off until cleared.</summary>
    public static string SafeModeFlagFile => Path.Combine(RuntimeDirectory, "safemode.flag");

    /// <summary>Manual panic switch. An administrator can create this file to disable all enforcement instantly.</summary>
    public static string ManualKillSwitchFile => Path.Combine(RootDirectory, "SAFEMODE");

    /// <summary>Written by the service after password verification. When present and not expired, maintenance lock is open.</summary>
    public static string UnlockFile => Path.Combine(RuntimeDirectory, "unlock.json");
}

public sealed class ConfigurationStore
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;
    private readonly object _sync = new();

    public ConfigurationStore(string? path = null)
    {
        _path = string.IsNullOrWhiteSpace(path) ? ConfigPaths.ConfigurationFile : path;
    }

    public ConfigurationDocument Load()
    {
        lock (_sync)
        {
            if (!File.Exists(_path))
            {
                return LoadBackupOrDefault();
            }

            try
            {
                var json = File.ReadAllText(_path);
                var configuration = JsonSerializer.Deserialize<ConfigurationDocument>(json, JsonOptions)
                    ?? ConfigurationDocument.Default;
                return ConfigurationMigrator.Migrate(configuration);
            }
            catch (JsonException)
            {
                return LoadBackupOrDefault();
            }
            catch (IOException)
            {
                return LoadBackupOrDefault();
            }
        }
    }

    private ConfigurationDocument LoadBackupOrDefault()
    {
        var backupPath = _path + ".bak";
        if (!File.Exists(backupPath))
        {
            return ConfigurationDocument.Default;
        }

        try
        {
            var backupJson = File.ReadAllText(backupPath);
            var backup = JsonSerializer.Deserialize<ConfigurationDocument>(backupJson, JsonOptions);
            return backup is null ? ConfigurationDocument.Default : ConfigurationMigrator.Migrate(backup);
        }
        catch (JsonException)
        {
            return ConfigurationDocument.Default;
        }
        catch (IOException)
        {
            return ConfigurationDocument.Default;
        }
    }

    public void Save(ConfigurationDocument configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        lock (_sync)
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var migrated = ConfigurationMigrator.Migrate(configuration);
            var temporaryPath = _path + ".tmp";
            if (File.Exists(_path))
            {
                File.Copy(_path, _path + ".bak", true);
            }

            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(migrated, JsonOptions));
            File.Move(temporaryPath, _path, true);
        }
    }

    /// <summary>Deep copy through JSON. Used so a response can never mutate the live document.</summary>
    public static ConfigurationDocument Clone(ConfigurationDocument source)
    {
        var json = JsonSerializer.Serialize(source, JsonOptions);
        return JsonSerializer.Deserialize<ConfigurationDocument>(json, JsonOptions) ?? ConfigurationDocument.Default;
    }
}
