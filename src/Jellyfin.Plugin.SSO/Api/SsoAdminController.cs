using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.SSO.Abstractions;
using Jellyfin.Plugin.SSO.Accounts;
using Jellyfin.Plugin.SSO.Configuration;
using Jellyfin.Plugin.SSO.Oidc;
using Jellyfin.Plugin.SSO.Web;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SSO.Api;

/// <summary>
/// Admin configuration and operations endpoints.
/// </summary>
[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("sso/admin")]
public sealed class SsoAdminController : ControllerBase
{
    private readonly IPluginConfigAccessor _config;
    private readonly IProviderConfigurationValidator _validator;
    private readonly IOidcProviderFactory _providerFactory;
    private readonly ILinkStore _linkStore;
    private readonly IPendingStore _pendingStore;
    private readonly IIdentityResolver _identityResolver;
    private readonly IUserManager _userManager;
    private readonly IServerApplicationHost _applicationHost;
    private readonly IAuditLog _auditLog;
    private readonly ILogger<SsoAdminController> _logger;
    private static readonly object ConfigWriteLock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="SsoAdminController"/> class.
    /// </summary>
    public SsoAdminController(
        IPluginConfigAccessor config,
        IProviderConfigurationValidator validator,
        IOidcProviderFactory providerFactory,
        ILinkStore linkStore,
        IPendingStore pendingStore,
        IIdentityResolver identityResolver,
        IUserManager userManager,
        IServerApplicationHost applicationHost,
        IAuditLog auditLog,
        ILogger<SsoAdminController> logger)
    {
        _config = config;
        _validator = validator;
        _providerFactory = providerFactory;
        _linkStore = linkStore;
        _pendingStore = pendingStore;
        _identityResolver = identityResolver;
        _userManager = userManager;
        _applicationHost = applicationHost;
        _auditLog = auditLog;
        _logger = logger;
    }

    /// <summary>
    /// Gets plugin and setup status.
    /// </summary>
    [HttpGet("status")]
    public async Task<ActionResult<AdminStatusDto>> Status(CancellationToken cancellationToken)
    {
        var config = _config.Current;
        var links = await _linkStore.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var pending = await _pendingStore.GetAllAsync(cancellationToken).ConfigureAwait(false);

        return new AdminStatusDto
        {
            PluginVersion = typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "unknown",
            JellyfinVersion = _applicationHost.ApplicationVersionString,
            PublicBaseUrl = SsoUrlBuilder.GetPublicBaseUrl(Request, config),
            InjectionEnabled = config.LoginPage.InjectLoginPage,
            EnabledProviders = config.Providers.Count(x => x.Enabled),
            LinkCount = links.Count,
            PendingCount = pending.Count,
            ApiVersion = 1,
            SsoOnlyEnabled = config.SsoOnly.Enabled
        };
    }

    /// <summary>
    /// Gets provider configurations with secrets masked.
    /// </summary>
    [HttpGet("providers")]
    public ActionResult<IReadOnlyList<AdminProviderDto>> GetProviders()
    {
        return _config.Current.Providers
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(AdminProviderDto.FromConfig)
            .ToList();
    }

    /// <summary>
    /// Adds a provider.
    /// </summary>
    [HttpPost("providers")]
    public ActionResult<AdminProviderDto> AddProvider([FromBody] AdminProviderDto request)
    {
        if (request is null)
        {
            return BadRequest(Error("invalid_request", "Provider configuration is required."));
        }

        if (!_validator.Validate(request, out var validationError))
        {
            return BadRequest(Error("invalid_provider", validationError!));
        }

        lock (ConfigWriteLock)
        {
            var config = _config.Current;
            if (config.Providers.Any(x => string.Equals(x.Slug, request.Slug, StringComparison.OrdinalIgnoreCase)))
            {
                return Conflict(Error("provider_exists", "A provider with that slug already exists."));
            }

            var provider = new OidcProviderConfig { Slug = request.Slug.Trim().ToLowerInvariant() };
            request.Slug = provider.Slug;
            request.ApplyTo(provider);
            config.Providers.Add(provider);
            _config.Save();
        }

        _logger.LogInformation("SSO provider {Provider} added by administrator.", request.Slug);
        return AdminProviderDto.FromConfig(_config.Current.Providers.First(x => x.Slug == request.Slug));
    }

