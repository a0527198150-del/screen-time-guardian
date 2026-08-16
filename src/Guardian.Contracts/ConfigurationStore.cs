using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScreenTimeGuardian.Contracts;

public static class ConfigPaths
{
    public static string RootDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ScreenTimeGuardian");

    public static string ConfigurationFile => Path.Combine(RootDirectory, "config.json");
}

public sealed class ConfigurationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;
    private readonly object _sync = new();

    public ConfigurationStore(string? path = null)
    {
        _path = string.IsNullOrWhiteSpace(path)
            ? ConfigPaths.ConfigurationFile
            : path;
    }

    public ConfigurationDocument Load()
    {
        lock (_sync)
        {
            if (!File.Exists(_path))
            {
                return ConfigurationDocument.Default;
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
                return ConfigurationDocument.Default;
            }
            catch (IOException)
            {
                return ConfigurationDocument.Default;
            }
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
}
