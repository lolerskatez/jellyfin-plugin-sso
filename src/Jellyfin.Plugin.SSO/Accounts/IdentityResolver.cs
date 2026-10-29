using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.SSO.Abstractions;
using Jellyfin.Plugin.SSO.Configuration;
using Jellyfin.Plugin.SSO.Oidc;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SSO.Accounts;

/// <summary>
/// Result of resolving an external identity.
/// </summary>
public enum IdentityResolutionStatus
{
    /// <summary>
    /// A Jellyfin user is available.
    /// </summary>
    Success,

    /// <summary>
    /// Administrator action is required.
    /// </summary>
    Pending,

    /// <summary>
    /// The login is denied.
    /// </summary>
    Denied
}

/// <summary>
/// Result of an identity resolution.
/// </summary>
public sealed class IdentityResolution
{
    /// <summary>
    /// Gets or sets the status.
    /// </summary>
    public IdentityResolutionStatus Status { get; set; }

    /// <summary>
    /// Gets or sets the resolved user.
    /// </summary>
    public User? User { get; set; }

    /// <summary>
    /// Gets or sets the pending request.
    /// </summary>
    public PendingLoginRecord? Pending { get; set; }

    /// <summary>
    /// Gets or sets a stable error code.
    /// </summary>
    public string? ErrorCode { get; set; }

    /// <summary>
    /// Gets or sets a safe user-facing error message.
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Gets or sets whether the identity is still allowed to access Jellyfin.
    /// </summary>
    public bool HasAccess { get; set; } = true;
}

/// <summary>
/// Resolves OIDC identities to Jellyfin users and manages links.
/// </summary>
public sealed class IdentityResolver : IIdentityResolver
{
    private readonly IUserManager _userManager;
    private readonly IPluginConfigAccessor _config;
    private readonly ILinkStore _linkStore;
    private readonly IPendingStore _pendingStore;
    private readonly IUsernameResolver _usernameResolver;
    private readonly IUserProvisioner _userProvisioner;
    private readonly IPolicyMapper _policyMapper;
    private readonly IAuditLog _auditLog;
    private readonly ILogger<IdentityResolver> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="IdentityResolver"/> class.
    /// </summary>
    public IdentityResolver(
        IUserManager userManager,
        IPluginConfigAccessor config,
        ILinkStore linkStore,
        IPendingStore pendingStore,
        IUsernameResolver usernameResolver,
        IUserProvisioner userProvisioner,
        IPolicyMapper policyMapper,
        IAuditLog auditLog,
        ILogger<IdentityResolver> logger)
    {
        _userManager = userManager;
        _config = config;
        _linkStore = linkStore;
        _pendingStore = pendingStore;
        _usernameResolver = usernameResolver;
        _userProvisioner = userProvisioner;
        _policyMapper = policyMapper;
        _auditLog = auditLog;
        _logger = logger;
    }

