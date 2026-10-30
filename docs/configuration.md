# Configuration guide

This guide configures the Single Sign-On plugin end to end: the Authentik
application, the redirect URI, the plugin provider, account linking, roles, and
logout.

It applies to **Jellyfin 12.x** and plugin **0.1.0.2 or newer**. Authentik is the
primary example; the same fields work for any standard OIDC provider.

## Before you start

You need:

- Jellyfin 12.x with the **Single Sign-On** plugin installed and enabled.
- An OIDC application in your identity provider (Authentik is used below).
- The URL users type to reach Jellyfin. This is the **public base URL**.
- The issuer URL of the provider application.

### URLs used by the plugin

| URL | Where it comes from |
|---|---|
| Public base URL | Detected from the browser request, or **Settings → Public base URL override** |
| Callback / redirect URI | `<public base URL>/sso/callback/<slug>` |
| Post-logout landing page | `<public base URL>/web/index.html#/login` |
| Discovery document | `<issuer>/.well-known/openid-configuration` |
| Authorization, token, UserInfo, JWKS endpoints | Read automatically from the discovery document |
| End-session (logout) endpoint | Published in discovery; used manually for logout today |

The plugin only needs the **issuer** and the **client credentials**. It fetches
the authorization, token, UserInfo, JWKS, and end-session endpoints from the
discovery document. There are no separate fields for those endpoints.

> **Important:** the callback URL contains a slug chosen in the plugin. If you
> change the slug later, the callback URL changes and the old redirect URI stops
> working. Pick the slug once.

## Step 1 — Choose the public URLs

Decide how users reach Jellyfin:

| Deployment | Public base URL example |
|---|---|
| Direct LAN access | `http://192.168.1.183:8096` |
| Reverse proxy with HTTPS | `https://jellyfin.example.com` |
| Reverse proxy on a path | `https://example.com/jellyfin` |

If the URL users see is different from the URL Jellyfin sees internally (reverse
proxy, NAT, different port), set **Dashboard → Plugins → Single Sign-On →
Settings → Public base URL override** to the external URL. Otherwise leave it
blank and let Jellyfin detect it from the request.

The callback URL is then:

```text
<public base URL>/sso/callback/<slug>
```

For the examples above with slug `authentik`:

```text
http://192.168.1.183:8096/sso/callback/authentik
https://jellyfin.example.com/sso/callback/authentik
https://example.com/jellyfin/sso/callback/authentik
```

## Step 2 — Create the Authentik application and provider

In Authentik:

1. Open **Applications → Applications** and create or edit the application for
   Jellyfin (for example `Jellyfin`).
2. Create or edit its **OAuth2/OpenID Provider** with these values:

| Setting | Value |
|---|---|
| Client type | `Confidential` |
| Client ID | your choice, for example `jellyfin` |
| Client secret | generate and copy it |
| Redirect URIs | the callback URL from Step 1 |
| Signing Key | a certificate/key pair (recommended, RS256) |
| Scopes | `openid`, `profile`, `email` |
| Issuer mode | default per-provider issuer |

3. Authentik's default **profile** scope already returns the `groups` claim, so
   you do **not** need a separate `groups` scope for a standard setup. If you
   use a custom scope that emits groups, request that scope instead.
4. Keep the default **Subject mode** (hashed user ID). Changing it after users
   have linked accounts changes every `sub` value and breaks existing links.

Copy the **Client ID** and **Client secret**. You also need the application's
issuer, shown on the provider page:

```text
https://auth.example.com/application/o/<application-slug>/
```

The discovery document is at:

```text
https://auth.example.com/application/o/<application-slug>/.well-known/openid-configuration
```

### Redirect URI rules

The redirect URI must match **exactly**:

- same scheme (`http` or `https`)
- same host and port
- same path, including `/sso/callback/<slug>`
- no trailing slash after the slug

If Authentik rejects the login with `invalid redirect_uri`, compare the value
shown in **Single Sign-On → Providers** with the value saved in Authentik,
character by character.

### Post-logout redirect URI (optional)

If you want Authentik to return users to Jellyfin after an Authentik logout, add
the Jellyfin login page to the provider's redirect URIs:

