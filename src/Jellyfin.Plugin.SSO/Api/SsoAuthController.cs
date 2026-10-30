using System.Security;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.SSO.Abstractions;
using Jellyfin.Plugin.SSO.Accounts;
using Jellyfin.Plugin.SSO.Configuration;
using Jellyfin.Plugin.SSO.Oidc;
using Jellyfin.Plugin.SSO.Sessions;
using Jellyfin.Plugin.SSO.Web;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SSO.Api;

/// <summary>
/// Public OIDC login endpoints.
/// </summary>
[ApiController]
[Route("sso")]
public sealed class SsoAuthController : SsoControllerBase
{
    private readonly IOidcProviderFactory _providerFactory;
    private readonly AuthorizationStateStore _stateStore;
    private readonly LoginTicketStore _ticketStore;
    private readonly IIdentityResolver _identityResolver;
    private readonly ISessionIssuer _sessionIssuer;
    private readonly IUserManager _userManager;
    private readonly IServerApplicationHost _applicationHost;
    private readonly ISsoRateLimiter _rateLimiter;
    private readonly ILogger<SsoAuthController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SsoAuthController"/> class.
    /// </summary>
    public SsoAuthController(
        IPluginConfigAccessor config,
        ISsoPageRenderer pageRenderer,
        IOidcProviderFactory providerFactory,
        AuthorizationStateStore stateStore,
        LoginTicketStore ticketStore,
        IIdentityResolver identityResolver,
        ISessionIssuer sessionIssuer,
        IUserManager userManager,
        IServerApplicationHost applicationHost,
        ISsoRateLimiter rateLimiter,
        ILogger<SsoAuthController> logger)
        : base(config, pageRenderer)
    {
        _providerFactory = providerFactory;
        _stateStore = stateStore;
        _ticketStore = ticketStore;
        _identityResolver = identityResolver;
        _sessionIssuer = sessionIssuer;
        _userManager = userManager;
        _applicationHost = applicationHost;
        _rateLimiter = rateLimiter;
        _logger = logger;
    }

    /// <summary>
    /// Gets enabled providers for the login page.
    /// </summary>
    [HttpGet("providers")]
    [AllowAnonymous]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public ActionResult<ProvidersResponse> GetProviders()
    {
        var config = Configuration;
        return new ProvidersResponse
        {
            MaxInlineButtons = Math.Clamp(config.LoginPage.MaxInlineButtons, 1, 10),
            ShowProviderButtons = config.LoginPage.ShowProviderButtons,
            HideLocalLogin = config.SsoOnly.Enabled && config.SsoOnly.HideLocalLogin,
            ShowLocalAccountLink = config.SsoOnly.Enabled && config.SsoOnly.HideLocalLogin && config.SsoOnly.ShowLocalAccountLink,
            Providers = config.Providers
                .Where(x => x.Enabled && !string.IsNullOrWhiteSpace(x.Slug))
                .OrderBy(x => x.DisplayOrder)
                .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(x => new ProviderSummaryDto
                {
                    Slug = x.Slug,
                    DisplayName = x.DisplayName,
                    ButtonText = string.IsNullOrWhiteSpace(x.ButtonText)
                        ? "Sign in with " + x.DisplayName
                        : x.ButtonText,
                    IconUrl = x.IconUrl
                })
                .ToList()
        };
    }