    /// <summary>
    /// Resolves a login identity.
    /// </summary>
    public async Task<IdentityResolution> ResolveLoginAsync(
        OidcIdentity identity,
        OidcProviderConfig provider,
        string remoteIp,
        CancellationToken cancellationToken = default)
    {
        var config = _config.Current;

        if (!ValidateEmail(identity, provider, out var emailError))
        {
            await AuditAsync("LoginDenied", provider, identity, null, "email-policy", emailError, cancellationToken)
                .ConfigureAwait(false);
            return Denied("email_not_allowed", emailError ?? "This email address is not allowed.");
        }

        var roles = _policyMapper.Evaluate(identity, provider);
        if (!roles.HasAccess)
        {
            await AuditAsync("LoginDenied", provider, identity, null, "role-required", "Required role missing", cancellationToken)
                .ConfigureAwait(false);
            return Denied("role_required", "Your account is not a member of a required group.");
        }

        var link = await _linkStore.FindAsync(provider.Slug, identity.Issuer, identity.Subject, cancellationToken)
            .ConfigureAwait(false);

        if (link is not null)
        {
            var linkedUser = _userManager.GetUserById(link.JellyfinUserId);
            if (linkedUser is null)
            {
                await AuditAsync("LoginDenied", provider, identity, null, "orphaned-link", "Linked Jellyfin user no longer exists", cancellationToken)
                    .ConfigureAwait(false);
                return Denied("orphaned_link", "This SSO account is linked to a Jellyfin user that no longer exists. Ask an administrator to repair the link.");
            }

            if (linkedUser.HasPermission(PermissionKind.IsDisabled))
            {
                await AuditAsync("LoginDenied", provider, identity, linkedUser.Id, "disabled", "User is disabled", cancellationToken)
                    .ConfigureAwait(false);
                return Denied("account_disabled", "This Jellyfin account is disabled.");
            }

            if (_policyMapper.ShouldSyncRoles(provider)
                && (identity.Groups.Count > 0
                    || !string.Equals(config.Provisioning.MissingRoleBehavior, "Keep", StringComparison.OrdinalIgnoreCase)))
            {
                await _policyMapper.ApplySyncPolicyAsync(linkedUser, provider, roles, cancellationToken).ConfigureAwait(false);
            }

            await _linkStore.TouchAsync(link, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
            return new IdentityResolution { Status = IdentityResolutionStatus.Success, User = linkedUser, HasAccess = true };
        }

        var username = _usernameResolver.Resolve(identity, provider);
        var existing = _usernameResolver.FindExisting(username);

        var autoProvision = provider.AutoProvision ?? config.Provisioning.AutoProvisionNewUsers;
        var requireApproval = provider.RequireApproval ?? config.Provisioning.RequireAdminApproval;
        var collisionPolicy = string.IsNullOrWhiteSpace(provider.UsernameConflictPolicy)
            ? config.Provisioning.UsernameConflictPolicy
            : provider.UsernameConflictPolicy;

        if (existing is not null && !string.Equals(collisionPolicy, "Suffix", StringComparison.OrdinalIgnoreCase))
        {
            return await CreatePendingAsync(
                identity,
                provider,
                "username-collision",
                username,
                remoteIp,
                "A Jellyfin account with this name already exists. An administrator must link it.",
                cancellationToken).ConfigureAwait(false);
        }

        if (!autoProvision || requireApproval)
        {
            return await CreatePendingAsync(
                identity,
                provider,
                !autoProvision ? "auto-provision-disabled" : "approval-required",
                username,
                remoteIp,
                "This SSO account is not linked to Jellyfin. An administrator must approve it.",
                cancellationToken).ConfigureAwait(false);
        }

        if (existing is not null)
        {
            username = _usernameResolver.MakeUnique(username);
        }

        try
        {
            var user = await _userProvisioner.CreateAsync(
                identity,
                provider,
                username,
                roles,
                "auto",
                cancellationToken).ConfigureAwait(false);

            await _auditLog.WriteAsync(
                new AuditEvent
                {
                    Type = "LoginSucceeded",
                    ProviderId = provider.Slug,
                    UserId = user.Id,
                    Username = user.Username,
                    SubjectHash = AuditLog.Fingerprint(identity.Issuer + "|" + identity.Subject),
                    IpHash = AuditLog.Fingerprint(remoteIp),
                    Outcome = "success",
                    Detail = "User auto-provisioned."
                },
                cancellationToken).ConfigureAwait(false);

            return new IdentityResolution { Status = IdentityResolutionStatus.Success, User = user, HasAccess = true };
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Username collision while provisioning {Username}", username);
            return await CreatePendingAsync(
                identity,
                provider,
                "username-collision",
                username,
                remoteIp,
                "The requested username is no longer available. An administrator must review this request.",
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Links an identity to an authenticated Jellyfin user.
    /// </summary>
    public async Task<IdentityResolution> LinkAsync(
        OidcIdentity identity,
        OidcProviderConfig provider,
        Guid jellyfinUserId,
        CancellationToken cancellationToken = default)
    {
        var existing = await _linkStore.FindAsync(provider.Slug, identity.Issuer, identity.Subject, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            if (existing.JellyfinUserId == jellyfinUserId)
            {
                return new IdentityResolution { Status = IdentityResolutionStatus.Success, User = _userManager.GetUserById(jellyfinUserId) };
            }

            return Denied("identity_already_linked", "This SSO account is already linked to another Jellyfin user.");
        }

        var user = _userManager.GetUserById(jellyfinUserId);
        if (user is null)
        {
            return Denied("user_not_found", "The Jellyfin user no longer exists.");
        }

        var existingForProvider = await _linkStore.GetForUserAsync(jellyfinUserId, cancellationToken).ConfigureAwait(false);
        if (existingForProvider.Any(x => string.Equals(x.ProviderId, provider.Slug, StringComparison.OrdinalIgnoreCase)))
        {
            return Denied("provider_already_linked", "This Jellyfin user is already linked to this provider.");
        }

        await _linkStore.AddAsync(
            new ExternalLinkRecord
            {
                ProviderId = provider.Slug,
                Issuer = identity.Issuer,
                Subject = identity.Subject,
                JellyfinUserId = jellyfinUserId,
                Email = NullIfEmpty(identity.Email),
                Username = user.Username,
                DisplayName = NullIfEmpty(identity.DisplayName),
                Source = "self"
            },
            cancellationToken).ConfigureAwait(false);

        await AuditAsync("LinkCreated", provider, identity, jellyfinUserId, "success", "Linked by user", cancellationToken)
            .ConfigureAwait(false);

        return new IdentityResolution { Status = IdentityResolutionStatus.Success, User = user };
    }

    /// <summary>
    /// Removes a provider link from a user.
    /// </summary>
    public async Task<bool> UnlinkAsync(
        Guid jellyfinUserId,
        string providerId,
        CancellationToken cancellationToken = default)
    {
        var removed = await _linkStore.RemoveAsync(providerId, jellyfinUserId, cancellationToken).ConfigureAwait(false);
        if (removed)
        {
            await _auditLog.WriteAsync(
                new AuditEvent
                {
                    Type = "LinkRemoved",
                    ProviderId = providerId,
                    UserId = jellyfinUserId,
                    Outcome = "success",
                    Detail = "Unlinked by user or administrator."
                },
                cancellationToken).ConfigureAwait(false);
        }

        return removed;
    }

    /// <summary>
    /// Approves a pending request by linking to an existing user or creating a new one.
    /// </summary>
    public async Task<IdentityResolution> ApprovePendingAsync(
        Guid pendingId,
        Guid? jellyfinUserId,
        bool createUser,
        CancellationToken cancellationToken = default)
    {
        var pending = await _pendingStore.GetAsync(pendingId, cancellationToken).ConfigureAwait(false);
        if (pending is null)
        {
            return Denied("pending_not_found", "This request has expired or was already handled.");
        }

        var provider = _config.Current.Providers.FirstOrDefault(x =>
            string.Equals(x.Slug, pending.ProviderId, StringComparison.OrdinalIgnoreCase));
        if (provider is null)
        {
            return Denied("provider_not_found", "The provider no longer exists.");
        }

        var identity = PendingToIdentity(pending);
        var roles = _policyMapper.Evaluate(identity, provider);

        if (jellyfinUserId is not null)
        {
            var result = await LinkAsync(identity, provider, jellyfinUserId.Value, cancellationToken).ConfigureAwait(false);
            if (result.Status == IdentityResolutionStatus.Success)
            {
                await _pendingStore.ApproveAsync(pendingId, cancellationToken).ConfigureAwait(false);
            }

            return result;
        }

        if (!createUser)
        {
            return Denied("invalid_request", "Choose an existing user or request user creation.");
        }

        var username = string.IsNullOrWhiteSpace(pending.Username)
            ? _usernameResolver.Resolve(identity, provider)
            : UsernameResolver.Sanitize(pending.Username);
        if (_usernameResolver.FindExisting(username) is not null)
        {
            username = _usernameResolver.MakeUnique(username);
        }

        var user = await _userProvisioner.CreateAsync(
            identity,
            provider,
            username,
            roles,
            "admin",
            cancellationToken).ConfigureAwait(false);

        await _pendingStore.ApproveAsync(pendingId, cancellationToken).ConfigureAwait(false);
        await AuditAsync("PendingApproved", provider, identity, user.Id, "success", "Administrator created user", cancellationToken)
            .ConfigureAwait(false);

        return new IdentityResolution { Status = IdentityResolutionStatus.Success, User = user };
    }

    /// <summary>
    /// Denies a pending request.
    /// </summary>
    public async Task<bool> DenyPendingAsync(Guid pendingId, CancellationToken cancellationToken = default)
    {
        var result = await _pendingStore.DenyAsync(pendingId, cancellationToken).ConfigureAwait(false);
        if (result)
        {
            await _auditLog.WriteAsync(
                new AuditEvent { Type = "PendingDenied", Outcome = "success", Detail = "Administrator denied pending request." },
                cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    private async Task<IdentityResolution> CreatePendingAsync(
        OidcIdentity identity,
        OidcProviderConfig provider,
        string reason,
        string username,
        string remoteIp,
        string message,
        CancellationToken cancellationToken)
    {
        var request = await _pendingStore.AddOrGetAsync(
            new PendingLoginRecord
            {
                ProviderId = provider.Slug,
                Issuer = identity.Issuer,
                Subject = identity.Subject,
                Username = username,
                DisplayName = NullIfEmpty(identity.DisplayName),
                Email = NullIfEmpty(identity.Email),
                Groups = identity.Groups.ToList(),
                Reason = reason,
                IpHash = AuditLog.Fingerprint(remoteIp)
            },
            cancellationToken).ConfigureAwait(false);

        await AuditAsync("PendingCreated", provider, identity, null, reason, message, cancellationToken).ConfigureAwait(false);

        return new IdentityResolution
        {
            Status = IdentityResolutionStatus.Pending,
            Pending = request,
            ErrorCode = reason,
            Error = message
        };
    }

    private bool ValidateEmail(OidcIdentity identity, OidcProviderConfig provider, out string? error)
    {
        error = null;

        if (provider.RequireVerifiedEmail && !identity.EmailVerified)
        {
            error = "The identity provider did not verify this email address.";
            return false;
        }

        if (provider.AllowedEmailDomains.Count == 0)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(identity.Email))
        {
            error = "This provider requires an email address.";
            return false;
        }

        var at = identity.Email.LastIndexOf('@');
        if (at < 0 || at == identity.Email.Length - 1)
        {
            error = "The email address is invalid.";
            return false;
        }

        var domain = identity.Email[(at + 1)..];
        if (!provider.AllowedEmailDomains.Any(x => string.Equals(x.TrimStart('@'), domain, StringComparison.OrdinalIgnoreCase)))
        {
            error = "This email domain is not allowed.";
            return false;
        }

        return true;
    }

    private static OidcIdentity PendingToIdentity(PendingLoginRecord pending)
    {
        return new OidcIdentity
        {
            Issuer = pending.Issuer,
            Subject = pending.Subject,
            Username = pending.Username ?? string.Empty,
            DisplayName = pending.DisplayName ?? string.Empty,
            Email = pending.Email ?? string.Empty,
            Groups = pending.Groups
        };
    }

    private Task AuditAsync(
        string type,
        OidcProviderConfig provider,
        OidcIdentity identity,
        Guid? userId,
        string outcome,
        string? detail,
        CancellationToken cancellationToken)
    {
        return _auditLog.WriteAsync(
            new AuditEvent
            {
                Type = type,
                ProviderId = provider.Slug,
                UserId = userId,
                Username = identity.Username,
                SubjectHash = AuditLog.Fingerprint(identity.Issuer + "|" + identity.Subject),
                Outcome = outcome,
                Detail = detail
            },
            cancellationToken);
    }

    private static IdentityResolution Denied(string code, string message)
    {
        return new IdentityResolution
        {
            Status = IdentityResolutionStatus.Denied,
            ErrorCode = code,
            Error = message,
            HasAccess = false
        };
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
