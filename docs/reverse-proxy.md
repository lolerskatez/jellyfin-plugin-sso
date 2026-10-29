# Reverse proxy and URL configuration

OIDC redirect URIs must match exactly. Jellyfin behind a reverse proxy needs the
correct external scheme, host, and path base.

## Recommended Jellyfin setup

Configure Jellyfin's known proxies so forwarded headers are trusted. The plugin
uses the request's scheme, host, and path base to build the callback URL.

If automatic detection is wrong, set **Public base URL** in the plugin Settings
page:

```text
https://jellyfin.example.com
```

For a Jellyfin instance mounted at a subpath:

```text
https://example.com/jellyfin
```

The callback then becomes:

```text
https://example.com/jellyfin/sso/callback/authentik
```

## Reverse proxy examples

### Caddy

```caddyfile
jellyfin.example.com {
    reverse_proxy 127.0.0.1:8096 {
        header_up X-Forwarded-Proto {scheme}
        header_up X-Forwarded-Host {host}
    }
}
```

### nginx

```nginx
location / {
    proxy_pass http://127.0.0.1:8096;
    proxy_set_header Host $host;
    proxy_set_header X-Forwarded-Proto $scheme;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Host $host;
    proxy_set_header X-Real-IP $remote_addr;
}
```

## Cookies and HTTPS

The plugin uses short-lived HttpOnly, SameSite=Lax cookies for state binding and
the one-time login ticket. They are marked `Secure` when the request is HTTPS.
Use HTTPS in production; HTTP is intended only for local development.

## Multiple Jellyfin servers

The callback URL is derived per request and can be overridden per provider.
Each Jellyfin instance should use its own IdP client or a distinct redirect URI.
