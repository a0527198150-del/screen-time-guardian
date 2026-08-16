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
                return JsonSerializer.Deserialize<ConfigurationDocument>(json, JsonOptions)
                    ?? ConfigurationDocument.Default;
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

            var temporaryPath = _path + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(configuration, JsonOptions));
            File.Move(temporaryPath, _path, true);
        }
    }
}
