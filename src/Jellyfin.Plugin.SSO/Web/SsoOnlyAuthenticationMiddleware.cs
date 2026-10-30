using System.Text;
using System.Text.Json;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.SSO.Abstractions;
using Jellyfin.Plugin.SSO.Accounts;
using Jellyfin.Plugin.SSO.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SSO.Web;

/// <summary>
/// Blocks password authentication for SSO-only accounts while allowing trusted
/// API-key integrations to verify credentials.
/// </summary>
public sealed class SsoOnlyAuthenticationMiddleware
{
    private const string ErrorCode = "sso_only";
    private const string BlockedMessage = "Password sign-in is disabled for this account. Use Single Sign-On or Quick Connect.";

    private readonly RequestDelegate _next;
    private readonly IPluginConfigAccessor _config;
    private readonly ILogger<SsoOnlyAuthenticationMiddleware> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SsoOnlyAuthenticationMiddleware"/> class.
    /// </summary>
    public SsoOnlyAuthenticationMiddleware(
        RequestDelegate next,
        IPluginConfigAccessor config,
        ILogger<SsoOnlyAuthenticationMiddleware> logger)
    {
        _next = next;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Processes a request.
    /// </summary>
    public async Task InvokeAsync(
        HttpContext context,
        IAuthorizationContext authorizationContext,
        IUserManager userManager,
        ILinkStore linkStore,
        IAuditLog auditLog)
    {
        var settings = _config.Current.SsoOnly;
        if (!settings.Enabled || !IsCandidate(context.Request))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        if (settings.AllowApiKeyPasswordVerification)
        {
            try
            {
                var authorization = await authorizationContext.GetAuthorizationInfo(context.Request).ConfigureAwait(false);
                if (authorization.IsApiKey)
                {
                    await _next(context).ConfigureAwait(false);
                    return;
                }
            }
            catch (Exception ex)
            {
                // Fail closed: if we cannot inspect the request, continue to the policy check.
                _logger.LogDebug(ex, "Unable to inspect authorization information for SSO-only policy.");
            }
        }

        var user = await ResolveUserAsync(context.Request, userManager).ConfigureAwait(false);
        if (user is null)
        {
            // Let Jellyfin return its normal invalid-credentials response.
            await _next(context).ConfigureAwait(false);
            return;
        }

        var links = await linkStore.GetForUserAsync(user.Id, context.RequestAborted).ConfigureAwait(false);
        var decision = SsoOnlyPolicy.Evaluate(user.Id, links, settings);
        if (decision.Allowed)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        await WriteBlockedAsync(context, user, decision, auditLog).ConfigureAwait(false);
    }

    private static bool IsCandidate(HttpRequest request)
    {
        if (!HttpMethods.IsPost(request.Method))
        {
            return false;
        }

        var path = request.Path.Value;
        return !string.IsNullOrEmpty(path)
            && (path.EndsWith("/Users/AuthenticateByName", StringComparison.OrdinalIgnoreCase)
                || TryGetLegacyUserId(path).HasValue);
    }

    private static Guid? TryGetLegacyUserId(string path)
    {
        if (!path.EndsWith("/Authenticate", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 3
            || !string.Equals(segments[^3], "Users", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Guid.TryParse(segments[^2], out var userId) ? userId : null;
    }

    private static async Task<User?> ResolveUserAsync(HttpRequest request, IUserManager userManager)
    {
        var path = request.Path.Value ?? string.Empty;
        if (path.EndsWith("/Users/AuthenticateByName", StringComparison.OrdinalIgnoreCase))
        {
            var username = await ReadUsernameAsync(request).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(username) ? null : userManager.GetUserByName(username);
        }

        var userId = TryGetLegacyUserId(path);
        return userId.HasValue ? userManager.GetUserById(userId.Value) : null;
    }

    private static async Task<string?> ReadUsernameAsync(HttpRequest request)
    {
        if (request.ContentLength is 0)
        {
            return null;
        }

        request.EnableBuffering();
        request.Body.Position = 0;
        using var reader = new StreamReader(
            request.Body,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 1024,
            leaveOpen: true);
        var json = await reader.ReadToEndAsync().ConfigureAwait(false);
        request.Body.Position = 0;

        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, "Username", StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == JsonValueKind.String)
                {
                    return property.Value.GetString();
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static async Task WriteBlockedAsync(
        HttpContext context,
        User user,
        SsoOnlyDecision decision,
        IAuditLog auditLog)
    {
        try
        {
            await auditLog.WriteAsync(
                new AuditEvent
                {
                    Type = "PasswordLoginBlocked",
                    UserId = user.Id,
                    Username = user.Username,
                    IpHash = AuditLog.Fingerprint(context.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
                    Outcome = "blocked",
                    Detail = decision.Reason
                },
                context.RequestAborted).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Audit failures must not change the authentication decision.
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsJsonAsync(
            new { error = ErrorCode, message = BlockedMessage },
            context.RequestAborted).ConfigureAwait(false);
    }
}