- **Authentik 2026.5 and newer:** add a redirect URI with type **Post Logout**.
- **Older versions:** this entry type may not exist; upgrade Authentik or skip
  the `post_logout_redirect_uri` parameter described in [Logout](#logout).

Value:

```text
<public base URL>/web/index.html#/login
```

## Step 3 — Add the provider in Jellyfin

Open **Dashboard → Plugins → Single Sign-On → Providers → Add provider**.

Recommended values for Authentik:

| Field | Value |
|---|---|
| Slug | `authentik` (permanent; becomes part of the callback URL) |
| Protocol | `OpenID Connect (OIDC)` |
| Provider profile | `Authentik` |
| Display name | `Authentik` |
| Button text | `Sign in with Authentik` |
| Authority / issuer URL | `https://auth.example.com/application/o/jellyfin/` |
| Discovery URL override | leave blank |
| Issuer override | leave blank |
| Client ID | from Authentik |
| Client secret | from Authentik |
| Scopes | `openid profile email` |
| Username claim | `preferred_username` |
| Display name claim | `name` |
| Email claim | `email` |
| Group claim paths | `groups` |
| Admin roles | e.g. `jellyfin-admins` (optional) |
| Required roles | e.g. `jellyfin-users` (optional) |
| Response mode | `query (recommended)` |
| Token endpoint authentication | `client_secret_post` |
| Username conflict policy | `Pending — admin approval` |
| Enabled | checked |
| Use PKCE | checked |
| Fetch UserInfo | checked |

Save the provider. The Providers tab now shows the exact **Redirect URI** for
that provider. Copy it into Authentik if you have not already.

### Field notes

- **Authority / issuer URL** — the issuer, ending with `/` in Authentik. The
  plugin appends `/.well-known/openid-configuration`. You may paste the full
  discovery URL here instead; the plugin detects the suffix.
- **Discovery URL override** — only needed when discovery is not at
  `<issuer>/.well-known/openid-configuration`, or when the issuer cannot be
  derived from the discovery URL. Paste the full discovery URL.
- **Issuer override** — only needed when the issuer in the discovery document
  differs from the one you want to enforce. Leave blank for Authentik.
- **Scopes** — Authentik's default `profile` scope includes `groups`. Add a
  custom scope only if your provider emits groups under a different scope.
- **Username claim** — `preferred_username` is the Authentik username.
- **Group claim paths** — comma-separated JSON paths. `groups` is the default.
- **Admin roles / Required roles** — comma-separated group names; matching is
  case-insensitive and exact.
- **Response mode** — keep `query` unless your provider requires `form_post`.
- **Token endpoint authentication** — `client_secret_post` for a confidential
  Authentik client. Use `client_secret_basic` if your provider requires HTTP
  Basic, or `none (public + PKCE)` only for a public client.
- **Username conflict policy** — what happens when the IdP username already
  belongs to a different Jellyfin user. `Pending` sends the request to the
  approval queue instead of taking over the account.

### If your provider gives you separate endpoint URLs

Many providers show a list like this:

```text
OpenID Configuration URL   https://auth.example.com/application/o/app/.well-known/openid-configuration
Issuer                     https://auth.example.com/application/o/app/
Authorize URL              https://auth.example.com/application/o/authorize/
Token URL                  https://auth.example.com/application/o/token/
UserInfo URL               https://auth.example.com/application/o/userinfo/
Logout URL                 https://auth.example.com/application/o/app/end-session/
JWKS URL                   https://auth.example.com/application/o/app/jwks/
```

Only two of them belong in the plugin:

| Provider value | Plugin field |
|---|---|
| Issuer | **Authority / issuer URL** (recommended) |
| OpenID Configuration URL | **Discovery URL override** (optional; leave blank when the issuer is correct) |
| Authorize URL | Not a field — read from discovery |
| Token URL | Not a field — read from discovery |
| UserInfo URL | Not a field — read from discovery |
| JWKS URL | Not a field — read from discovery |
| Logout URL | Not used for login — see [Logout](#logout) |

## Step 4 — Verify discovery and login

1. In **Providers**, click **Test discovery** next to the provider.
2. A successful test shows the issuer and the authorization, token, UserInfo,
   and JWKS endpoints.
3. Open the Jellyfin login page in a private browser window.
4. Click **Sign in with Authentik**.
5. Authenticate in Authentik.
6. Jellyfin should land on the home page with a normal Jellyfin session.

The status page should show:

- **Login page injection:** Enabled
- **Enabled providers:** 1 (or more)
- The plugin version and Jellyfin version

### First login and new users

With the default global settings, a new SSO user is provisioned automatically
and hidden from the local login user picker. The account receives a random
password, so it cannot be used with the password form until an administrator
sets one.

If **Require administrator approval for new users** is enabled, or a username
collision occurs, the request appears under **Pending**. Approve it to create or
link the user.

## Step 5 — Link an existing Jellyfin user

Existing users are never linked automatically by username or email. To link one:

1. Sign in to Jellyfin with the existing local account.
2. Open:

   ```text
   <public base URL>/sso/link
   ```

3. Click the provider button.
4. Complete the provider login.

The link is created only if that external identity is not already linked to a
different Jellyfin user. Otherwise the request is rejected or queued for
approval, depending on the conflict policy.

Administrators can review and revoke links under **Single Sign-On → Links**.

## Step 6 — Roles and policy

- **Admin roles** grant Jellyfin administrator rights when the claim contains
  one of the listed groups. New-user admin provisioning is disabled by default;
  enable **Allow new users to become administrators from claims** only when the
  identity provider and group mapping are trusted.
- **Required roles** deny login when the user is not in at least one listed
  group. Leave empty to allow every authenticated user.
- **Sync roles on every login** applies the mapped roles at each login. The
  last-administrator guard prevents removing the final Jellyfin administrator.
- **Missing role behavior** controls what happens when a previously mapped role
  disappears: keep the existing policy, clear mapped permissions, or deny login.
- Group names are case-insensitive and must match exactly, including spaces.

Keep a separate local administrator account as a break-glass account.

## Logout

The plugin currently issues a normal Jellyfin session and does not perform
OIDC RP-initiated logout, front-channel logout, or back-channel logout. This
means:

1. **Jellyfin logout** ends the Jellyfin device session and token.
2. **The Authentik session remains active.**
3. If the user clicks **Sign in with Authentik** again, Authentik may sign them
   back in without prompting. This is normal SSO behavior, not a plugin error.

### Sign out of Authentik manually

Use the end-session endpoint from the discovery document:

```text
https://auth.example.com/application/o/<application-slug>/end-session/
```

Add the Jellyfin login page as the post-logout target:

```text
https://auth.example.com/application/o/<application-slug>/end-session/?client_id=<client-id>&post_logout_redirect_uri=https%3A%2F%2Fjellyfin.example.com%2Fweb%2Findex.html%23%2Flogin
```

Notes:

- URL-encode the redirect URI. The `#` becomes `%23`.
- Register `<public base URL>/web/index.html#/login` as a **Post Logout**
  redirect URI in Authentik (2026.5+). Without it, Authentik shows its own
  logout confirmation page instead of returning to Jellyfin.
- `client_id` is optional but helps Authentik select the right provider.

### Sign out of everything

If you want an Authentik logout to end the user's session in Jellyfin and other
applications, configure a **Single Logout** flow in Authentik:

1. Open the provider in Authentik.
2. Under **Advanced flow settings**, set **Invalidation Flow** to a flow that
   includes the **User Logout** stage (for example `default-invalidation-flow`).
3. Save.

This does not change how Jellyfin initiates logout. It only controls what
happens when the logout starts in Authentik. Jellyfin does not expose a
front-channel or back-channel logout endpoint in this plugin version, so
Authentik cannot terminate a Jellyfin session directly.

## Troubleshooting

| Symptom | Check |
|---|---|
| `redirect_uri` mismatch | Copy the exact URI from the plugin's provider page. Check scheme, port, path base, and trailing slash. |
| Discovery fails | Jellyfin must reach the issuer over the network. `localhost` inside a container is not the host. Check DNS, TLS, and proxy rules. |
| `issuer` mismatch | The discovery document's `iss` must equal the configured issuer. In Authentik it normally ends with `/`. |
| Login button missing | Hard-refresh the web client. Check **Login page injection** on the status page. External web clients need the branding fallback from the Settings tab. |
| Groups missing / admin not granted | Confirm the provider returns `groups` (Authentik's `profile` scope does by default). Check **Group claim paths**, then sign in again. |
| Always signed in without a prompt | The Authentik session cookie is still active. Use the end-session URL above. |
| Approval queue grows | **Require administrator approval for new users** is on, or usernames collide with existing Jellyfin users. Review **Pending**. |
| `state` expired | The login took longer than the state lifetime (default 10 minutes). Start again. |
| Reverse proxy issues | Set **Public base URL override** to the external URL and forward `Host` / `X-Forwarded-*` headers correctly. |

For provider-specific Authentik notes, see [authentik.md](authentik.md). For the
architecture, see [architecture.md](architecture.md).
