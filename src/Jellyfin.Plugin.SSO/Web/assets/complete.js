(function () {
    'use strict';

    var baseMeta = document.querySelector('meta[name="sso-base"]');
    var basePath = baseMeta ? (baseMeta.getAttribute('content') || '') : '';
    var apiBase = window.location.origin + basePath;
    var params = new URLSearchParams(window.location.search);

    var title = document.getElementById('sso-title');
    var message = document.getElementById('sso-message');
    var detail = document.getElementById('sso-detail');
    var spinner = document.getElementById('sso-spinner');
    var actions = document.getElementById('sso-actions');

    var errors = {
        access_denied: 'Sign-in was cancelled at the identity provider.',
        invalid_state: 'This sign-in request has expired. Please start again from the login page.',
        provider_error: 'The identity provider could not complete sign-in.',
        provider_unavailable: 'The identity provider is currently unavailable.',
        provider_not_found: 'That SSO provider is not configured or is disabled.',
        missing_code: 'The identity provider did not return an authorization code.',
        invalid_identity: 'The identity provider returned an incomplete identity.',
        login_denied: 'This SSO account is not allowed to sign in.',
        account_disabled: 'This Jellyfin account is disabled.',
        email_not_allowed: 'This email address is not allowed on this server.',
        role_required: 'Your account is not a member of a required group.',
        orphaned_link: 'This SSO account is linked to a Jellyfin user that no longer exists. Ask an administrator to repair it.',
        invalid_ticket: 'This sign-in link has expired. Please start again from the login page.',
        user_not_found: 'The linked Jellyfin user no longer exists.',
        internal_error: 'Jellyfin could not complete the sign-in. Check the server log for details.',
        rate_limited: 'Too many sign-in attempts. Wait a minute and try again.'
    };

    function showError(code) {
        if (spinner) {
            spinner.hidden = true;
        }
        title.textContent = 'Unable to sign in';
        message.textContent = errors[code] || 'Jellyfin could not complete the sign-in.';
        message.className = 'ssoError';
        detail.textContent = code ? 'Reference: ' + code : '';
        actions.hidden = false;
    }

    function showPending() {
        if (spinner) {
            spinner.hidden = true;
        }
        title.textContent = 'Approval required';
        message.textContent = 'Your identity was verified, but this account is not linked to Jellyfin yet. An administrator must approve the request.';
        detail.textContent = 'After approval, return to the login page and sign in again.';
        actions.hidden = false;
    }

    function getDeviceId() {
        try {
            var existing = localStorage.getItem('_deviceId2');
            if (existing) {
                return existing;
            }

            var generated = (window.crypto && crypto.randomUUID)
                ? crypto.randomUUID()
                : 'sso-' + Date.now().toString(36) + '-' + Math.random().toString(36).slice(2);
            localStorage.setItem('_deviceId2', generated);
            return generated;
        } catch (e) {
            return 'sso-' + Date.now().toString(36) + '-' + Math.random().toString(36).slice(2);
        }
    }

    function getDeviceName() {
        var ua = navigator.userAgent || '';
        if (window.NativeShell && window.NativeShell.AppHost && window.NativeShell.AppHost.deviceName) {
            try {
                return window.NativeShell.AppHost.deviceName();
            } catch (e) {
                // Fall through to user-agent detection.
            }
        }
        if (/android/i.test(ua)) return 'Android Browser';
        if (/iphone|ipad|ipod/i.test(ua)) return 'iOS Browser';
        if (/firefox/i.test(ua)) return 'Firefox';
        if (/edg\//i.test(ua)) return 'Edge';
        if (/chrome\//i.test(ua)) return 'Chrome';
        if (/safari/i.test(ua)) return 'Safari';
        return 'Web Browser';
    }

    function getAppInfo() {
        if (window.NativeShell && window.NativeShell.AppHost) {
            try {
                return {
                    name: window.NativeShell.AppHost.appName ? window.NativeShell.AppHost.appName() : 'Jellyfin Web',
                    version: window.NativeShell.AppHost.appVersion ? window.NativeShell.AppHost.appVersion() : '12.0.0'
                };
            } catch (e) {
                // Fall through.
            }
        }
        return { name: 'Jellyfin Web', version: '12.0.0' };
    }

    function loadCredentials() {
        try {
            var raw = localStorage.getItem('jellyfin_credentials');
            var parsed = raw ? JSON.parse(raw) : {};
            if (!parsed || typeof parsed !== 'object') {
                parsed = {};
            }
            if (!Array.isArray(parsed.Servers)) {
                parsed.Servers = [];
            }
            return parsed;
        } catch (e) {
            return { Servers: [] };
        }
    }

    function saveCredentials(result, serverName) {
        var credentials = loadCredentials();
        var address = apiBase;
        var user = result.User || result.user || {};
        var server = credentials.Servers.find(function (item) {
            return item && (item.Id === result.ServerId || item.ManualAddress === address);
        });

        if (!server) {
            server = {};
            credentials.Servers.push(server);
        }

        server.Id = result.ServerId;
        server.Name = serverName || server.Name || window.location.hostname;
        server.ManualAddress = address;
        server.LastConnectionMode = 2;
        server.AccessToken = result.AccessToken;
        server.UserId = user.Id || user.id;
        server.DateLastAccessed = Date.now();

        localStorage.setItem('jellyfin_credentials', JSON.stringify(credentials));
        localStorage.setItem('enableAutoLogin', 'true');
    }

    function safeReturnUrl(value) {
        if (typeof value !== 'string' || value.charAt(0) !== '/' || value.indexOf('//') === 0 || value.indexOf('\\') >= 0) {
            return '/web/index.html#/home';
        }
        return value;
    }

    function finish(result) {
        var returnUrl = safeReturnUrl(result.ReturnUrl);
        var target = window.location.origin + basePath + returnUrl;

        fetch(apiBase + '/System/Info/Public', {
            headers: { Accept: 'application/json; profile="CamelCase"' },
            credentials: 'same-origin'
        }).then(function (response) {
            return response.ok ? response.json() : null;
        }).catch(function () {
            return null;
        }).then(function (info) {
            var serverName = info && (info.ServerName || info.serverName);
            saveCredentials(result, serverName);
            window.location.replace(target);
        });
    }

    function complete(ticket) {
        var app = getAppInfo();
        var body = {
            ticket: ticket,
            deviceId: getDeviceId(),
            deviceName: getDeviceName(),
            appName: app.name,
            appVersion: app.version
        };

        fetch(apiBase + '/sso/complete', {
            method: 'POST',
            credentials: 'same-origin',
            headers: {
                'Content-Type': 'application/json',
                Accept: 'application/json; profile="CamelCase"'
            },
            body: JSON.stringify(body)
        }).then(function (response) {
            return response.json().catch(function () { return {}; }).then(function (data) {
                if (!response.ok) {
                    throw new Error(data.code || data.Code || 'login_failed');
                }
                return data;
            });
        }).then(function (result) {
            finish({
                AccessToken: result.AccessToken || result.accessToken,
                ServerId: result.ServerId || result.serverId,
                User: result.User || result.user,
                ReturnUrl: result.ReturnUrl || result.returnUrl
            });
        }).catch(function (error) {
            showError(error.message || 'internal_error');
        });
    }

    var error = params.get('error');
    var pending = params.get('pending');
    var ticket = params.get('ticket');

    if (error) {
        showError(error);
    } else if (pending) {
        showPending();
    } else if (!ticket) {
        showError('invalid_ticket');
    } else {
        complete(ticket);
    }
})();