    /// <summary>
    /// Updates a provider. The slug cannot be changed because links reference it.
    /// </summary>
    [HttpPut("providers/{slug}")]
    public ActionResult<AdminProviderDto> UpdateProvider(string slug, [FromBody] AdminProviderDto request)
    {
        if (request is null)
        {
            return BadRequest(Error("invalid_request", "Provider configuration is required."));
        }

        var normalizedSlug = slug.Trim().ToLowerInvariant();
        request.Slug = normalizedSlug;
        if (!_validator.Validate(request, out var validationError))
        {
            return BadRequest(Error("invalid_provider", validationError!));
        }

        lock (ConfigWriteLock)
        {
            var config = _config.Current;
            var provider = config.Providers.FirstOrDefault(x =>
                string.Equals(x.Slug, normalizedSlug, StringComparison.OrdinalIgnoreCase));
            if (provider is null)
            {
                return NotFound(Error("provider_not_found", "Provider not found."));
            }

            request.ApplyTo(provider);
            _config.Save();
        }

        _logger.LogInformation("SSO provider {Provider} updated by administrator.", slug);
        return AdminProviderDto.FromConfig(_config.Current.Providers.First(x =>
            string.Equals(x.Slug, slug, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Deletes a provider. Existing links are left in place for manual cleanup.
    /// </summary>
    [HttpDelete("providers/{slug}")]
    public ActionResult DeleteProvider(string slug)
    {
        lock (ConfigWriteLock)
        {
            var config = _config.Current;
            var provider = config.Providers.FirstOrDefault(x =>
                string.Equals(x.Slug, slug, StringComparison.OrdinalIgnoreCase));
            if (provider is null)
            {
                return NotFound(Error("provider_not_found", "Provider not found."));
            }

            config.Providers.Remove(provider);
            _config.Save();
        }

        _logger.LogWarning("SSO provider {Provider} deleted. Existing links remain in the link store.", slug);
        return NoContent();
    }

    /// <summary>
    /// Tests provider discovery.
    /// </summary>
    [HttpPost("providers/{slug}/test")]
    public async Task<ActionResult> TestProvider(string slug, CancellationToken cancellationToken)
    {
        var provider = _config.Current.Providers.FirstOrDefault(x =>
            string.Equals(x.Slug, slug, StringComparison.OrdinalIgnoreCase));
        if (provider is null)
        {
            return NotFound(Error("provider_not_found", "Provider not found."));
        }

        try
        {
            var client = _providerFactory.Get(provider);
            var discovery = await client.GetDiscoveryAsync(true, cancellationToken).ConfigureAwait(false);
            return Ok(new
            {
                success = true,
                issuer = discovery.Issuer,
                authorizationEndpoint = discovery.AuthorizationEndpoint,
                tokenEndpoint = discovery.TokenEndpoint,
                userInfoEndpoint = discovery.UserInfoEndpoint,
                jwksUri = discovery.JwksUri
            });
        }
        catch (OidcException ex)
        {
            return BadRequest(Error("discovery_failed", ex.Message));
        }
        catch (HttpRequestException ex)
        {
            return BadRequest(Error("discovery_failed", ex.Message));
        }
    }

    /// <summary>
    /// Gets the callback URL for a provider.
    /// </summary>
    [HttpGet("providers/{slug}/redirect-uri")]
    public ActionResult RedirectUri(string slug)
    {
        var provider = _config.Current.Providers.FirstOrDefault(x =>
            string.Equals(x.Slug, slug, StringComparison.OrdinalIgnoreCase));
        if (provider is null)
        {
            return NotFound(Error("provider_not_found", "Provider not found."));
        }

        return Ok(new { redirectUri = SsoUrlBuilder.GetCallbackUrl(Request, _config.Current, provider) });
    }

    /// <summary>
    /// Gets global settings.
    /// </summary>
    [HttpGet("settings")]
    public ActionResult<AdminSettingsDto> GetSettings()
    {
        var config = _config.Current;
        return new AdminSettingsDto
        {
            PublicBaseUrl = config.PublicBaseUrl,
            RequireHttps = config.RequireHttps,
            StateLifetimeMinutes = config.StateLifetimeMinutes,
            TicketLifetimeSeconds = config.TicketLifetimeSeconds,
            Provisioning = config.Provisioning,
            Roles = config.Roles,
            LoginPage = config.LoginPage,
            SsoOnly = ToAdminSsoOnly(config.SsoOnly)
        };
    }

    /// <summary>
    /// Updates global settings.
    /// </summary>
    [HttpPut("settings")]
    public ActionResult<AdminSettingsDto> UpdateSettings([FromBody] AdminSettingsDto request)
    {
        if (request is null)
        {
            return BadRequest(Error("invalid_request", "Settings are required."));
        }

        if (!string.IsNullOrWhiteSpace(request.PublicBaseUrl)
            && !Uri.TryCreate(request.PublicBaseUrl, UriKind.Absolute, out _))
        {
            return BadRequest(Error("invalid_settings", "Public base URL must be an absolute URL."));
        }

        SsoOnlySettings? ssoOnly = null;
        if (request.SsoOnly is not null
            && !TryBuildSsoOnlySettings(request.SsoOnly, out ssoOnly, out var ssoOnlyError))
        {
            return BadRequest(Error("invalid_sso_only", ssoOnlyError!));
        }

        lock (ConfigWriteLock)
        {
            var config = _config.Current;
            config.PublicBaseUrl = request.PublicBaseUrl.Trim();
            config.RequireHttps = request.RequireHttps;
            config.StateLifetimeMinutes = Math.Clamp(request.StateLifetimeMinutes, 2, 30);
            config.TicketLifetimeSeconds = Math.Clamp(request.TicketLifetimeSeconds, 30, 300);
            config.Provisioning = request.Provisioning ?? new ProvisioningSettings();
            config.Roles = request.Roles ?? new RoleMappingSettings();
            config.LoginPage = request.LoginPage ?? new LoginPageSettings();
            if (ssoOnly is not null)
            {
                config.SsoOnly = ssoOnly;
            }

            _config.Save();
        }

        return GetSettings();
    }

    private AdminSsoOnlyDto ToAdminSsoOnly(SsoOnlySettings settings)
    {
        var names = settings.PasswordLoginAllowedUserIds
            .Select(id => _userManager.GetUserById(id)?.Username)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new AdminSsoOnlyDto
        {
            Enabled = settings.Enabled,
            BlockProvisionedUsers = settings.BlockProvisionedUsers,
            BlockLinkedUsers = settings.BlockLinkedUsers,
            AllowApiKeyPasswordVerification = settings.AllowApiKeyPasswordVerification,
            HideLocalLogin = settings.HideLocalLogin,
            ShowLocalAccountLink = settings.ShowLocalAccountLink,
            PasswordLoginAllowedUserIds = settings.PasswordLoginAllowedUserIds.ToList(),
            PasswordLoginAllowedUsers = names
        };
    }

    private bool TryBuildSsoOnlySettings(AdminSsoOnlyDto request, out SsoOnlySettings? settings, out string? error)
    {
        settings = null;
        error = null;

        var ids = new List<Guid>();
        foreach (var name in request.PasswordLoginAllowedUsers ?? [])
        {
            var normalized = name?.Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            var user = _userManager.GetUserByName(normalized);
            if (user is null)
            {
                error = $"Jellyfin user '{normalized}' was not found.";
                return false;
            }

            ids.Add(user.Id);
        }

        if (ids.Count == 0 && request.PasswordLoginAllowedUserIds is { Count: > 0 })
        {
            ids.AddRange(request.PasswordLoginAllowedUserIds);
        }

        ids = ids.Distinct().ToList();

        if (request.Enabled)
        {
            var hasAdmin = ids
                .Select(id => _userManager.GetUserById(id))
                .Any(user => user is not null && user.HasPermission(PermissionKind.IsAdministrator));
            if (!hasAdmin)
            {
                error = "Add at least one existing Jellyfin administrator to the password-login allowlist before enabling SSO-only login.";
                return false;
            }
        }

        settings = new SsoOnlySettings
        {
            Enabled = request.Enabled,
            BlockProvisionedUsers = request.BlockProvisionedUsers,
            BlockLinkedUsers = request.BlockLinkedUsers,
            AllowApiKeyPasswordVerification = request.AllowApiKeyPasswordVerification,
            HideLocalLogin = request.HideLocalLogin,
            ShowLocalAccountLink = request.ShowLocalAccountLink,
            PasswordLoginAllowedUserIds = ids
        };
        return true;
    }

    /// <summary>
    /// Gets links, optionally filtered by user, provider, issuer, or subject.
    /// </summary>
    [HttpGet("links")]
    public async Task<ActionResult> GetLinks(
        [FromQuery] Guid? userId,
        [FromQuery] string? provider,
        [FromQuery] string? issuer,
        [FromQuery] string? subject,
        CancellationToken cancellationToken)
    {
        var links = await _linkStore.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var filtered = links.AsEnumerable();

        if (userId.HasValue)
        {
            filtered = filtered.Where(x => x.JellyfinUserId == userId.Value);
        }

        if (!string.IsNullOrWhiteSpace(provider))
        {
            filtered = filtered.Where(x => string.Equals(x.ProviderId, provider.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(issuer))
        {
            filtered = filtered.Where(x => string.Equals(x.Issuer, issuer.Trim(), StringComparison.Ordinal));
        }

        if (!string.IsNullOrWhiteSpace(subject))
        {
            filtered = filtered.Where(x => string.Equals(x.Subject, subject.Trim(), StringComparison.Ordinal));
        }

        return Ok(filtered.Select(x => new
        {
            x.ProviderId,
            x.Issuer,
            x.Subject,
            x.JellyfinUserId,
            x.Username,
            x.Email,
            x.DisplayName,
            x.CreatedAt,
            x.LastLoginAt,
            x.Source,
            x.Provisioned
        }));
    }

    /// <summary>
    /// Removes one link.
    /// </summary>
    [HttpDelete("links/{provider}/{userId:guid}")]
    public async Task<ActionResult> DeleteLink(string provider, Guid userId, CancellationToken cancellationToken)
    {
        var removed = await _identityResolver.UnlinkAsync(userId, provider, cancellationToken).ConfigureAwait(false);
        return removed ? NoContent() : NotFound();
    }

    /// <summary>
    /// Creates a link for a trusted integration.
    /// </summary>
    [HttpPost("links")]
    public async Task<ActionResult> CreateLink([FromBody] CreateLinkRequest request, CancellationToken cancellationToken)
    {
        if (request is null
            || string.IsNullOrWhiteSpace(request.Provider)
            || string.IsNullOrWhiteSpace(request.Issuer)
            || string.IsNullOrWhiteSpace(request.Subject)
            || request.UserId == Guid.Empty)
        {
            return BadRequest(Error("invalid_request", "Provider, issuer, subject, and userId are required."));
        }

        var providerSlug = request.Provider.Trim();
        var provider = _config.Current.Providers.FirstOrDefault(x =>
            string.Equals(x.Slug, providerSlug, StringComparison.OrdinalIgnoreCase));
        if (provider is null)
        {
            return NotFound(Error("provider_not_found", "Provider not found."));
        }

        var user = _userManager.GetUserById(request.UserId);
        if (user is null)
        {
            return NotFound(Error("user_not_found", "Jellyfin user not found."));
        }

        if (user.HasPermission(PermissionKind.IsDisabled))
        {
            return Conflict(Error("user_disabled", "The Jellyfin user is disabled."));
        }

        string discoveredIssuer;
        try
        {
            var discovery = await _providerFactory.Get(provider)
                .GetDiscoveryAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            discoveredIssuer = discovery.Issuer;
        }
        catch (OidcException ex)
        {
            return BadRequest(Error("issuer_unavailable", ex.Message));
        }
        catch (HttpRequestException ex)
        {
            return BadRequest(Error("issuer_unavailable", ex.Message));
        }

        var issuer = request.Issuer.Trim();
        var subject = request.Subject.Trim();
        if (!string.Equals(issuer, discoveredIssuer, StringComparison.Ordinal))
        {
            return BadRequest(Error("issuer_mismatch", "The issuer does not match the provider discovery document."));
        }

        var existing = await _linkStore.FindAsync(provider.Slug, issuer, subject, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return existing.JellyfinUserId == user.Id
                ? Ok(ToLinkResponse(existing))
                : Conflict(Error("identity_already_linked", "This identity is linked to another Jellyfin user."));
        }

        var existingForProvider = await _linkStore.GetForUserAsync(user.Id, cancellationToken)
            .ConfigureAwait(false);
        if (existingForProvider.Any(x => string.Equals(x.ProviderId, provider.Slug, StringComparison.OrdinalIgnoreCase)))
        {
            return Conflict(Error("user_already_linked", "This Jellyfin user is already linked to this provider."));
        }

        var identity = new OidcIdentity
        {
            Issuer = issuer,
            Subject = subject,
            Username = string.IsNullOrWhiteSpace(request.Username) ? user.Username : request.Username.Trim(),
            Email = request.Email?.Trim() ?? string.Empty,
            DisplayName = request.DisplayName?.Trim() ?? string.Empty
        };

        var result = await _identityResolver.LinkAsync(
            identity,
            provider,
            user.Id,
            source: "integration",
            actor: "api-key",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (result.Status != IdentityResolutionStatus.Success)
        {
            var conflict = string.Equals(result.ErrorCode, "identity_already_linked", StringComparison.Ordinal)
                || string.Equals(result.ErrorCode, "provider_already_linked", StringComparison.Ordinal);
            return conflict
                ? Conflict(Error(result.ErrorCode ?? "link_conflict", result.Error ?? "Unable to create the link."))
                : BadRequest(Error(result.ErrorCode ?? "link_failed", result.Error ?? "Unable to create the link."));
        }

        var created = await _linkStore.FindAsync(provider.Slug, issuer, subject, cancellationToken)
            .ConfigureAwait(false);
        return created is null
            ? StatusCode(StatusCodes.Status201Created)
            : StatusCode(StatusCodes.Status201Created, ToLinkResponse(created));
    }

    /// <summary>
    /// Evaluates password-login policy by username.
    /// </summary>
    [HttpGet("password-policy")]
    public Task<ActionResult<PasswordPolicyDto>> GetPasswordPolicy(
        [FromQuery] string? username,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return Task.FromResult<ActionResult<PasswordPolicyDto>>(
                BadRequest(Error("invalid_request", "A username is required.")));
        }

        var user = _userManager.GetUserByName(username.Trim());
        return BuildPasswordPolicyAsync(user, cancellationToken);
    }

    /// <summary>
    /// Evaluates password-login policy by Jellyfin user ID.
    /// </summary>
    [HttpGet("users/{userId:guid}/password-policy")]
    public Task<ActionResult<PasswordPolicyDto>> GetUserPasswordPolicy(Guid userId, CancellationToken cancellationToken)
    {
        var user = _userManager.GetUserById(userId);
        return BuildPasswordPolicyAsync(user, cancellationToken);
    }

    private async Task<ActionResult<PasswordPolicyDto>> BuildPasswordPolicyAsync(
        Jellyfin.Database.Implementations.Entities.User? user,
        CancellationToken cancellationToken)
    {
        if (user is null)
        {
            return NotFound(Error("user_not_found", "Jellyfin user not found."));
        }

        var config = _config.Current;
        var links = await _linkStore.GetForUserAsync(user.Id, cancellationToken).ConfigureAwait(false);
        var decision = SsoOnlyPolicy.Evaluate(user.Id, links, config.SsoOnly);

        return new PasswordPolicyDto
        {
            UserId = user.Id,
            Username = user.Username,
            PasswordAllowed = decision.Allowed,
            Reason = decision.Reason,
            SsoOnlyEnabled = config.SsoOnly.Enabled,
            Provisioned = decision.Provisioned,
            Linked = decision.Linked
        };
    }

    private static object ToLinkResponse(ExternalLinkRecord link)
    {
        return new
        {
            link.ProviderId,
            link.Issuer,
            link.Subject,
            link.JellyfinUserId,
            link.Username,
            link.Email,
            link.DisplayName,
            link.CreatedAt,
            link.LastLoginAt,
            link.Source,
            link.Provisioned
        };
    }

    /// <summary>
    /// Gets pending requests.
    /// </summary>
    [HttpGet("pending")]
    public async Task<ActionResult> GetPending(CancellationToken cancellationToken)
    {
        var pending = await _pendingStore.GetAllAsync(cancellationToken).ConfigureAwait(false);
        return Ok(pending);
    }

    /// <summary>
    /// Approves a pending request.
    /// </summary>
    [HttpPost("pending/{id:guid}/approve")]
    public async Task<ActionResult> ApprovePending(
        Guid id,
        [FromBody] ApprovePendingRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _identityResolver.ApprovePendingAsync(
            id,
            request?.UserId,
            request?.CreateUser ?? false,
            cancellationToken).ConfigureAwait(false);

        if (result.Status == IdentityResolutionStatus.Success)
        {
            return Ok(new { success = true, userId = result.User?.Id });
        }

        return BadRequest(Error(result.ErrorCode ?? "approval_failed", result.Error ?? "Unable to approve the request."));
    }

    /// <summary>
    /// Denies a pending request.
    /// </summary>
    [HttpPost("pending/{id:guid}/deny")]
    public async Task<ActionResult> DenyPending(Guid id, CancellationToken cancellationToken)
    {
        var removed = await _identityResolver.DenyPendingAsync(id, cancellationToken).ConfigureAwait(false);
        return removed ? NoContent() : NotFound();
    }

    /// <summary>
    /// Searches Jellyfin users for the link picker.
    /// </summary>
    [HttpGet("users")]
    public ActionResult<IReadOnlyList<AdminUserDto>> GetUsers([FromQuery] string? search)
    {
        var users = _userManager.GetUsers().AsEnumerable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            users = users.Where(x => x.Username.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        return users
            .OrderBy(x => x.Username, StringComparer.OrdinalIgnoreCase)
            .Take(100)
            .Select(x => new AdminUserDto { Id = x.Id, Name = x.Username })
            .ToList();
    }

    /// <summary>
    /// Gets recent audit events.
    /// </summary>
    [HttpGet("audit")]
    public async Task<ActionResult> GetAudit([FromQuery] int limit, CancellationToken cancellationToken)
    {
        var events = await _auditLog.ReadRecentAsync(Math.Clamp(limit <= 0 ? 100 : limit, 1, 500), cancellationToken)
            .ConfigureAwait(false);
        return Ok(events);
    }

    private static ApiError Error(string code, string message) => new() { Code = code, Message = message };
}
