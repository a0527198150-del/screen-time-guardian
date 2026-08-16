namespace ScreenTimeGuardian.Contracts;

public static class ConfigurationMigrator
{
    public const int CurrentSchemaVersion = 2;

    public static ConfigurationDocument Migrate(ConfigurationDocument configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (configuration.SchemaVersion < 1)
        {
            configuration.SchemaVersion = 1;
        }

        if (configuration.SchemaVersion < 2)
        {
            configuration.BlockPortableBrowsersDuringAnySchedule = true;
            configuration.StrictPortableApplicationMode = false;
            configuration.SchemaVersion = 2;
        }

        return configuration;
    }
}
