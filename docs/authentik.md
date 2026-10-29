# Authentik OIDC setup

This is the primary supported provider configuration.

## 1. Authentik: create the provider

In the Authentik admin interface:

1. Open **Applications → Providers** and create a new **OIDC Provider**.
2. Use these values:
   - **Name**: `Jellyfin`
   - **Authorization flow**: a flow that suits your users
   - **Client type**: `Confidential`
   - **Client ID**: `jellyfin`
   - **Client Secret**: generate and copy it
   - **Redirect URI**:
     ```text
     https://jellyfin.example.com/sso/callback/authentik
     ```
   - **Signing Key**: select a certificate
   - **Scopes**: `openid`, `profile`, `email`, `groups`
3. Create an **Application** bound to the provider.
4. Ensure the `groups` scope mapping is enabled and returns an array of group
   names. If your Authentik version does not provide one, create a scope mapping
   with:
   ```python
   return {"groups": [group.name for group in request.user.ak_groups.all()]}
   ```
   and bind it to the `groups` scope.
5. Create or use groups such as:
   - `jellyfin-users`
   - `jellyfin-admins`
6. Add test users to those groups.

## 2. Jellyfin: add the provider

Open **Dashboard → Plugins → Single Sign-On → Providers → Add provider**.

Recommended values:

| Field | Value |
|---|---|
| Slug | `authentik` |
| Display name | `Authentik` |
| Button text | `Sign in with Authentik` |
| Authority | `https://authentik.example.com/application/o/jellyfin/` |
| Discovery URL override | leave blank |
| Issuer override | leave blank |
| Client ID | `jellyfin` |
| Client secret | the generated secret |
| Scopes | `openid profile email groups` |
| Username claim | `preferred_username` |
| Display name claim | `name` |
| Email claim | `email` |
| Group claim paths | `groups` |
| Admin roles | `jellyfin-admins` |
| Required roles | `jellyfin-users` (optional) |
| Response mode | `query` |
| Token endpoint authentication | `client_secret_post` |
| Username conflict policy | `Pending` |

Save, then click **Test discovery**. A successful test shows the issuer,
authorization, token, UserInfo, and JWKS endpoints.

Copy the **Redirect URI** shown by Jellyfin and make sure it is present in
Authentik exactly, including the scheme, host, and path base.

## 3. Test users

| User | Groups | Expected |
|---|---|---|
| `alice` | `jellyfin-users` | normal user, auto-provisioned |
| `admin` | `jellyfin-users`, `jellyfin-admins` | administrator after an admin explicitly enables admin provisioning or links the account |
| `bob` | no `jellyfin-users` | denied when a required role is configured |

Group matching is case-insensitive and exact. Authentik group names may contain
spaces; configure the exact name.

## 4. First login

1. Open the Jellyfin login page.
2. Click **Sign in with Authentik**.
3. Complete Authentik authentication.
4. The plugin creates the Jellyfin user if allowed and lands on the home page.

Auto-provisioned users are hidden from the login user picker by default. They
receive a random password, so the normal Jellyfin password form cannot be used
for them unless an administrator sets a password.

## 5. Linking an existing user

1. Sign in to Jellyfin with the existing local account.
2. Open `/sso/link`.
3. Click the provider.
4. Complete the IdP login.

The identity is linked only if it is not already linked to another Jellyfin
user. A username collision creates a pending request instead of linking
automatically.

## 6. Role mapping

- **Admin roles** grant `IsAdministrator`. New-user admin provisioning is off by
  default. Enable it only if the IdP and claim mapping are trusted.
- **Required roles** deny login if the user is not in at least one listed group.
- **Folder roles** can be configured in Settings → Roles. When a configured role
  matches, the user is restricted to the union of its folder IDs.
- **Live TV roles** can grant Live TV access and management.
- Role syncing is off by default. Turning it on applies mapped roles on every
  login; the last-administrator guard prevents removing the final admin.

Keep a separate local administrator account as a break-glass account.

## Troubleshooting

- **`redirect_uri` mismatch**: copy the exact URI from the provider page in
  Jellyfin. Check reverse-proxy path base and scheme.
- **Discovery fails**: check that the Jellyfin container can reach the
  Authentik authority URL. `localhost` inside a container is not the host.
- **Issuer mismatch**: use the issuer exactly as published by Authentik,
  including its trailing slash.
- **Groups missing**: enable the `groups` scope mapping and request the `groups`
  scope.
- **Login page button missing**: hard-refresh the web client. Verify Jellyfin
  serves its own web client and that the plugin is enabled. For externally
  hosted web clients, use the branding fallback.
