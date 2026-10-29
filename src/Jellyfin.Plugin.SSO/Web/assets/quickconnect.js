(function () {
    'use strict';

    var baseMeta = document.querySelector('meta[name="sso-base"]');
    var basePath = baseMeta ? (baseMeta.getAttribute('content') || '') : '';
    var apiBase = window.location.origin + basePath;
    var status = document.getElementById('sso-status');
    var providerHost = document.getElementById('sso-provider-actions');
    var form = document.getElementById('sso-code-form');
    var codeInput = document.getElementById('sso-code');

    function currentServer() {
        try {
            var data = JSON.parse(localStorage.getItem('jellyfin_credentials') || '{}');
            var servers = Array.isArray(data.Servers) ? data.Servers : [];
            return servers
                .filter(function (server) { return server && server.AccessToken && server.UserId; })
                .sort(function (a, b) { return (b.DateLastAccessed || 0) - (a.DateLastAccessed || 0); })[0];
        } catch (e) {
            return null;
        }
    }

    function headers(token) {
        return {
            Accept: 'application/json; profile="CamelCase"',
            'Content-Type': 'application/json',
            'X-Emby-Token': token,
            'Authorization': 'MediaBrowser Token="' + token + '"'
        };
    }

    function setStatus(text, kind) {
        if (!text) {
            status.hidden = true;
            return;
        }
        status.hidden = false;
        status.textContent = text;
        status.className = kind === 'error' ? 'ssoError' : (kind === 'success' ? 'ssoSuccess' : 'ssoMuted');
    }

    function loadProviders() {
        var server = currentServer();
        if (server) {
            form.hidden = false;
            providerHost.replaceChildren();
            return;
        }

        form.hidden = true;
        fetch(apiBase + '/sso/providers', {
            credentials: 'same-origin',
            headers: { Accept: 'application/json; profile="CamelCase"' }
        }).then(function (response) {
            return response.ok ? response.json() : { providers: [] };
        }).then(function (data) {
            var providers = Array.isArray(data.providers) ? data.providers : [];
            providerHost.replaceChildren();

            if (!providers.length) {
                var text = document.createElement('p');
                text.className = 'ssoMuted';
                text.textContent = 'No SSO providers are enabled. Sign in to Jellyfin normally first.';
                providerHost.appendChild(text);
                return;
            }

            var heading = document.createElement('p');
            heading.textContent = 'Sign in with SSO:';
            providerHost.appendChild(heading);

            providers.forEach(function (provider) {
                var link = document.createElement('a');
                link.className = 'ssoButton';
                link.href = apiBase + '/sso/start/' + encodeURIComponent(provider.slug)
                    + '?returnUrl=' + encodeURIComponent('/sso/quickconnect');
                link.textContent = provider.buttonText || ('Sign in with ' + provider.displayName);
                providerHost.appendChild(link);
            });
        }).catch(function (error) {
            setStatus(error.message, 'error');
        });
    }

    form.addEventListener('submit', function (event) {
        event.preventDefault();
        var server = currentServer();
        if (!server) {
            setStatus('Sign in first.', 'error');
            return;
        }

        var code = codeInput.value.trim();
        if (!code) {
            setStatus('Enter the code shown on your device.', 'error');
            return;
        }

        fetch(apiBase + '/sso/quickconnect/authorize', {
            method: 'POST',
            credentials: 'same-origin',
            headers: headers(server.AccessToken),
            body: JSON.stringify({ code: code })
        }).then(function (response) {
            return response.json().catch(function () { return {}; }).then(function (data) {
                if (!response.ok) {
                    throw new Error(data.message || data.Message || 'The code was rejected.');
                }
                return data;
            });
        }).then(function () {
            setStatus('Device authorized. You can close this page.', 'success');
            codeInput.value = '';
        }).catch(function (error) {
            setStatus(error.message, 'error');
        });
    });

    loadProviders();
})();
