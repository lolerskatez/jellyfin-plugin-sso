# Architecture and extension points

The plugin is intentionally split into small modules so future protocols,
storage engines, or Jellyfin client integrations do not require rewriting the
controllers.

## Layers

```text
Api/
  SsoAuthController          public OIDC login/callback/complete
  SsoAccountController       authenticated linking and Quick Connect
  SsoAdminController         admin configuration and operations
  SsoControllerBase          shared URL, auth, and page helpers
  ProviderConfigurationValidator

Oidc/
  OidcProvider               facade used by controllers
  OidcDiscoveryService       discovery document + JWKS cache
  OidcTokenClient            token exchange + UserInfo
  OidcTokenValidator         ID token validation
  ClaimExtractor             claim-path and identity normalization
  AuthorizationStateStore    state/nonce/PKCE lifetime
  LoginTicketStore           one-time browser-bound ticket

Accounts/
  IdentityResolver           issuer+subject -> Jellyfin user
  UserProvisioner            create user, policy, random password, link
  UsernameResolver           safe/unique Jellyfin usernames
  PolicyMapper               groups -> Jellyfin policy
  LinkStore                  external identity links
  PendingStore               approval queue
  AuditLog                   append-only events

Sessions/
  SessionIssuer              native Jellyfin device session

Web/
  LoginPageStartupFilter     ASP.NET Core IStartupFilter
  LoginPageMiddleware        serve-time index.html injection
  SsoPageRenderer            standalone plugin pages
  SsoUrlBuilder / SsoCookieWriter / SsoRateLimiter

Configuration/
  PluginConfiguration        persisted settings
  PluginConfigurationMigrator versioned migrations
  IPluginConfigAccessor      live config access

Abstractions/                interfaces used by controllers and tests
DependencyInjection/         module composition
Storage/                     generic atomic JSON persistence
```

## Extension points

- **New protocol (for example SAML):** add a provider implementation beside
  `OidcProvider`, add its runtime factory, and route it from
  `SsoAuthController`/`SsoAccountController` based on `OidcProviderConfig.Protocol`.
  The user provisioning, linking, policy, session, storage, and admin layers are
  protocol-independent.
- **New IdP profile:** add a value to the provider profile list and provider
  defaults. Claim paths, scopes, and role mappings remain configuration, not code.
- **New storage backend:** replace `ILinkStore`, `IPendingStore`, or `IAuditLog`
  registrations in `SsoServiceCollectionExtensions`. Controllers depend on the
  interfaces, not the JSON implementations.
- **New Jellyfin client integration:** add a web module or endpoint beside
  `SsoAccountController`; use the same `IIdentityResolver` and `ISessionIssuer`.
- **Configuration changes:** add an `IPluginConfigurationMigration` and increase
  `PluginConfigurationMigrator.CurrentVersion`. The plugin runs migrations before
  services are constructed.
- **New API capability:** add a focused controller or service; keep protocol
  flow logic out of the admin controllers.

## Compatibility rule

Authentication is only a front end. Every successful flow ends in
`ISessionIssuer`, which calls Jellyfin's normal session manager. The plugin never
issues its own media tokens and never bypasses Jellyfin's library policy.
