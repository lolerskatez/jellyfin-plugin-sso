namespace Jellyfin.Plugin.SSO.Api;

/// <summary>
/// Public provider summary.
/// </summary>
public sealed class ProviderSummaryDto
{
    /// <summary>
    /// Gets or sets the provider slug.
    /// </summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the login button text.
    /// </summary>
    public string ButtonText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional icon URL.
    /// </summary>
    public string IconUrl { get; set; } = string.Empty;
}

/// <summary>
/// Public provider list.
/// </summary>
public sealed class ProvidersResponse
{
    /// <summary>
    /// Gets or sets the providers.
    /// </summary>
    public List<ProviderSummaryDto> Providers { get; set; } = [];

    /// <summary>
    /// Gets or sets the maximum number of inline buttons.
    /// </summary>
    public int MaxInlineButtons { get; set; } = 3;

    /// <summary>
    /// Gets or sets a value indicating whether provider buttons should appear on the login page.
    /// </summary>
    public bool ShowProviderButtons { get; set; } = true;
}

/// <summary>
/// Ticket exchange request from the browser.
/// </summary>
public sealed class CompleteLoginRequest
{
    /// <summary>
    /// Gets or sets the one-time ticket.
    /// </summary>
    public string Ticket { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the device ID.
    /// </summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the device name.
    /// </summary>
    public string DeviceName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the app name.
    /// </summary>
    public string AppName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the app version.
    /// </summary>
    public string AppVersion { get; set; } = string.Empty;
}

/// <summary>
/// Minimal user information returned to the browser.
/// </summary>
public sealed class SsoUserDto
{
    /// <summary>
    /// Gets or sets the user ID.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the username.
    /// </summary>
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Ticket exchange response.
/// </summary>
public sealed class CompleteLoginResponse
{
    /// <summary>
    /// Gets or sets the Jellyfin access token.
    /// </summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Jellyfin server ID.
    /// </summary>
    public string ServerId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the server name.
    /// </summary>
    public string ServerName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the user.
    /// </summary>
    public SsoUserDto User { get; set; } = new();

    /// <summary>
    /// Gets or sets the validated return URL.
    /// </summary>
    public string ReturnUrl { get; set; } = "/web/index.html#/home";
}

/// <summary>
/// A linked identity summary.
/// </summary>
public sealed class LinkSummaryDto
{
    /// <summary>
    /// Gets or sets the provider slug.
    /// </summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the last known email.
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Gets or sets the last known username.
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// Gets or sets the last login time.
    /// </summary>
    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>
    /// Gets or sets the link source.
    /// </summary>
    public string Source { get; set; } = string.Empty;
}

/// <summary>
/// Link start request.
/// </summary>
public sealed class LinkStartRequest
{
    /// <summary>
    /// Gets or sets the provider slug.
    /// </summary>
    public string Provider { get; set; } = string.Empty;
}

/// <summary>
/// Link start response.
/// </summary>
public sealed class LinkStartResponse
{
    /// <summary>
    /// Gets or sets the authorization URL.
    /// </summary>
    public string Url { get; set; } = string.Empty;
}

/// <summary>
/// Quick Connect authorization request.
/// </summary>
public sealed class QuickConnectAuthorizeRequest
{
    /// <summary>
    /// Gets or sets the code shown by the client.
    /// </summary>
    public string Code { get; set; } = string.Empty;
}

/// <summary>
/// Safe API error.
/// </summary>
public sealed class ApiError
{
    /// <summary>
    /// Gets or sets the error code.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the safe message.
    /// </summary>
    public string Message { get; set; } = string.Empty;
}
