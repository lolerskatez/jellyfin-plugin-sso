# Native clients and Quick Connect

Jellyfin native clients do not have a plugin login UI. The SSO button is only
injected into the Jellyfin Web client (including Jellyfin Desktop, which embeds
the web UI).

Native clients still work normally because the plugin issues standard Jellyfin
access tokens. There are two supported paths.

## Quick Connect (recommended)

1. Open the Jellyfin client. It shows a Quick Connect code.
2. On a phone or computer, open:
   ```text
   https://jellyfin.example.com/sso/quickconnect
   ```
3. Sign in with SSO if needed.
4. Enter the code shown by the client.
5. The client receives a normal Jellyfin session.

Quick Connect must be enabled in Jellyfin (it is enabled by default).

## Local password fallback

An existing Jellyfin user can link SSO and keep a local password. If the client
does not support Quick Connect, continue using the local password or have an
administrator set a password for the SSO-provisioned account.

## Why there is no native SSO button

A Jellyfin server plugin cannot add UI to Android, iOS, Roku, or TV clients. The
plugin deliberately does not try to fake a native login screen. Quick Connect
is the secure bridge that lets those clients obtain the same standard Jellyfin
token that the web client receives.
