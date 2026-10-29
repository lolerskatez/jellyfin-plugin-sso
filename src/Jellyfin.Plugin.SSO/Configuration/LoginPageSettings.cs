namespace Jellyfin.Plugin.SSO.Configuration;

/// <summary>
/// Login page presentation settings.
/// </summary>
public class LoginPageSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether the plugin injects its login script into the web client.
    /// </summary>
    public bool InjectLoginPage { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of provider buttons shown inline before using a chooser.
    /// </summary>
    public int MaxInlineButtons { get; set; } = 3;

    /// <summary>
    /// Gets or sets a value indicating whether provider buttons are shown.
    /// </summary>
    public bool ShowProviderButtons { get; set; } = true;
}
