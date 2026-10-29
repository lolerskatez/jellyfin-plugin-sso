namespace Jellyfin.Plugin.SSO.Configuration;

/// <summary>
/// Provides access to the live plugin configuration.
/// </summary>
public interface IPluginConfigAccessor
{
    /// <summary>
    /// Gets the current configuration.
    /// </summary>
    PluginConfiguration Current { get; }

    /// <summary>
    /// Persists the current configuration.
    /// </summary>
    void Save();
}

/// <summary>
/// Default configuration accessor backed by the plugin singleton.
/// </summary>
public sealed class PluginConfigAccessor : IPluginConfigAccessor
{
    /// <inheritdoc />
    public PluginConfiguration Current => Plugin.Instance?.Configuration
        ?? throw new InvalidOperationException("SSO plugin is not initialized.");

    /// <inheritdoc />
    public void Save() => Plugin.Instance?.SaveConfiguration();
}
