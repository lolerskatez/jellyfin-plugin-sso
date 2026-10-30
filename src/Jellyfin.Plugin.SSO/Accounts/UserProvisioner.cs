using System.Security.Cryptography;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.SSO.Abstractions;
using Jellyfin.Plugin.SSO.Configuration;
using Jellyfin.Plugin.SSO.Oidc;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Jellyfin.Plugin.SSO.Accounts;

/// <summary>
/// Creates Jellyfin users for external identities.
/// </summary>
public sealed class UserProvisioner : IUserProvisioner
{
    private readonly IUserManager _userManager;
    private readonly IPolicyMapper _policyMapper;
    private readonly ILinkStore _linkStore;
    private readonly IAuditLog _auditLog;
    private readonly ILogger<UserProvisioner> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserProvisioner"/> class.
    /// </summary>
    public UserProvisioner(
        IUserManager userManager,
        IPolicyMapper policyMapper,
        ILinkStore linkStore,
        IAuditLog auditLog,
        ILogger<UserProvisioner> logger)
    {
        _userManager = userManager;
        _policyMapper = policyMapper;
        _linkStore = linkStore;
        _auditLog = auditLog;
        _logger = logger;
    }

    /// <summary>
    /// Creates a Jellyfin user, applies policy, sets an unguessable password, and links the identity.
    /// </summary>
    public async Task<User> CreateAsync(
        OidcIdentity identity,
        OidcProviderConfig provider,
        string username,
        RoleEvaluation roles,
        string source,
        CancellationToken cancellationToken = default)
    {
        var user = await _userManager.CreateUserAsync(username).ConfigureAwait(false);

        try
        {
            await _policyMapper.ApplyNewUserPolicyAsync(user, provider, roles, cancellationToken).ConfigureAwait(false);

            // Jellyfin permits blank-password users to authenticate with an empty password.
            // SSO-only users therefore get a random password that is never disclosed.
            var randomPassword = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
            await _userManager.ChangePassword(user.Id, randomPassword).ConfigureAwait(false);

            await _linkStore.AddAsync(
                new ExternalLinkRecord
                {
                    ProviderId = provider.Slug,
                    Issuer = identity.Issuer,
                    Subject = identity.Subject,
                    JellyfinUserId = user.Id,
                    Email = NullIfEmpty(identity.Email),
                    Username = username,
                    DisplayName = NullIfEmpty(identity.DisplayName),
                    Source = source,
                    Provisioned = true
                },
                cancellationToken).ConfigureAwait(false);

            await _auditLog.WriteAsync(
                new AuditEvent
                {
                    Type = "UserProvisioned",
                    ProviderId = provider.Slug,
                    UserId = user.Id,
                    Username = username,
                    SubjectHash = AuditLog.Fingerprint(identity.Issuer + "|" + identity.Subject),
                    Outcome = "success",
                    Detail = "Created Jellyfin user from SSO identity."
                },
                cancellationToken).ConfigureAwait(false);

            return user;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to finish provisioning Jellyfin user {Username}. The user may exist without an SSO link.", username);
            throw;
        }
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
