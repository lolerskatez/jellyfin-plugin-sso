using Jellyfin.Plugin.SSO.Abstractions;
using Jellyfin.Plugin.SSO.Accounts;
using Jellyfin.Plugin.SSO.Api;
using Jellyfin.Plugin.SSO.Configuration;
using Jellyfin.Plugin.SSO.Oidc;
using Jellyfin.Plugin.SSO.Sessions;
using Jellyfin.Plugin.SSO.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.SSO.DependencyInjection;

/// <summary>
/// Composes the plugin from small, replaceable modules.
/// </summary>
public static class SsoServiceCollectionExtensions
{
    /// <summary>
    /// Registers all SSO modules.
    /// </summary>
    public static IServiceCollection AddSso(this IServiceCollection services)
    {
        services.AddSsoCore();
        services.AddSsoOidc();
        services.AddSsoAccounts();
        services.AddSsoWeb();
        return services;
    }

    /// <summary>
    /// Registers configuration, persistence, and cross-cutting services.
    /// </summary>
    public static IServiceCollection AddSsoCore(this IServiceCollection services)
    {
        services.AddSingleton<IPluginConfigAccessor, PluginConfigAccessor>();
        services.AddSingleton<ILinkStore, LinkStore>();
        services.AddSingleton<IPendingStore, PendingStore>();
        services.AddSingleton<IAuditLog, AuditLog>();
        services.AddSingleton<IProviderConfigurationValidator, ProviderConfigurationValidator>();
        return services;
    }

    /// <summary>
    /// Registers OIDC discovery, token exchange, validation, and transient flow state.
    /// </summary>
    public static IServiceCollection AddSsoOidc(this IServiceCollection services)
    {
        services.AddSingleton<ClaimExtractor>();
        services.AddSingleton<IOidcProviderFactory, OidcProviderFactory>();
        services.AddSingleton<AuthorizationStateStore>();
        services.AddSingleton<LoginTicketStore>();

        services.AddHttpClient("sso", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Jellyfin-Plugin-SSO/0.1");
        });

        return services;
    }

    /// <summary>
    /// Registers user resolution, provisioning, policy mapping, and session issuance.
    /// </summary>
    public static IServiceCollection AddSsoAccounts(this IServiceCollection services)
    {
        services.AddSingleton<IUsernameResolver, UsernameResolver>();
        services.AddSingleton<IPolicyMapper, PolicyMapper>();
        services.AddSingleton<IUserProvisioner, UserProvisioner>();
        services.AddSingleton<IIdentityResolver, IdentityResolver>();
        services.AddSingleton<ISessionIssuer, SessionIssuer>();
        return services;
    }

    /// <summary>
    /// Registers web integration points.
    /// </summary>
    public static IServiceCollection AddSsoWeb(this IServiceCollection services)
    {
        services.AddSingleton<ISsoPageRenderer, SsoPageRenderer>();
        services.AddSingleton<ISsoRateLimiter, SsoRateLimiter>();
        services.AddSingleton<IStartupFilter, LoginPageStartupFilter>();
        return services;
    }
}
