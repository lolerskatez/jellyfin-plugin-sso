# Authentik OIDC setup

Authentik is the primary supported provider. For the full field reference,
endpoint mapping, and troubleshooting, see [configuration.md](configuration.md).

## 1. Authentik: create the application and provider

In the Authentik admin interface:

1. Open **Applications → Applications** and create an application named
   `Jellyfin` (or edit the one you already have).
2. Create its **OAuth2/OpenID Provider** and set:

| Setting | Value |
|---|---|
| Client type | `Confidential` |
| Client ID | `jellyfin` (or any value you prefer) |
| Client secret | generate and copy it |
| Redirect URIs | `https://jellyfin.example.com/sso/callback/authentik` |
| Signing Key | a certificate/key pair (recommended, RS256) |
| Scopes | `openid`, `profile`, `email` |
| Issuer mode | default per-provider issuer |

3. The default `profile` scope already returns `groups`, so no separate
   `groups` scope mapping is required for a standard setup. If you use a custom
   claim, add your own scope mapping and request that scope.
4. Keep the default **Subject mode** (hashed user ID). Changing it after users
   have linked accounts changes every `sub` value and breaks the links.

The issuer is shown on the provider page and normally ends with `/`:

```text
https://authentik.example.com/application/o/jellyfin/
```

### Redirect URI

Use the exact callback URL for your deployment:

```text
https://jellyfin.example.com/sso/callback/authentik
```

The value is:

```text
<public base URL>/sso/callback/<slug>
```

The slug is chosen when you add the provider in Jellyfin. It is permanent: if
you change it, update the redirect URI in Authentik too.

The redirect URI must match exactly, including scheme, host, port, path, and
the absence of a trailing slash after the slug.

### Post-logout redirect URI

To have Authentik return users to Jellyfin after a logout, add a **Post Logout**
redirect URI (Authentik 2026.5+):

```text
https://jellyfin.example.com/web/index.html#/login
```

On older Authentik versions this redirect type may not be available. The
Jellyfin session still ends; Authentik will show its own logout page.

## 2. Jellyfin: add the provider

Open **Dashboard → Plugins → Single Sign-On → Providers → Add provider**.

| Field | Value |
|---|---|
| Slug | `authentik` |
| Protocol | `OpenID Connect (OIDC)` |
| Provider profile | `Authentik` |
| Display name | `Authentik` |
| Button text | `Sign in with Authentik` |
| Authority / issuer URL | `https://authentik.example.com/application/o/jellyfin/` |
| Discovery URL override | leave blank |
| Issuer override | leave blank |
| Client ID | `jellyfin` |
| Client secret | the generated secret |
| Scopes | `openid profile email` |
| Username claim | `preferred_username` |
| Display name claim | `name` |
| Email claim | `email` |
| Group claim paths | `groups` |
| Admin roles | `jellyfin-admins` (optional) |
| Required roles | `jellyfin-users` (optional) |
| Response mode | `query` |
| Token endpoint authentication | `client_secret_post` |
| Username conflict policy | `Pending` |
| Enabled | checked |
| Use PKCE | checked |
| Fetch UserInfo | checked |

Save the provider, then copy the **Redirect URI** shown by Jellyfin and make
sure the exact string is present in Authentik.

## 3. Verify

Click **Test discovery**. A successful test shows the issuer, authorization,
token, UserInfo, and JWKS endpoints.

Then open the Jellyfin login page in a private browser window and click
**Sign in with Authentik**.

## 4. Test users

| User | Groups | Expected |
|---|---|---|
| `alice` | `jellyfin-users` | normal user, auto-provisioned |
| `admin` | `jellyfin-users`, `jellyfin-admins` | administrator after an admin explicitly enables admin provisioning or links the account |
| `bob` | no `jellyfin-users` | denied when a required role is configured |

Group matching is case-insensitive and exact. Authentik group names may contain
spaces; configure the exact name.

## 5. Linking an existing user

1. Sign in to Jellyfin with the existing local account.
2. Open `/sso/link`.
3. Click the provider.
4. Complete the Authentik login.

The identity is linked only if it is not already linked to another Jellyfin
user. A username collision creates a pending request instead of linking
automatically.

## 6. Role mapping

- **Admin roles** grant `IsAdministrator`. New-user admin provisioning is off by
  default. Enable it only if Authentik and the group mapping are trusted.
- **Required roles** deny login if the user is not in at least one listed group.
- Role syncing is off by default. Turning it on applies mapped roles on every
  login; the last-administrator guard prevents removing the final admin.
- **Missing role behavior** controls what happens when a mapped role disappears:
  keep the existing policy, clear mapped permissions, or deny login.

Keep a separate local administrator account as a break-glass account.

## 7. SSO-only login

Enable **Settings → SSO-only login** to block password authentication for
auto-provisioned SSO users. Add at least one existing Jellyfin administrator to
the allowlist first; that account is the break-glass login if Authentik is
unavailable.

- Existing local users remain password-capable in balanced mode.
- Quick Connect remains available for native clients.
- API-key password verification remains allowed by default so management apps
  can verify credentials while linking.
- The web login page hides the local form and offers a “Use a local account”
  button when configured.

## 8. Logout

The plugin implements RP-initiated logout for Jellyfin Web sessions created
through SSO. Signing out from the user menu or settings page:

1. Revokes the Jellyfin session and clears browser credentials.
2. Redirects to the plugin’s `/sso/logout` endpoint.
3. Redirects to Authentik’s `end_session_endpoint` with `client_id` and
   `post_logout_redirect_uri`.
4. Authentik ends its session and returns to the Jellyfin web index.

Configure the destination under **Jellyfin → Single Sign-On → Providers →
Edit → Post-logout redirect URI**. Leave it blank to use:

```text
https://jellyfin.example.com/web/index.html
```

The provider card displays the effective **Post-logout URI**. Add that exact URI
to the Authentik provider as a **Post Logout** redirect URI (Authentik 2026.5+).

Notes:

- Local password sessions and native client sessions do not trigger Authentik
  logout.
- The plugin sends `client_id`, not `id_token_hint`.
- Sessions created before the logout feature was installed do not have the SSO
  marker; sign in through SSO again.

To make an Authentik logout end the user's sessions in all applications, select
an **Invalidation Flow** that includes the **User Logout** stage (for example
`default-invalidation-flow`) under the provider's advanced flow settings.

## Troubleshooting

- **`redirect_uri` mismatch**: copy the exact URI from the provider page in
  Jellyfin. Check reverse-proxy path base and scheme. If it works on the LAN but
  fails on your domain, set **Settings → Public base URL override** to the
  domain URL and add that callback to Authentik.
- **Discovery fails**: check that the Jellyfin container can reach the
  Authentik issuer. `localhost` inside a container is not the host.
- **Issuer mismatch**: use the issuer exactly as published by Authentik,
  including its trailing slash.
- **Groups missing**: Authentik's default `profile` scope returns `groups`;
  confirm the scope is enabled and the claim is not filtered by a policy.
- **Login page button missing**: hard-refresh the web client. Verify Jellyfin
  serves its own web client and that the plugin is enabled. For externally
  hosted web clients, use the branding fallback.
- **Immediately signed in again after logout**: the Authentik session is still
  active. Use the end-session URL above.
