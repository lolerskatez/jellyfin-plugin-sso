namespace Jellyfin.Plugin.SSO.Configuration;

/// <summary>
/// One versioned plugin configuration migration.
/// </summary>
public interface IPluginConfigurationMigration
{
    /// <summary>
    /// Gets the version this migration upgrades from.
    /// </summary>
    int FromVersion { get; }

    /// <summary>
    /// Applies the migration in place.
    /// </summary>
    void Apply(PluginConfiguration configuration);
}

/// <summary>
/// Runs versioned configuration migrations and normalizes defaults.
/// </summary>
public static class PluginConfigurationMigrator
{
    /// <summary>
    /// Current configuration schema version.
    /// </summary>
    public const int CurrentVersion = 2;

    private static readonly IReadOnlyList<IPluginConfigurationMigration> Migrations =
    [
        new SchemaV0ToV1Migration(),
        new SchemaV1ToV2Migration()
    ];

    /// <summary>
    /// Migrates and normalizes a configuration. Returns true when the caller should save it.
    /// </summary>
    public static bool Migrate(PluginConfiguration configuration)
    {
        var changed = false;

        while (configuration.SchemaVersion < CurrentVersion)
        {
            var migration = Migrations.FirstOrDefault(x => x.FromVersion == configuration.SchemaVersion);
            if (migration is null)
            {
                configuration.SchemaVersion = CurrentVersion;
                changed = true;
                break;
            }

            migration.Apply(configuration);
            configuration.SchemaVersion = migration.FromVersion + 1;
            changed = true;
        }

        changed |= Normalize(configuration);
        return changed;
    }

    private static bool Normalize(PluginConfiguration configuration)
    {
        var changed = false;

        if (configuration.Providers is null)
        {
            configuration.Providers = [];
            changed = true;
        }

        foreach (var provider in configuration.Providers)
        {
            if (string.IsNullOrWhiteSpace(provider.Protocol))
            {
                provider.Protocol = "oidc";
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(provider.Profile))
            {
                provider.Profile = "generic";
                changed = true;
            }

            if (provider.GroupClaimPaths is null)
            {
                provider.GroupClaimPaths = ["groups"];
                changed = true;
            }
            else if (provider.GroupClaimPaths.Count != provider.GroupClaimPaths.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            {
                provider.GroupClaimPaths = provider.GroupClaimPaths
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                changed = true;
            }

            if (provider.RequiredRoles is null)
            {
                provider.RequiredRoles = [];
                changed = true;
            }

            if (provider.AdminRoles is null)
            {
                provider.AdminRoles = [];
                changed = true;
            }

            if (provider.AllowedEmailDomains is null)
            {
                provider.AllowedEmailDomains = [];
                changed = true;
            }

            if (provider.AdditionalAuthorizationParameters is null)
            {
                provider.AdditionalAuthorizationParameters = [];
                changed = true;
            }
        }

        if (configuration.Provisioning is null)
        {
            configuration.Provisioning = new ProvisioningSettings();
            changed = true;
        }

        if (configuration.Roles is null)
        {
            configuration.Roles = new RoleMappingSettings();
            changed = true;
        }

        if (configuration.LoginPage is null)
        {
            configuration.LoginPage = new LoginPageSettings();
            changed = true;
        }

        return changed;
    }

    private sealed class SchemaV0ToV1Migration : IPluginConfigurationMigration
    {
        public int FromVersion => 0;

        public void Apply(PluginConfiguration configuration)
        {
            configuration.Providers ??= [];
            configuration.Provisioning ??= new ProvisioningSettings();
            configuration.Roles ??= new RoleMappingSettings();
            configuration.LoginPage ??= new LoginPageSettings();
        }
    }

    private sealed class SchemaV1ToV2Migration : IPluginConfigurationMigration
    {
        public int FromVersion => 1;

        public void Apply(PluginConfiguration configuration)
        {
            foreach (var provider in configuration.Providers ?? [])
            {
                if (string.IsNullOrWhiteSpace(provider.Protocol))
                {
                    provider.Protocol = "oidc";
                }

                if (string.IsNullOrWhiteSpace(provider.Profile))
                {
                    provider.Profile = "generic";
                }

                provider.AdditionalAuthorizationParameters ??= [];
            }
        }
    }
}
