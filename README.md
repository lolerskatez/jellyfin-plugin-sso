# Jellyfin Single Sign-On

An OpenID Connect (OIDC) authentication plugin for **Jellyfin 12.x**.

The plugin adds an SSO button to the Jellyfin Web login page, authenticates
users through an OIDC provider, and issues a **normal Jellyfin user session**.
Jellyfin remains responsible for users, libraries, playback, transcoding,
parental controls, and every other authorization decision.

## What it does

- OIDC Authorization Code flow with PKCE, state, nonce, and validated ID tokens
- Automatic login-page button injection into Jellyfin Web and Jellyfin Desktop
- Automatic Jellyfin user provisioning with a safe default policy
- Explicit account linking for existing Jellyfin users
- Pending approval queue for username collisions or approval-only setups
- Optional group/role mapping for administrator, library, and Live TV access
- Normal Jellyfin device tokens through `ISessionManager.AuthenticateDirect`
- Quick Connect helper page for Android/iOS/TV clients
- Admin page with provider configuration, discovery tests, links, approvals,
  audit log, and setup status
- JSON-backed link and audit stores; no Jellyfin database changes

## Requirements

- **Jellyfin 12.x** (tested with 12.4)
- **.NET 10 SDK** to build
- An OIDC provider. Authentik is the primary documented target.

## Install from the plugin repository

In Jellyfin, open **Dashboard → Plugins → Repositories**, add:

```text
https://raw.githubusercontent.com/lolerskatez/jellyfin-plugin-sso/main/manifest.json
```

Then install **Single Sign-On** from the plugin catalog. The repository is
updated automatically when a GitHub release is published.

## Project conventions

The solution follows the official
[jellyfin/jellyfin-plugin-template](https://github.com/jellyfin/jellyfin-plugin-template)
layout and conventions:

- `BasePlugin<TConfiguration>` + `IHasWebPages`
- `Jellyfin.Controller` / `Jellyfin.Model` references with `ExcludeAssets="runtime"`
- Embedded configuration page
- Version metadata in `Directory.Build.props`
- Jellyfin `.editorconfig`, nullable, analyzers, and warnings-as-errors
- `build.yaml` for JPRM packaging

It targets `net10.0` and Jellyfin 12.x, rather than the template's older
`net9.0`/10.11 defaults. Runtime IdentityModel assemblies are intentionally
included in `build.yaml` because the plugin uses them directly.

## Build

```powershell
dotnet restore
dotnet build -c Release
dotnet test
```

The plugin build output is under:

```text
src/Jellyfin.Plugin.SSO/bin/Release/net10.0/
```

Copy the entire output directory into:

```text
<Jellyfin config>/plugins/Jellyfin.Plugin.SSO/
```

Then restart Jellyfin.

For a packaged plugin, use `jprm` with `build.yaml`:

```powershell
jprm plugin build .
```

## Authentik quick start

1. In Authentik, create an **OIDC provider**:
   - Client type: Confidential
   - Redirect URI: `https://jellyfin.example.com/sso/callback/authentik`
   - Scopes: `openid profile email groups`
   - Ensure a `groups` scope mapping returns group names.
2. In Jellyfin, open **Dashboard → Plugins → Single Sign-On**.
3. Add a provider:
   - Slug: `authentik`
   - Authority: `https://authentik.example.com/application/o/jellyfin/`
   - Client ID / secret
   - Scopes: `openid profile email groups`
   - Username claim: `preferred_username`
   - Group paths: `groups`
   - Admin roles: `jellyfin-admins`
   - Required roles: `jellyfin-users` (optional)
4. Save and run **Test discovery**.
5. Copy the displayed redirect URI into Authentik exactly.
6. Reload the Jellyfin login page.

Detailed instructions are in [docs/authentik.md](docs/authentik.md).

The module boundaries and extension points are documented in
[docs/architecture.md](docs/architecture.md).

## How authentication stays compatible

After a successful OIDC callback the plugin resolves a real Jellyfin user and
calls:

```csharp
ISessionManager.AuthenticateDirect(...)
```

That returns the same `AuthenticationResult` as a normal Jellyfin login. The
completion page writes the standard `jellyfin_credentials` localStorage value
and redirects to the web client. All API calls use the normal
`Authorization: MediaBrowser Token="..."` header.

Because the plugin also manually enforces disabled-user, remote-access, and
parental-schedule checks that the password path normally performs, SSO users
behave like local users. Auto-provisioned users receive a random password so
they cannot log in with an empty password.

Existing Jellyfin users are **never automatically linked by username or email**.
They link explicitly through `/sso/link`, or an administrator approves a
pending request.

## Native clients

Jellyfin clients cannot render a server plugin's login button. Use the
Quick Connect helper:

1. The client shows a Quick Connect code.
2. Open `/sso/quickconnect` on a phone or browser.
3. Sign in with SSO.
4. Enter the code shown by the client.

The client receives a normal Jellyfin token and works exactly as before.

## Current limitations

- OIDC only; SAML is not implemented in this version.
- Jellyfin 10.11 is not supported by this build; it targets Jellyfin 12.x/.NET 10.
- Login-page injection applies to Jellyfin Web and Jellyfin Desktop. External
  web deployments need the branding fallback described in the admin page.
- OIDC single logout is not implemented.
- The provider client secret is stored in Jellyfin's plugin configuration, like
  other Jellyfin plugins. Protect the configuration directory.
- Multi-instance Jellyfin is not supported by the in-memory state/ticket stores.

## Development status

This is the initial implementation (0.1.0). The working path has been tested
against Jellyfin 12.4 with a mock OIDC provider:

- SSO button injection
- OIDC login and callback
- Automatic Jellyfin user provisioning
- Normal token issuance
- Browser credential write and redirect
- Username-collision pending approval
- Admin status/providers/settings APIs
- Link page and admin configuration page
