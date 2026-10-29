using Jellyfin.Plugin.SSO.Abstractions;
using Jellyfin.Plugin.SSO.Accounts;
using Jellyfin.Plugin.SSO.Configuration;
using Jellyfin.Plugin.SSO.Oidc;
using Jellyfin.Plugin.SSO.Web;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.QuickConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SSO.Api;

/// <summary>
/// Authenticated account-linking and Quick Connect endpoints.
/// </summary>
[ApiController]
[Route("sso")]
public sealed class SsoAccountController : SsoControllerBase
{
    private readonly IOidcProviderFactory _providerFactory;
    private readonly AuthorizationStateStore _stateStore;
    private readonly IIdentityResolver _identityResolver;
    private readonly ILinkStore _linkStore;
    private readonly IQuickConnect _quickConnect;
    private readonly ILogger<SsoAccountController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SsoAccountController"/> class.
    /// </summary>
    public SsoAccountController(
        IPluginConfigAccessor config,
        ISsoPageRenderer pageRenderer,
        IOidcProviderFactory providerFactory,
        AuthorizationStateStore stateStore,
        IIdentityResolver identityResolver,
        ILinkStore linkStore,
        IQuickConnect quickConnect,
        ILogger<SsoAccountController> logger)
        : base(config, pageRenderer)
    {
        _providerFactory = providerFactory;
        _stateStore = stateStore;
        _identityResolver = identityResolver;
        _linkStore = linkStore;
        _quickConnect = quickConnect;
        _logger = logger;
    }

    /// <summary>
    /// Serves the account-linking page.
    /// </summary>
    [HttpGet("link")]
    [AllowAnonymous]
    public IActionResult LinkPage()
    {
        return HtmlPage("link.html");
    }

    /// <summary>
    /// Starts an authenticated account-link flow.
    /// </summary>
    [HttpPost("link/start")]
    [Authorize]
    public async Task<ActionResult<LinkStartResponse>> LinkStart(
        [FromBody] LinkStartRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized(new ApiError { Code = "not_authenticated", Message = "Sign in to Jellyfin first." });
        }

        if (request is null)
        {
            return BadRequest(new ApiError { Code = "invalid_request", Message = "A provider is required." });
        }

        var provider = FindProvider(request.Provider);
        if (provider is null)
        {
            return NotFound(new ApiError { Code = "provider_not_found", Message = "Unknown SSO provider." });
        }

        try
        {
            var config = Configuration;
            var redirectUri = SsoUrlBuilder.GetCallbackUrl(Request, config, provider);
            var state = _stateStore.Create(provider.Slug, redirectUri, "link", userId, "/sso/link");
            var cookieName = AuthorizationStateStore.GetCookieName(state.StateId);
            var stateSameSite = string.Equals(provider.ResponseMode, "form_post", StringComparison.OrdinalIgnoreCase)
                && Request.IsHttps
                    ? SameSiteMode.None
                    : SameSiteMode.Lax;
            SsoCookieWriter.Write(
                Response,
                cookieName,
                state.BrowserBinding,
                Math.Clamp(config.StateLifetimeMinutes, 2, 30) * 60,
                Request.IsHttps,
                stateSameSite);

            var client = _providerFactory.Get(provider);
            var url = await client.BuildAuthorizationUrlAsync(
                redirectUri,
                state.StateId,
                state.Nonce,
                state.CodeVerifier,
                cancellationToken).ConfigureAwait(false);

            return new LinkStartResponse { Url = url };
        }
        catch (OidcException ex)
        {
            _logger.LogWarning(ex, "Unable to start link flow for provider {Provider}", provider.Slug);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ApiError { Code = "provider_unavailable", Message = "The identity provider is unavailable." });
        }
    }

    /// <summary>
    /// Lists the current user's SSO links.
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<LinkSummaryDto>>> Me(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var links = await _linkStore.GetForUserAsync(userId.Value, cancellationToken).ConfigureAwait(false);
        return links.Select(x => new LinkSummaryDto
        {
            ProviderId = x.ProviderId,
            Email = x.Email,
            Username = x.Username,
            LastLoginAt = x.LastLoginAt,
            Source = x.Source
        }).ToList();
    }

    /// <summary>
    /// Removes one provider link from the current user.
    /// </summary>
    [HttpDelete("me/{provider}")]
    [Authorize]
    public async Task<IActionResult> Unlink(string provider, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var removed = await _identityResolver.UnlinkAsync(userId.Value, provider, cancellationToken).ConfigureAwait(false);
        return removed ? NoContent() : NotFound();
    }

    /// <summary>
    /// Serves the Quick Connect helper page.
    /// </summary>
    [HttpGet("quickconnect")]
    [AllowAnonymous]
    public IActionResult QuickConnectPage()
    {
        return HtmlPage("quickconnect.html");
    }

    /// <summary>
    /// Authorizes a Quick Connect code for the current user.
    /// </summary>
    [HttpPost("quickconnect/authorize")]
    [Authorize]
    public async Task<ActionResult> QuickConnectAuthorize(
        [FromBody] QuickConnectAuthorizeRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Code))
        {
            return BadRequest(new ApiError { Code = "missing_code", Message = "Enter the code shown on your device." });
        }

        try
        {
            var success = await _quickConnect.AuthorizeRequest(userId.Value, request.Code.Trim()).ConfigureAwait(false);
            return success
                ? Ok(new { success = true })
                : BadRequest(new ApiError { Code = "invalid_code", Message = "The code was rejected or has expired." });
        }
        catch (AuthenticationException ex)
        {
            return BadRequest(new ApiError { Code = "quickconnect_disabled", Message = ex.Message });
        }
    }
}
