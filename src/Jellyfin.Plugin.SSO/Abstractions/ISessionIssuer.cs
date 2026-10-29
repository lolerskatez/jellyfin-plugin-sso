using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.SSO.Sessions;
using MediaBrowser.Controller.Authentication;

namespace Jellyfin.Plugin.SSO.Abstractions;

/// <summary>
/// Issues normal Jellyfin sessions after SSO authentication.
/// </summary>
public interface ISessionIssuer
{
    Task<AuthenticationResult> IssueAsync(User user, SsoDeviceInfo device, string remoteIp, string providerId, CancellationToken cancellationToken = default);
}
