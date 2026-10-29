using System.Security;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.SSO.Abstractions;
using Jellyfin.Plugin.SSO.Accounts;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SSO.Sessions;

/// <summary>
/// Device details supplied by the client when exchanging a login ticket.
/// </summary>
public sealed class SsoDeviceInfo
{
    /// <summary>
    /// Gets or sets the client device ID.
    /// </summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the device name.
    /// </summary>
    public string DeviceName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the client app name.
    /// </summary>
    public string AppName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the client app version.
    /// </summary>
    public string AppVersion { get; set; } = string.Empty;
}

/// <summary>
/// Issues normal Jellyfin sessions after an SSO login.
/// </summary>
public sealed class SessionIssuer : ISessionIssuer
{
    private readonly ISessionManager _sessionManager;
    private readonly IUserManager _userManager;
    private readonly INetworkManager _networkManager;
    private readonly IAuditLog _auditLog;
    private readonly ILogger<SessionIssuer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionIssuer"/> class.
    /// </summary>
    public SessionIssuer(
        ISessionManager sessionManager,
        IUserManager userManager,
        INetworkManager networkManager,
        IAuditLog auditLog,
        ILogger<SessionIssuer> logger)
    {
        _sessionManager = sessionManager;
        _userManager = userManager;
        _networkManager = networkManager;
        _auditLog = auditLog;
        _logger = logger;
    }

    /// <summary>
    /// Issues a normal Jellyfin device token.
    /// </summary>
    public async Task<AuthenticationResult> IssueAsync(
        User user,
        SsoDeviceInfo device,
        string remoteIp,
        string providerId,
        CancellationToken cancellationToken = default)
    {
        if (user.HasPermission(PermissionKind.IsDisabled))
        {
            throw new SecurityException("This Jellyfin account is disabled.");
        }

        if (!user.HasPermission(PermissionKind.EnableRemoteAccess)
            && !_networkManager.IsInLocalNetwork(remoteIp))
        {
            throw new SecurityException("Remote access is disabled for this Jellyfin account.");
        }

        if (!user.IsParentalScheduleAllowed())
        {
            throw new SecurityException("This Jellyfin account is not allowed to sign in at this time.");
        }

        var request = new AuthenticationRequest
        {
            UserId = user.Id,
            Username = user.Username,
            App = Normalize(device.AppName, "Jellyfin Web", 64),
            AppVersion = Normalize(device.AppVersion, "12.0.0", 32),
            DeviceId = Normalize(device.DeviceId, null, 128)
                ?? throw new AuthenticationException("A device ID is required."),
            DeviceName = Normalize(device.DeviceName, "Web Browser", 128),
            RemoteEndPoint = remoteIp
        };

        var result = await _sessionManager.AuthenticateDirect(request).ConfigureAwait(false);

        try
        {
            user.LastLoginDate = DateTime.UtcNow;
            user.LastActivityDate = DateTime.UtcNow;
            user.InvalidLoginAttemptCount = 0;
            await _userManager.UpdateUserAsync(user).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A timestamp failure must not invalidate an otherwise successful login.
            _logger.LogWarning(ex, "Unable to update login timestamps for {Username}.", user.Username);
        }

        await _auditLog.WriteAsync(
            new AuditEvent
            {
                Type = "LoginSucceeded",
                ProviderId = providerId,
                UserId = user.Id,
                Username = user.Username,
                IpHash = AuditLog.Fingerprint(remoteIp),
                Outcome = "success",
                Detail = "Jellyfin session issued."
            },
            cancellationToken).ConfigureAwait(false);

        return result;
    }

    private static string? Normalize(string? value, string? fallback, int maxLength)
    {
        var result = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        if (string.IsNullOrWhiteSpace(result))
        {
            return null;
        }

        return result.Length <= maxLength ? result : result[..maxLength];
    }
}
