(function () {
    'use strict';

    var webIndex = window.location.pathname.indexOf('/web');
    var basePath = webIndex >= 0 ? window.location.pathname.substring(0, webIndex) : '';
    var apiBase = window.location.origin + basePath;
    var redirecting = false;

    function readJson(key) {
        try {
            var raw = localStorage.getItem(key);
            return raw ? JSON.parse(raw) : null;
        } catch (e) {
            return null;
        }
    }

    function getSsoSession() {
        var session = readJson('sso_session');
        if (!session || !session.provider || !session.serverId || !session.userId) {
            return null;
        }
        return session;
    }

    function getCurrentServer(session) {
        var credentials = readJson('jellyfin_credentials');
        var servers = credentials && Array.isArray(credentials.Servers) ? credentials.Servers : [];
        return servers.find(function (server) {
            return server
                && server.AccessToken
                && server.Id === session.serverId
                && server.UserId === session.userId;
        }) || null;
    }

    function beginLogout(session, server) {
        if (redirecting) {
            return;
        }
        redirecting = true;

        var token = server.AccessToken;
        try {
            localStorage.removeItem('jellyfin_credentials');
            localStorage.removeItem('sso_session');
        } catch (e) {
            // Continue to the identity-provider logout even if storage is unavailable.
        }

        if (token) {
            fetch(apiBase + '/Sessions/Logout', {
                method: 'POST',
                headers: {
                    Authorization: 'MediaBrowser Token="' + token + '"'
                },
                keepalive: true
            }).catch(function () {
                // The identity-provider logout still proceeds.
            });
        }

        window.location.assign(apiBase + '/sso/logout?provider=' + encodeURIComponent(session.provider));
    }

    function isLogoutClick(target) {
        if (!target || !target.closest) {
            return false;
        }

        if (target.closest('.btnLogout')) {
            return true;
        }

        var icon = target.closest('[data-testid="LogoutIcon"]');
        if (icon) {
            return true;
        }

        var menuItem = target.closest('li');
        return !!(menuItem && menuItem.querySelector('[data-testid="LogoutIcon"]'));
    }

    document.addEventListener('click', function (event) {
        if (!isLogoutClick(event.target)) {
            return;
        }

        var session = getSsoSession();
        if (!session) {
            return;
        }

        var server = getCurrentServer(session);
        if (!server) {
            return;
        }

        event.preventDefault();
        event.stopPropagation();
        beginLogout(session, server);
    }, true);
})();