    /// <summary>
    /// Starts a login flow.
    /// </summary>
    [HttpGet("start/{provider}")]
    [AllowAnonymous]
    public async Task<IActionResult> Start(string provider, [FromQuery] string? returnUrl, CancellationToken cancellationToken)
    {
        var providerConfig = FindProvider(provider);
        if (providerConfig is null)
        {
            return RedirectToComplete("provider_not_found");
        }

        if (!_rateLimiter.IsAllowed("sso-start:" + GetRemoteIp(), 30, TimeSpan.FromMinutes(1)))
        {
            return RedirectToComplete("rate_limited");
        }

        try
        {
            var config = Configuration;
            var redirectUri = SsoUrlBuilder.GetCallbackUrl(Request, config, providerConfig);
            var state = _stateStore.Create(
                providerConfig.Slug,
                redirectUri,
                "login",
                null,
                SsoUrlBuilder.NormalizeReturnUrl(returnUrl));

            var cookieName = AuthorizationStateStore.GetCookieName(state.StateId);
            var stateSameSite = string.Equals(providerConfig.ResponseMode, "form_post", StringComparison.OrdinalIgnoreCase)
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

            var client = _providerFactory.Get(providerConfig);
            var url = await client.BuildAuthorizationUrlAsync(
                redirectUri,
                state.StateId,
                state.Nonce,
                state.CodeVerifier,
                cancellationToken).ConfigureAwait(false);

            return Redirect(url);
        }
        catch (OidcException ex)
        {
            _logger.LogWarning(ex, "Unable to start SSO login for provider {Provider}", providerConfig.Slug);
            return RedirectToComplete("provider_unavailable");
        }
    }

