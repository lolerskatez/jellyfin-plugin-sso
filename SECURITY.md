# Security policy

## Reporting a vulnerability

Please report security issues privately to the maintainers rather than opening a
public issue with exploit details.

## Security model

- SSO is an authentication front end only. Jellyfin remains the authorization
  authority for libraries, playback, downloads, Live TV, and parental controls.
- External identities are keyed by `(provider, issuer, subject)`, never by
  username or email.
- Existing Jellyfin accounts are not auto-linked. Linking requires an
  authenticated Jellyfin session or an administrator approval.
- Auto-provisioned accounts receive a random password. Jellyfin normally permits
  password-less users to sign in with an empty password, so this is required.
- OIDC uses Authorization Code with PKCE, state, nonce, issuer/audience/lifetime
  validation, and a fresh JWKS fetch on key rotation.
- The callback creates a one-time ticket bound to an HttpOnly browser cookie.
- Disabled, remote-access, and parental-schedule checks are performed explicitly
  before `ISessionManager.AuthenticateDirect`.
- The last administrator is protected from role-sync demotion.

## Secrets

The OIDC client secret is stored in Jellyfin's plugin configuration file. Protect
the Jellyfin configuration directory and its backups. Secrets are masked in the
admin API and are never logged.

## Deployment recommendations

- Use HTTPS.
- Keep a separate local administrator account as a break-glass account.
- Restrict IdP registration and admin claims to trusted users.
- Do not enable claim-based administrator provisioning for untrusted providers.
- Review provider settings after upgrades.
