using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Jellyfin.Plugin.SSO.Web;

/// <summary>
/// Inserts the SSO login-page middleware before Jellyfin's static file middleware.
/// </summary>
public sealed class LoginPageStartupFilter : IStartupFilter
{
    /// <inheritdoc />
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.UseMiddleware<SsoOnlyAuthenticationMiddleware>();
            app.UseMiddleware<LoginPageMiddleware>();
            next(app);
        };
    }
}
