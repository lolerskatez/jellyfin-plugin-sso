using System.Globalization;
using Jellyfin.Plugin.SSO.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.SSO;

/// <summary>
/// Main Jellyfin SSO plugin entry point.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;

        // Config migration is deliberately independent of the runtime services so
        // older configurations can be upgraded before controllers are constructed.
        if (PluginConfigurationMigrator.Migrate(Configuration))
        {
            SaveConfiguration(Configuration);
        }
    }

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("a286a3f9-cfcc-40f9-ac28-f4d98a3172ae");

    /// <inheritdoc />
    public override string Name => "Single Sign-On";

    /// <inheritdoc />
    public override string Description => "Authenticate Jellyfin users through an OpenID Connect provider.";

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = Name,
            EmbeddedResourcePath = string.Format(
                CultureInfo.InvariantCulture,
                "{0}.Configuration.configPage.html",
                GetType().Namespace)
        };
    }
}