    /// <summary>
    /// OIDC callback.
    /// </summary>
    [HttpGet("callback/{provider}")]
    [HttpPost("callback/{provider}")]
    [AllowAnonymous]
    public async Task<IActionResult> Callback(string provider, CancellationToken cancellationToken)
    {
        var values = await ReadCallbackValuesAsync(cancellationToken).ConfigureAwait(false);
        var providerConfig = FindProvider(provider);

        if (providerConfig is null)
        {
            return RedirectToComplete("provider_not_found");
        }

        var stateId = values.GetValueOrDefault("state") ?? string.Empty;
        var cookieName = AuthorizationStateStore.GetCookieName(stateId);
        var browserBinding = Request.Cookies[cookieName];
        var state = _stateStore.Consume(stateId, browserBinding);
        SsoCookieWriter.Delete(Response, cookieName, Request.IsHttps);

        if (state is null || !string.Equals(state.ProviderId, providerConfig.Slug, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("SSO callback rejected for provider {Provider}: invalid or expired state.", providerConfig.Slug);
            return RedirectToComplete("invalid_state");
        }

        if (!string.IsNullOrWhiteSpace(values.GetValueOrDefault("error")))
        {
            var error = values["error"];
            _logger.LogInformation("SSO provider {Provider} returned error {Error}", providerConfig.Slug, error);
            return RedirectToComplete(string.Equals(error, "access_denied", StringComparison.OrdinalIgnoreCase)
                ? "access_denied"
                : "provider_error");
        }

        var code = values.GetValueOrDefault("code");
        if (string.IsNullOrWhiteSpace(code))
        {
            return RedirectToComplete("missing_code");
        }

        try
        {
            var client = _providerFactory.Get(providerConfig);
            var token = await client.ExchangeCodeAsync(
                code,
                state.RedirectUri,
                state.CodeVerifier,
                cancellationToken).ConfigureAwait(false);

            var tokenClaims = await client.ValidateIdTokenAsync(
                token.IdToken,
                state.Nonce,
                cancellationToken).ConfigureAwait(false);

            var userInfo = await client.GetUserInfoAsync(token.AccessToken, cancellationToken).ConfigureAwait(false);
            var identity = client.ExtractIdentity(tokenClaims, userInfo);

            if (string.IsNullOrWhiteSpace(identity.Issuer) || string.IsNullOrWhiteSpace(identity.Subject))
            {
                return RedirectToComplete("invalid_identity");
            }

            if (string.Equals(state.Mode, "link", StringComparison.OrdinalIgnoreCase))
            {
                if (state.JellyfinUserId is null)
                {
                    return Redirect(LocalUrl("/sso/link?status=error&code=invalid_link_state"));
                }

                var linkResult = await _identityResolver.LinkAsync(
                    identity,
                    providerConfig,
                    state.JellyfinUserId.Value,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                return linkResult.Status == IdentityResolutionStatus.Success
                    ? Redirect(LocalUrl("/sso/link?status=linked"))
                    : Redirect(LocalUrl("/sso/link?status=error&code=" + Uri.EscapeDataString(linkResult.ErrorCode ?? "link_failed")));
            }

            var result = await _identityResolver.ResolveLoginAsync(
                identity,
                providerConfig,
                GetRemoteIp(),
                cancellationToken).ConfigureAwait(false);

            if (result.Status == IdentityResolutionStatus.Pending && result.Pending is not null)
            {
                return Redirect(LocalUrl("/sso/complete?pending=" + Uri.EscapeDataString(result.Pending.Id.ToString("D"))));
            }

            if (result.Status != IdentityResolutionStatus.Success || result.User is null)
            {
                return RedirectToComplete(result.ErrorCode ?? "login_denied");
            }

            var (ticket, binding) = _ticketStore.Create(
                result.User.Id,
                providerConfig.Slug,
                state.ReturnUrl);

            SsoCookieWriter.Write(
                Response,
                LoginTicketStore.CookieName,
                binding,
                Math.Clamp(Configuration.TicketLifetimeSeconds, 30, 300),
                Request.IsHttps);

            return Redirect(LocalUrl("/sso/complete?ticket=" + Uri.EscapeDataString(ticket)));
        }
        catch (OidcException ex)
        {
            _logger.LogWarning(ex, "OIDC callback failed for provider {Provider}", providerConfig.Slug);
            return RedirectToComplete("provider_error");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected SSO callback failure for provider {Provider}", providerConfig.Slug);
            return RedirectToComplete("internal_error");
        }
    }

    /// <summary>
    /// Serves the completion page.
    /// </summary>
    [HttpGet("complete")]
    [AllowAnonymous]
    public IActionResult CompletePage()
    {
        return HtmlPage("complete.html");
    }

    /// <summary>
    /// Exchanges a one-time ticket for Jellyfin credentials.
    /// </summary>
    [HttpPost("complete")]
    [AllowAnonymous]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<CompleteLoginResponse>> Complete(
        [FromBody] CompleteLoginRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Ticket))
        {
            return BadRequest(new ApiError { Code = "invalid_request", Message = "A login ticket is required." });
        }

        var binding = Request.Cookies[LoginTicketStore.CookieName];
        var ticket = _ticketStore.Consume(request.Ticket, binding);
        SsoCookieWriter.Delete(Response, LoginTicketStore.CookieName, Request.IsHttps);

        if (ticket is null)
        {
            return BadRequest(new ApiError { Code = "invalid_ticket", Message = "This sign-in link has expired. Please return to the login page." });
        }

        var user = _userManager.GetUserById(ticket.JellyfinUserId);
        if (user is null)
        {
            return BadRequest(new ApiError { Code = "user_not_found", Message = "The Jellyfin user no longer exists." });
        }

        try
        {
            var result = await _sessionIssuer.IssueAsync(
                user,
                new SsoDeviceInfo
                {
                    DeviceId = request.DeviceId,
                    DeviceName = request.DeviceName,
                    AppName = request.AppName,
                    AppVersion = request.AppVersion
                },
                GetRemoteIp(),
                ticket.ProviderId,
                cancellationToken).ConfigureAwait(false);

            return new CompleteLoginResponse
            {
                AccessToken = result.AccessToken,
                ServerId = result.ServerId,
                ServerName = _applicationHost.FriendlyName,
                User = new SsoUserDto { Id = result.User.Id, Name = result.User.Name },
                ReturnUrl = SsoUrlBuilder.NormalizeReturnUrl(ticket.ReturnUrl)
            };
        }
        catch (SecurityException ex)
        {
            _logger.LogInformation(ex, "SSO session issuance denied for {Username}", user.Username);
            return StatusCode(StatusCodes.Status403Forbidden, new ApiError { Code = "login_denied", Message = ex.Message });
        }
        catch (AuthenticationException ex)
        {
            _logger.LogInformation(ex, "SSO session issuance failed for {Username}", user.Username);
            return StatusCode(StatusCodes.Status401Unauthorized, new ApiError { Code = "login_failed", Message = ex.Message });
        }
    }

    private async Task<Dictionary<string, string>> ReadCallbackValuesAsync(CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in Request.Query)
        {
            values[pair.Key] = pair.Value.ToString();
        }

        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync(cancellationToken).ConfigureAwait(false);
            foreach (var pair in form)
            {
                values[pair.Key] = pair.Value.ToString();
            }
        }

        return values;
    }
}
