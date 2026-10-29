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
            PendingCount = pending.Count
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
            LoginPage = config.LoginPage
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
            _config.Save();
        }

        return GetSettings();
    }

    /// <summary>
    /// Gets all links.
    /// </summary>
    [HttpGet("links")]
    public async Task<ActionResult> GetLinks(CancellationToken cancellationToken)
    {
        var links = await _linkStore.GetAllAsync(cancellationToken).ConfigureAwait(false);
        return Ok(links.Select(x => new
        {
            x.ProviderId,
            x.JellyfinUserId,
            x.Username,
            x.Email,
            x.DisplayName,
            x.CreatedAt,
            x.LastLoginAt,
            x.Source
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
