using Jellyfin.Plugin.SSO.Api;

namespace Jellyfin.Plugin.SSO.Abstractions;

/// <summary>
/// Validates provider configuration before it is persisted.
/// </summary>
public interface IProviderConfigurationValidator
{
    bool Validate(AdminProviderDto request, out string? validationError);
}
