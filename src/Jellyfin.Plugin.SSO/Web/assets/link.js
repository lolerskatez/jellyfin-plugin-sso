(function () {
    'use strict';

    var baseMeta = document.querySelector('meta[name="sso-base"]');
    var basePath = baseMeta ? (baseMeta.getAttribute('content') || '') : '';
    var apiBase = window.location.origin + basePath;
    var status = document.getElementById('sso-status');
    var linksHost = document.getElementById('sso-links');
    var providerHost = document.getElementById('sso-provider-actions');
    var signedOut = document.getElementById('sso-signed-out');

    function credentials() {
        try {
            return JSON.parse(localStorage.getItem('jellyfin_credentials') || '{}');
        } catch (e) {
            return {};
        }
    }

    function currentServer() {
        var data = credentials();
        var servers = Array.isArray(data.Servers) ? data.Servers : [];
        return servers
            .filter(function (server) { return server && server.AccessToken && server.UserId; })
            .sort(function (a, b) { return (b.DateLastAccessed || 0) - (a.DateLastAccessed || 0); })[0];
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

    function renderLinks(links) {
        linksHost.replaceChildren();

        if (!links.length) {
            var empty = document.createElement('p');
            empty.className = 'ssoMuted';
            empty.textContent = 'No SSO accounts are linked yet.';
            linksHost.appendChild(empty);
            return;
        }

        var list = document.createElement('ul');
        list.className = 'ssoLinks';

        links.forEach(function (link) {
            var item = document.createElement('li');
            var text = document.createElement('div');
            var title = document.createElement('strong');
            title.textContent = link.providerId;
            text.appendChild(title);

            var meta = document.createElement('div');
            meta.className = 'ssoMuted';
            meta.textContent = [link.username, link.email].filter(Boolean).join(' · ') || 'Linked SSO identity';
            text.appendChild(meta);

            var remove = document.createElement('button');
            remove.type = 'button';
            remove.className = 'ssoButton danger';
            remove.textContent = 'Unlink';
            remove.addEventListener('click', function () {
                if (!window.confirm('Unlink ' + link.providerId + ' from this Jellyfin account?')) {
                    return;
                }

                var server = currentServer();
                fetch(apiBase + '/sso/me/' + encodeURIComponent(link.providerId), {
                    method: 'DELETE',
                    credentials: 'same-origin',
                    headers: headers(server.AccessToken)
                }).then(function (response) {
                    if (!response.ok && response.status !== 404) {
                        throw new Error('Unable to unlink');
                    }
                    load();
                }).catch(function (error) {
                    setStatus(error.message, 'error');
                });
            });

            item.appendChild(text);
            item.appendChild(remove);
            list.appendChild(item);
        });

        linksHost.appendChild(list);
    }

    function renderProviders(providers, links) {
        providerHost.replaceChildren();
        var linked = new Set(links.map(function (link) { return link.providerId.toLowerCase(); }));
        var available = providers.filter(function (provider) {
            return !linked.has(provider.slug.toLowerCase());
        });

        if (!available.length) {
            return;
        }

        var heading = document.createElement('p');
        heading.textContent = 'Link another provider:';
        providerHost.appendChild(heading);

        available.forEach(function (provider) {
            var button = document.createElement('button');
            button.type = 'button';
            button.className = 'ssoButton';
            button.textContent = 'Link ' + provider.displayName;
            button.addEventListener('click', function () {
                var server = currentServer();
                button.disabled = true;
                fetch(apiBase + '/sso/link/start', {
                    method: 'POST',
                    credentials: 'same-origin',
                    headers: headers(server.AccessToken),
                    body: JSON.stringify({ provider: provider.slug })
                }).then(function (response) {
                    return response.json().then(function (data) {
                        if (!response.ok) {
                            throw new Error(data.message || data.Message || 'Unable to start linking');
                        }
                        return data;
                    });
                }).then(function (data) {
                    window.location.href = data.url || data.Url;
                }).catch(function (error) {
                    button.disabled = false;
                    setStatus(error.message, 'error');
                });
            });
            providerHost.appendChild(button);
        });
    }

    function load() {
        var server = currentServer();
        if (!server) {
            signedOut.hidden = false;
            linksHost.replaceChildren();
            providerHost.replaceChildren();
            return;
        }

        signedOut.hidden = true;

        fetch(apiBase + '/sso/me', {
            credentials: 'same-origin',
            headers: headers(server.AccessToken)
        }).then(function (response) {
            if (response.status === 401) {
                throw new Error('Your Jellyfin session has expired. Sign in again.');
            }
            if (!response.ok) {
                throw new Error('Unable to load linked accounts.');
            }
            return response.json();
        }).then(function (links) {
            renderLinks(links);

            return fetch(apiBase + '/sso/providers', {
                credentials: 'same-origin',
                headers: { Accept: 'application/json; profile="CamelCase"' }
            }).then(function (response) {
                return response.ok ? response.json() : { providers: [] };
            }).then(function (data) {
                renderProviders(Array.isArray(data.providers) ? data.providers : [], links);
            });
        }).catch(function (error) {
            setStatus(error.message, 'error');
        });
    }

    var params = new URLSearchParams(window.location.search);
    var state = params.get('status');
    if (state === 'linked') {
        setStatus('SSO account linked.', 'success');
    } else if (state === 'error') {
        setStatus('Unable to link that SSO account. It may already be linked to another Jellyfin user.', 'error');
    }

    load();
})();
