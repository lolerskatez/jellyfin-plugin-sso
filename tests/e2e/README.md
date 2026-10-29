# Local end-to-end stack

The compose file starts:

- Jellyfin 12.4 on `http://localhost:18096`
- A small mock OIDC provider on `http://localhost:19090`

The mock provider is useful for development without a full Authentik instance.
It always signs in as `alice` unless `MOCK_OIDC_USER` is set, and supports the
`/set-user?name=...` endpoint for switching test users.

## Build and start

```powershell
dotnet build src/Jellyfin.Plugin.SSO/Jellyfin.Plugin.SSO.csproj -c Release
docker compose -f tests/e2e/docker-compose.yml up --build
```

## Complete Jellyfin setup

Jellyfin's setup wizard must be completed once. The easiest way is to open
`http://localhost:18096` and follow the wizard.

Then add the provider in **Dashboard → Plugins → Single Sign-On**:

- Slug: `authentik`
- Authority: `http://host.docker.internal:19090`
- Discovery URL: `http://host.docker.internal:19090/.well-known/openid-configuration`
- Issuer: `http://host.docker.internal:19090`
- Client ID: `jellyfin`
- Client secret: `test-secret`
- Scopes: `openid profile email groups`
- Group claim paths: `groups`
- Admin roles: `jellyfin-admins`
- Required roles: `jellyfin-users`
- Allow HTTP: enabled (test only)
- Token endpoint authentication: `client_secret_post`

Reload `http://localhost:18096/web/index.html#/login` and click
**Sign in with Authentik**.

## Test users

- `alice` — member of `jellyfin-users`
- `admin` — member of `jellyfin-users` and `jellyfin-admins`
- `bob` — no allowed group

Switch the mock user:

```text
http://localhost:19090/set-user?name=admin
```
