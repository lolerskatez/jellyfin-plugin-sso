(function () {
    'use strict';

    var root = document.getElementById('sso-admin-root');
    var path = window.location.pathname;
    var webIndex = path.indexOf('/web');
    var basePath = webIndex >= 0 ? path.substring(0, webIndex) : '';
    var apiBase = window.location.origin + basePath;
    var activeTab = 'status';
    var providers = [];
    var settings = null;
    var status = null;
    var pending = [];
    var links = [];
    var audit = [];
    var users = [];
    var editing = null;
    var notice = null;

    function getToken() {
        if (window.ApiClient && typeof window.ApiClient.accessToken === 'function') {
            var token = window.ApiClient.accessToken();
            if (token) return token;
        }

        try {
            var data = JSON.parse(localStorage.getItem('jellyfin_credentials') || '{}');
            var servers = Array.isArray(data.Servers) ? data.Servers : [];
            var server = servers
                .filter(function (item) { return item && item.AccessToken; })
                .sort(function (a, b) { return (b.DateLastAccessed || 0) - (a.DateLastAccessed || 0); })[0];
            return server ? server.AccessToken : '';
        } catch (e) {
            return '';
        }
    }

    function api(pathname, options) {
        options = options || {};
        var headers = Object.assign({ Accept: 'application/json; profile="CamelCase"' }, options.headers || {});
        if (options.body) {
            headers['Content-Type'] = 'application/json';
        }

        var token = getToken();
        if (token) {
            headers['X-Emby-Token'] = token;
            headers.Authorization = 'MediaBrowser Token="' + token + '"';
        }

        return fetch(apiBase + pathname, Object.assign({}, options, {
            headers: headers,
            credentials: 'same-origin'
        })).then(function (response) {
            if (response.status === 204) {
                return null;
            }

            return response.text().then(function (text) {
                var data = null;
                try {
                    data = text ? JSON.parse(text) : null;
                } catch (e) {
                    data = text;
                }

                if (!response.ok) {
                    var message = (data && (data.message || data.Message)) || text || ('HTTP ' + response.status);
                    throw new Error(message);
                }

                return data;
            });
        });
    }

    function get(object, name) {
        if (!object) return undefined;
        var lower = name.charAt(0).toLowerCase() + name.slice(1);
        var upper = name.charAt(0).toUpperCase() + name.slice(1);
        if (object[name] !== undefined) return object[name];
        if (object[lower] !== undefined) return object[lower];
        return object[upper];
    }

    function el(tag, props, children) {
        var node = document.createElement(tag);
        if (props) {
            Object.keys(props).forEach(function (key) {
                if (key === 'text') {
                    node.textContent = props[key];
                } else if (key === 'className') {
                    node.className = props[key];
                } else if (key === 'onclick') {
                    node.addEventListener('click', props[key]);
                } else if (props[key] !== undefined && props[key] !== null) {
                    node.setAttribute(key, props[key]);
                }
            });
        }

        (children || []).forEach(function (child) {
            if (child === null || child === undefined) return;
            node.appendChild(typeof child === 'string' ? document.createTextNode(child) : child);
        });

        return node;
    }

    function field(labelText, input) {
        return el('div', { className: 'ssoFormRow' }, [
            el('label', { text: labelText }),
            input
        ]);
    }

    function input(value, type) {
        return el('input', { type: type || 'text', value: value === undefined || value === null ? '' : value });
    }

    function checkbox(labelText, checked) {
        var box = el('input', { type: 'checkbox' });
        box.checked = !!checked;
        return el('label', { className: 'ssoCheck' }, [box, el('span', { text: labelText })]);
    }

    function select(value, options) {
        var node = el('select');
        options.forEach(function (option) {
            var item = el('option', { value: option.value, text: option.label });
            if (String(option.value) === String(value)) {
                item.selected = true;
            }
            node.appendChild(item);
        });
        return node;
    }

    function showNotice(message, kind) {
        notice = message ? { message: message, kind: kind || '' } : null;
        render();
    }

    function render() {
        root.replaceChildren();

        var header = el('div', { className: 'ssoAdminHeader' }, [
            el('div', {}, [
                el('h1', { text: 'Single Sign-On' }),
                el('p', { text: 'OIDC login, automatic user provisioning, and account linking for Jellyfin.' })
            ]),
            el('div', {}, [
                el('button', { className: 'ssoBtn secondary', text: 'Refresh', onclick: function () { loadAll(); } })
            ])
        ]);
        root.appendChild(header);

        var tabs = [
            ['status', 'Status'],
            ['providers', 'Providers'],
            ['settings', 'Settings'],
            ['pending', 'Pending'],
            ['links', 'Links'],
            ['audit', 'Audit']
        ];
        var tabBar = el('div', { className: 'ssoTabs' });
        tabs.forEach(function (tab) {
            tabBar.appendChild(el('button', {
                className: 'ssoTab' + (activeTab === tab[0] ? ' active' : ''),
                text: tab[1],
                onclick: function () {
                    activeTab = tab[0];
                    notice = null;
                    render();
                    loadTab();
                }
            }));
        });
        root.appendChild(tabBar);

        if (notice) {
            root.appendChild(el('div', { className: 'ssoNotice ' + notice.kind, text: notice.message }));
        }

        var panel = el('div', { className: 'ssoPanel active' });
        root.appendChild(panel);
        renderPanel(panel);
    }

    function renderPanel(panel) {
        if (activeTab === 'status') renderStatus(panel);
        if (activeTab === 'providers') renderProviders(panel);
        if (activeTab === 'settings') renderSettings(panel);
        if (activeTab === 'pending') renderPending(panel);
        if (activeTab === 'links') renderLinks(panel);
        if (activeTab === 'audit') renderAudit(panel);
    }

    function renderStatus(panel) {
        if (!status) {
            panel.appendChild(el('div', { className: 'ssoLoading', text: 'Loading status…' }));
            return;
        }

        var box = el('div', { className: 'ssoBox' }, [
            el('h2', { text: 'Setup status' }),
            stat('Jellyfin', get(status, 'jellyfinVersion')),
            stat('Plugin', get(status, 'pluginVersion')),
            stat('Public base URL', get(status, 'publicBaseUrl')),
            stat('Login page injection', get(status, 'injectionEnabled') ? 'Enabled' : 'Disabled'),
            stat('SSO-only login', get(status, 'ssoOnlyEnabled') ? 'Enabled' : 'Disabled'),
            stat('Password allowlist', String(get(status, 'passwordAllowlistCount'))),
            stat('Enabled providers', String(get(status, 'enabledProviders'))),
            stat('Linked identities', String(get(status, 'linkCount'))),
            stat('Pending requests', String(get(status, 'pendingCount')))
        ]);
        panel.appendChild(box);

        var intro = el('div', { className: 'ssoBox' }, [
            el('h3', { text: 'First-time setup' }),
            el('p', { className: 'ssoMuted', text: 'Add an Authentik OIDC provider, copy its redirect URI into Authentik, then restart nothing — the login button is injected automatically.' }),
            el('button', {
                className: 'ssoBtn',
                text: 'Add provider',
                onclick: function () {
                    activeTab = 'providers';
                    editing = {};
                    render();
                }
            })
        ]);
        panel.appendChild(intro);
    }

    function stat(label, value) {
        return el('div', { className: 'ssoStat' }, [
            el('span', { text: label }),
            el('strong', { text: value === undefined || value === null || value === '' ? '—' : String(value) })
        ]);
    }

    function renderProviders(panel) {
        if (editing) {
            renderProviderForm(panel);
            return;
        }

        var intro = el('div', { className: 'ssoBox' }, [
            el('h2', { text: 'OIDC providers' }),
            el('p', { className: 'ssoMuted', text: 'Each provider gets a redirect URI. Add that exact URI to the provider configuration.' }),
            el('button', {
                className: 'ssoBtn',
                text: 'Add provider',
                onclick: function () {
                    editing = {};
                    render();
                }
            })
        ]);
        panel.appendChild(intro);

        if (!providers.length) {
            panel.appendChild(el('div', { className: 'ssoBox' }, [
                el('p', { className: 'ssoMuted', text: 'No providers configured yet.' })
            ]));
            return;
        }

        providers.forEach(function (provider) {
            var slug = get(provider, 'slug');
            var enabled = get(provider, 'enabled');
            var redirect = (status && get(status, 'publicBaseUrl') ? get(status, 'publicBaseUrl') : apiBase) + '/sso/callback/' + slug;
            var postLogout = get(provider, 'postLogoutRedirectUri')
                || ((status && get(status, 'publicBaseUrl') ? get(status, 'publicBaseUrl') : apiBase) + '/web/index.html');

            var actions = el('div', { className: 'ssoFormActions' }, [
                el('button', {
                    className: 'ssoBtn secondary',
                    text: 'Edit',
                    onclick: function () {
                        editing = provider;
                        render();
                    }
                }),
                el('button', {
                    className: 'ssoBtn secondary',
                    text: 'Test discovery',
                    onclick: function () {
                        api('/sso/admin/providers/' + encodeURIComponent(slug) + '/test', { method: 'POST' })
                            .then(function (data) {
                                showNotice('Discovery succeeded. Issuer: ' + (get(data, 'issuer') || 'unknown'), 'success');
                            })
                            .catch(function (error) { showNotice(error.message, 'error'); });
                    }
                }),
                el('button', {
                    className: 'ssoBtn danger',
                    text: 'Delete',
                    onclick: function () {
                        if (!window.confirm('Delete provider ' + slug + '? Existing links will remain orphaned.')) return;
                        api('/sso/admin/providers/' + encodeURIComponent(slug), { method: 'DELETE' })
                            .then(function () { showNotice('Provider deleted.', 'success'); return loadProviders(); })
                            .catch(function (error) { showNotice(error.message, 'error'); });
                    }
                })
            ]);

            panel.appendChild(el('div', { className: 'ssoBox' }, [
                el('h3', {}, [
                    el('span', { text: get(provider, 'displayName') || slug }),
                    el('span', { className: 'ssoBadge' + (enabled ? '' : ' off'), text: enabled ? 'Enabled' : 'Disabled' })
                ]),
                el('p', { className: 'ssoMuted', text: 'Slug: ' + slug }),
                el('p', { className: 'ssoMuted', text: 'Authority: ' + (get(provider, 'authority') || get(provider, 'discoveryUrl') || '—') }),
                el('p', { className: 'ssoMuted', text: 'Redirect URI: ' + redirect }),
                el('p', { className: 'ssoMuted', text: 'Post-logout URI: ' + postLogout }),
                actions
            ]));
        });
    }

    function renderProviderForm(panel) {
        var p = editing || {};
        var isNew = !get(p, 'slug') || !providers.some(function (x) { return get(x, 'slug') === get(p, 'slug'); });

        var form = el('form', { className: 'ssoForm' });
        var slug = input(get(p, 'slug') || 'authentik');
        slug.readOnly = !isNew;
        var protocol = select(get(p, 'protocol') || 'oidc', [
            { value: 'oidc', label: 'OpenID Connect (OIDC)' }
        ]);
        var profile = select(get(p, 'profile') || 'generic', [
            { value: 'generic', label: 'Generic OIDC' },
            { value: 'authentik', label: 'Authentik' },
            { value: 'keycloak', label: 'Keycloak' },
            { value: 'authelia', label: 'Authelia' },
            { value: 'entra', label: 'Microsoft Entra ID' },
            { value: 'google', label: 'Google' }
        ]);
        var displayName = input(get(p, 'displayName') || 'Authentik');
        var buttonText = input(get(p, 'buttonText') || 'Sign in with Authentik');
        var authority = input(get(p, 'authority') || '');
        var discoveryUrl = input(get(p, 'discoveryUrl') || '');
        var issuer = input(get(p, 'issuer') || '');
        var clientId = input(get(p, 'clientId') || '');
        var clientSecret = input('', 'password');
        clientSecret.placeholder = get(p, 'hasClientSecret') ? 'Stored secret — leave blank to keep' : 'Client secret (optional for public clients)';
        var postLogoutRedirect = input(get(p, 'postLogoutRedirectUri') || '');
        postLogoutRedirect.placeholder = 'Optional; defaults to <public base>/web/index.html';
        var scopes = input(get(p, 'scopes') || 'openid profile email groups');
        var usernameClaim = input(get(p, 'usernameClaim') || 'preferred_username');
        var displayNameClaim = input(get(p, 'displayNameClaim') || 'name');
        var emailClaim = input(get(p, 'emailClaim') || 'email');
        var groupClaims = input((get(p, 'groupClaimPaths') || ['groups']).join(', '));
        var adminRoles = input((get(p, 'adminRoles') || []).join(', '));
        var requiredRoles = input((get(p, 'requiredRoles') || []).join(', '));
        var allowedDomains = input((get(p, 'allowedEmailDomains') || []).join(', '));
        var responseMode = select(get(p, 'responseMode') || 'query', [
            { value: 'query', label: 'query (recommended)' },
            { value: 'form_post', label: 'form_post' }
        ]);
        var tokenAuth = select(get(p, 'tokenEndpointAuthMethod') || 'client_secret_post', [
            { value: 'client_secret_post', label: 'client_secret_post' },
            { value: 'client_secret_basic', label: 'client_secret_basic' },
            { value: 'none', label: 'none (public + PKCE)' }
        ]);
        var conflict = select(get(p, 'usernameConflictPolicy') || 'Pending', [
            { value: 'Pending', label: 'Pending — admin approval' },
            { value: 'Reject', label: 'Reject' },
            { value: 'Suffix', label: 'Suffix — create alice-2' }
        ]);
        var autoProvision = select(get(p, 'autoProvision') === null || get(p, 'autoProvision') === undefined ? 'default' : String(get(p, 'autoProvision')), [
            { value: 'default', label: 'Use global setting' },
            { value: 'true', label: 'Auto-provision' },
            { value: 'false', label: 'Do not auto-provision' }
        ]);
        var requireApproval = select(get(p, 'requireApproval') === null || get(p, 'requireApproval') === undefined ? 'default' : String(get(p, 'requireApproval')), [
            { value: 'default', label: 'Use global setting' },
            { value: 'true', label: 'Require admin approval' },
            { value: 'false', label: 'No approval required' }
        ]);
        var syncRoles = select(get(p, 'syncRolesOnLogin') === null || get(p, 'syncRolesOnLogin') === undefined ? 'default' : String(get(p, 'syncRolesOnLogin')), [
            { value: 'default', label: 'Use global setting' },
            { value: 'true', label: 'Sync roles on login' },
            { value: 'false', label: 'Do not sync roles' }
        ]);
        var enabled = checkbox('Enabled', get(p, 'enabled') !== false);
        var usePkce = checkbox('Use PKCE', get(p, 'usePkce') !== false);
        var fetchUserInfo = checkbox('Fetch UserInfo', get(p, 'fetchUserInfo') !== false);
        var verifiedEmail = checkbox('Require verified email', !!get(p, 'requireVerifiedEmail'));
        var allowHttp = checkbox('Allow HTTP (development only)', !!get(p, 'allowHttp'));
        var symmetric = checkbox('Allow symmetric ID token signing', !!get(p, 'allowSymmetricSigning'));
        var adminProvisioning = checkbox('Allow new users to become administrators from claims', !!get(p, 'allowAdminProvisioning'));

        [
            field('Slug (URL-safe, permanent)', slug),
            field('Protocol', protocol),
            field('Provider profile', profile),
            field('Display name', displayName),
            field('Button text', buttonText),
            field('Authority / issuer URL', authority),
            field('Discovery URL override (optional)', discoveryUrl),
            field('Issuer override (optional)', issuer),
            field('Client ID', clientId),
            field('Client secret', clientSecret),
            field('Post-logout redirect URI (optional)', postLogoutRedirect),
            el('label', { className: 'ssoCheck' }, [
                (function () { var c = el('input', { type: 'checkbox' }); c.id = 'sso-clear-secret'; return c; })(),
                el('span', { text: 'Clear stored client secret' })
            ]),
            field('Scopes', scopes),
            field('Username claim', usernameClaim),
            field('Display name claim', displayNameClaim),
            field('Email claim', emailClaim),
            field('Group claim paths (comma separated)', groupClaims),
            field('Admin roles (comma separated)', adminRoles),
            field('Required roles (comma separated)', requiredRoles),
            field('Allowed email domains (comma separated)', allowedDomains),
            field('Response mode', responseMode),
            field('Token endpoint authentication', tokenAuth),
            field('Username conflict policy', conflict),
            field('Auto-provision', autoProvision),
            field('Approval', requireApproval),
            field('Role sync', syncRoles),
            enabled,
            usePkce,
            fetchUserInfo,
            verifiedEmail,
            allowHttp,
            symmetric,
            adminProvisioning
        ].forEach(function (node) { form.appendChild(node); });

        var save = el('button', { className: 'ssoBtn', text: 'Save provider' });
        save.type = 'submit';
        form.appendChild(el('div', { className: 'ssoFormActions' }, [
            save,
            el('button', {
                className: 'ssoBtn secondary',
                text: 'Cancel',
                type: 'button',
                onclick: function () { editing = null; render(); }
            })
        ]));

        form.addEventListener('submit', function (event) {
            event.preventDefault();
            var body = {
                slug: slug.value.trim().toLowerCase(),
                protocol: protocol.value,
                profile: profile.value,
                additionalAuthorizationParameters: get(p, 'additionalAuthorizationParameters') || [],
                displayName: displayName.value.trim(),
                buttonText: buttonText.value.trim(),
                enabled: enabled.querySelector('input').checked,
                displayOrder: get(p, 'displayOrder') || 0,
                iconUrl: get(p, 'iconUrl') || '',
                authority: authority.value.trim(),
                discoveryUrl: discoveryUrl.value.trim(),
                issuer: issuer.value.trim(),
                clientId: clientId.value.trim(),
                clientSecret: clientSecret.value,
                clearClientSecret: document.getElementById('sso-clear-secret').checked,
                postLogoutRedirectUri: postLogoutRedirect.value.trim(),
                scopes: scopes.value.trim(),
                usePkce: usePkce.querySelector('input').checked,
                fetchUserInfo: fetchUserInfo.querySelector('input').checked,
                responseMode: responseMode.value,
                tokenEndpointAuthMethod: tokenAuth.value,
                callbackUrlOverride: get(p, 'callbackUrlOverride') || '',
                usernameClaim: usernameClaim.value.trim(),
                displayNameClaim: displayNameClaim.value.trim(),
                emailClaim: emailClaim.value.trim(),
                groupClaimPaths: split(groupClaims.value),
                adminRoles: split(adminRoles.value),
                requiredRoles: split(requiredRoles.value),
                requireVerifiedEmail: verifiedEmail.querySelector('input').checked,
                allowedEmailDomains: split(allowedDomains.value),
                usernameConflictPolicy: conflict.value,
                autoProvision: triState(autoProvision.value),
                requireApproval: triState(requireApproval.value),
                syncRolesOnLogin: triState(syncRoles.value),
                publicBaseUrlOverride: get(p, 'publicBaseUrlOverride') || '',
                allowHttp: allowHttp.querySelector('input').checked,
                allowSymmetricSigning: symmetric.querySelector('input').checked,
                allowAdminProvisioning: adminProvisioning.querySelector('input').checked
            };

            var request = isNew
                ? api('/sso/admin/providers', { method: 'POST', body: JSON.stringify(body) })
                : api('/sso/admin/providers/' + encodeURIComponent(body.slug), { method: 'PUT', body: JSON.stringify(body) });

            request.then(function () {
                editing = null;
                showNotice('Provider saved.', 'success');
                return loadProviders();
            }).catch(function (error) { showNotice(error.message, 'error'); });
        });

        panel.appendChild(el('div', { className: 'ssoBox' }, [
            el('h2', { text: isNew ? 'Add provider' : 'Edit provider' }),
            form
        ]));
    }

    function renderSettings(panel) {
        if (!settings) {
            panel.appendChild(el('div', { className: 'ssoLoading', text: 'Loading settings…' }));
            return;
        }

        var publicBaseUrl = input(get(settings, 'publicBaseUrl') || '');
        var requireHttps = checkbox('Require HTTPS for OIDC endpoints', get(settings, 'requireHttps') !== false);
        var stateLifetime = input(get(settings, 'stateLifetimeMinutes') || 10, 'number');
        var ticketLifetime = input(get(settings, 'ticketLifetimeSeconds') || 90, 'number');
        var provisioning = get(settings, 'provisioning') || {};
        var roles = get(settings, 'roles') || {};
        var loginPage = get(settings, 'loginPage') || {};
        var ssoOnly = get(settings, 'ssoOnly') || {};

        var autoProvision = checkbox('Auto-provision new users', get(provisioning, 'autoProvisionNewUsers') !== false);
        var requireApproval = checkbox('Require administrator approval for new users', !!get(provisioning, 'requireAdminApproval'));
        var allowAdminProvisioning = checkbox('Allow claim-based administrator provisioning', !!get(provisioning, 'allowAdminProvisioning'));
        var hideNewUsers = checkbox('Hide new users from the login user picker', get(provisioning, 'hideNewUsers') !== false);
        var allowAllFolders = checkbox('Give new users access to all libraries', get(provisioning, 'allowAllFolders') !== false);
        var allowDownloads = checkbox('Allow downloads for new users', get(provisioning, 'allowDownloads') !== false);
        var allowRemote = checkbox('Allow remote access for new users', get(provisioning, 'allowRemoteAccess') !== false);
        var allowLiveTv = checkbox('Allow Live TV for new users', !!get(provisioning, 'allowLiveTv'));
        var syncRoles = checkbox('Sync roles on every login', !!get(provisioning, 'syncRolesOnEveryLogin'));
        var requireRole = checkbox('Require a role for access', !!get(roles, 'requireRoleForAccess'));
        var protectLastAdmin = checkbox('Protect the last administrator from demotion', get(roles, 'protectLastAdmin') !== false);
        var missingRole = select(get(provisioning, 'missingRoleBehavior') || 'Keep', [
            { value: 'Keep', label: 'Keep existing policy' },
            { value: 'Clear', label: 'Clear mapped permissions' },
            { value: 'Deny', label: 'Deny login' }
        ]);
        var templateId = input(get(provisioning, 'policyTemplateUserId') || '');
        templateId.placeholder = 'Optional Jellyfin user GUID';
        var maxButtons = input(get(loginPage, 'maxInlineButtons') || 3, 'number');
        var inject = checkbox('Inject the SSO login button into the web client', get(loginPage, 'injectLoginPage') !== false);

        var ssoOnlyEnabled = checkbox('Enforce SSO-only login', !!get(ssoOnly, 'enabled'));
        var blockProvisioned = checkbox('Block password login for auto-provisioned SSO users', get(ssoOnly, 'blockProvisionedUsers') !== false);
        var blockLinked = checkbox('Also block linked local users', !!get(ssoOnly, 'blockLinkedUsers'));
        var allowApiKey = checkbox('Allow API-key password verification for management apps', get(ssoOnly, 'allowApiKeyPasswordVerification') !== false);
        var hideLocal = checkbox('Hide local login on the web login page', get(ssoOnly, 'hideLocalLogin') !== false);
        var showLocalLink = checkbox('Show a “Use a local account” link', get(ssoOnly, 'showLocalAccountLink') !== false);
        var allowedUsers = input((get(ssoOnly, 'passwordLoginAllowedUsers') || []).join(', '));
        allowedUsers.placeholder = 'admin, another-local-user';
        var allowedUsersHelp = el('div', {
            className: 'ssoMuted ssoFieldHelp',
            text: 'Required to enable SSO-only login. Use an existing Jellyfin administrator; this is the break-glass account.'
        });
        var ssoOnlyHeading = el('h3', { className: 'ssoFormSection', text: 'SSO-only login' });

        var form = el('form', { className: 'ssoForm' });
        [
            field('Public base URL override', publicBaseUrl),
            field('State lifetime (minutes)', stateLifetime),
            field('Ticket lifetime (seconds)', ticketLifetime),
            requireHttps,
            autoProvision,
            requireApproval,
            allowAdminProvisioning,
            hideNewUsers,
            allowAllFolders,
            allowDownloads,
            allowRemote,
            allowLiveTv,
            syncRoles,
            field('Policy template user ID', templateId),
            field('Missing role behavior', missingRole),
            requireRole,
            protectLastAdmin,
            inject,
            field('Maximum inline login buttons', maxButtons),
            ssoOnlyHeading,
            ssoOnlyEnabled,
            blockProvisioned,
            blockLinked,
            allowApiKey,
            hideLocal,
            showLocalLink,
            field('Password login allowed for these users (comma separated)', allowedUsers),
            allowedUsersHelp
        ].forEach(function (node) { form.appendChild(node); });

        var save = el('button', { className: 'ssoBtn', text: 'Save settings' });
        save.type = 'submit';
        form.appendChild(el('div', { className: 'ssoFormActions' }, [save]));

        form.addEventListener('submit', function (event) {
            event.preventDefault();
            var allowedList = split(allowedUsers.value);
            if (ssoOnlyEnabled.querySelector('input').checked && allowedList.length === 0) {
                showNotice('Add at least one existing Jellyfin administrator to the password-login allowlist before enabling SSO-only login.', 'error');
                window.scrollTo(0, 0);
                allowedUsers.focus({ preventScroll: true });
                return;
            }

            var body = {
                publicBaseUrl: publicBaseUrl.value.trim(),
                requireHttps: requireHttps.querySelector('input').checked,
                stateLifetimeMinutes: Number(stateLifetime.value) || 10,
                ticketLifetimeSeconds: Number(ticketLifetime.value) || 90,
                provisioning: {
                    autoProvisionNewUsers: autoProvision.querySelector('input').checked,
                    requireAdminApproval: requireApproval.querySelector('input').checked,
                    policyTemplateUserId: templateId.value.trim() || null,
                    hideNewUsers: hideNewUsers.querySelector('input').checked,
                    allowDownloads: allowDownloads.querySelector('input').checked,
                    allowRemoteAccess: allowRemote.querySelector('input').checked,
                    allowAllFolders: allowAllFolders.querySelector('input').checked,
                    defaultEnabledFolders: get(provisioning, 'defaultEnabledFolders') || [],
                    allowLiveTv: allowLiveTv.querySelector('input').checked,
                    allowAdminProvisioning: allowAdminProvisioning.querySelector('input').checked,
                    usernameConflictPolicy: get(provisioning, 'usernameConflictPolicy') || 'Pending',
                    syncRolesOnEveryLogin: syncRoles.querySelector('input').checked,
                    missingRoleBehavior: missingRole.value,
                    revokeTokensOnUnlink: !!get(provisioning, 'revokeTokensOnUnlink'),
                    maxPendingRequestsPerIpPerHour: get(provisioning, 'maxPendingRequestsPerIpPerHour') || 10
                },
                roles: {
                    folderRoles: get(roles, 'folderRoles') || [],
                    liveTvRoles: get(roles, 'liveTvRoles') || [],
                    liveTvManagementRoles: get(roles, 'liveTvManagementRoles') || [],
                    requireRoleForAccess: requireRole.querySelector('input').checked,
                    requiredRoles: get(roles, 'requiredRoles') || [],
                    protectLastAdmin: protectLastAdmin.querySelector('input').checked
                },
                loginPage: {
                    injectLoginPage: inject.querySelector('input').checked,
                    maxInlineButtons: Number(maxButtons.value) || 3,
                    showProviderButtons: get(loginPage, 'showProviderButtons') !== false
                },
                ssoOnly: {
                    enabled: ssoOnlyEnabled.querySelector('input').checked,
                    blockProvisionedUsers: blockProvisioned.querySelector('input').checked,
                    blockLinkedUsers: blockLinked.querySelector('input').checked,
                    allowApiKeyPasswordVerification: allowApiKey.querySelector('input').checked,
                    hideLocalLogin: hideLocal.querySelector('input').checked,
                    showLocalAccountLink: showLocalLink.querySelector('input').checked,
                    passwordLoginAllowedUsers: allowedList
                }
            };

            api('/sso/admin/settings', { method: 'PUT', body: JSON.stringify(body) })
                .then(function () {
                    showNotice('Settings saved.', 'success');
                    window.scrollTo(0, 0);
                    return loadSettings();
                })
                .catch(function (error) {
                    showNotice(error.message, 'error');
                    window.scrollTo(0, 0);
                    return loadSettings();
                });
        });

        panel.appendChild(el('div', { className: 'ssoBox' }, [el('h2', { text: 'Global settings' }), form]));

        var snippet = providers.map(function (provider) {
            var slug = get(provider, 'slug');
            var label = escapeHtml(get(provider, 'buttonText') || ('Sign in with ' + (get(provider, 'displayName') || slug)));
            return '<form action="/sso/start/' + encodeURIComponent(slug) + '"><button class="raised block emby-button button-submit">'
                + label + '</button></form>';
        }).join('\n');
        var fallbackText = el('textarea', { rows: '6', readonly: 'readonly' });
        fallbackText.value = snippet || 'Add a provider first.';
        panel.appendChild(el('div', { className: 'ssoBox' }, [
            el('h2', { text: 'Login page fallback' }),
            el('p', { className: 'ssoMuted', text: 'If the web client is hosted externally and automatic injection is unavailable, paste this into Jellyfin Branding → Login disclaimer. Replace the leading slash with your path base if needed.' }),
            fallbackText
        ]));
    }

    function renderPending(panel) {
        if (!pending.length) {
            panel.appendChild(el('div', { className: 'ssoBox' }, [
                el('h2', { text: 'Pending requests' }),
                el('p', { className: 'ssoMuted', text: 'No accounts are waiting for approval.' })
            ]));
            return;
        }

        var box = el('div', { className: 'ssoBox' }, [el('h2', { text: 'Pending requests' })]);
        var table = el('table', { className: 'ssoTable' });
        table.appendChild(el('thead', {}, [
            el('tr', {}, ['Provider', 'Suggested user', 'Email', 'Groups', 'Reason', 'Action'].map(function (h) { return el('th', { text: h }); }))
        ]));
        var body = el('tbody');

        pending.forEach(function (request) {
            var id = get(request, 'id');
            var userSelect = el('select');
            userSelect.appendChild(el('option', { value: '', text: 'Select existing user…' }));
            users.forEach(function (user) {
                userSelect.appendChild(el('option', { value: get(user, 'id'), text: get(user, 'name') }));
            });

            var create = el('button', {
                className: 'ssoBtn',
                text: 'Create user',
                onclick: function () {
                    approve(id, null, true);
                }
            });
            var link = el('button', {
                className: 'ssoBtn secondary',
                text: 'Link selected',
                onclick: function () {
                    if (!userSelect.value) {
                        showNotice('Select an existing user first.', 'error');
                        return;
                    }
                    approve(id, userSelect.value, false);
                }
            });
            var deny = el('button', {
                className: 'ssoBtn danger',
                text: 'Deny',
                onclick: function () {
                    api('/sso/admin/pending/' + encodeURIComponent(id) + '/deny', { method: 'POST' })
                        .then(function () { showNotice('Request denied.', 'success'); return loadPending(); })
                        .catch(function (error) { showNotice(error.message, 'error'); });
                }
            });

            body.appendChild(el('tr', {}, [
                el('td', { text: get(request, 'providerId') }),
                el('td', { text: get(request, 'username') || '—' }),
                el('td', { text: get(request, 'email') || '—' }),
                el('td', { text: (get(request, 'groups') || []).join(', ') || '—' }),
                el('td', { text: get(request, 'reason') || '—' }),
                el('td', {}, [
                    userSelect,
                    el('div', { className: 'ssoFormActions' }, [create, link, deny])
                ])
            ]));
        });

        table.appendChild(body);
        box.appendChild(el('div', { className: 'ssoTableWrap' }, [table]));
        panel.appendChild(box);
    }

    function approve(id, userId, createUser) {
        api('/sso/admin/pending/' + encodeURIComponent(id) + '/approve', {
            method: 'POST',
            body: JSON.stringify({ userId: userId, createUser: createUser })
        }).then(function () {
            showNotice('Request approved.', 'success');
            return loadPending();
        }).catch(function (error) {
            showNotice(error.message, 'error');
        });
    }

    function renderLinks(panel) {
        if (!links.length) {
            panel.appendChild(el('div', { className: 'ssoBox' }, [
                el('h2', { text: 'Linked identities' }),
                el('p', { className: 'ssoMuted', text: 'No SSO identities are linked yet.' })
            ]));
            return;
        }

        var table = el('table', { className: 'ssoTable' }, [
            el('thead', {}, [
                el('tr', {}, ['Provider', 'Jellyfin user', 'Email', 'Last login', 'Source', ''].map(function (h) { return el('th', { text: h }); }))
            ])
        ]);
        var body = el('tbody');
        links.forEach(function (link) {
            var provider = get(link, 'providerId');
            var userId = get(link, 'jellyfinUserId');
            body.appendChild(el('tr', {}, [
                el('td', { text: provider }),
                el('td', { text: get(link, 'username') || userId }),
                el('td', { text: get(link, 'email') || '—' }),
                el('td', { text: get(link, 'lastLoginAt') ? new Date(get(link, 'lastLoginAt')).toLocaleString() : '—' }),
                el('td', { text: get(link, 'source') || '—' }),
                el('td', {}, [
                    el('button', {
                        className: 'ssoBtn danger',
                        text: 'Unlink',
                        onclick: function () {
                            if (!window.confirm('Remove this link?')) return;
                            api('/sso/admin/links/' + encodeURIComponent(provider) + '/' + encodeURIComponent(userId), { method: 'DELETE' })
                                .then(function () { showNotice('Link removed.', 'success'); return loadLinks(); })
                                .catch(function (error) { showNotice(error.message, 'error'); });
                        }
                    })
                ])
            ]));
        });
        table.appendChild(body);

        panel.appendChild(el('div', { className: 'ssoBox' }, [
            el('h2', { text: 'Linked identities' }),
            el('div', { className: 'ssoTableWrap' }, [table])
        ]));
    }

    function renderAudit(panel) {
        if (!audit.length) {
            panel.appendChild(el('div', { className: 'ssoBox' }, [
                el('h2', { text: 'Audit log' }),
                el('p', { className: 'ssoMuted', text: 'No audit events yet.' })
            ]));
            return;
        }

        var table = el('table', { className: 'ssoTable' }, [
            el('thead', {}, [
                el('tr', {}, ['Time', 'Type', 'Provider', 'User', 'Outcome', 'Detail'].map(function (h) { return el('th', { text: h }); }))
            ])
        ]);
        var body = el('tbody');
        audit.forEach(function (event) {
            body.appendChild(el('tr', {}, [
                el('td', { text: get(event, 'timestamp') ? new Date(get(event, 'timestamp')).toLocaleString() : '—' }),
                el('td', { text: get(event, 'type') || '—' }),
                el('td', { text: get(event, 'providerId') || '—' }),
                el('td', { text: get(event, 'username') || get(event, 'userId') || '—' }),
                el('td', { text: get(event, 'outcome') || '—' }),
                el('td', { text: get(event, 'detail') || '—' })
            ]));
        });
        table.appendChild(body);

        panel.appendChild(el('div', { className: 'ssoBox' }, [
            el('h2', { text: 'Audit log' }),
            el('div', { className: 'ssoTableWrap' }, [table])
        ]));
    }

    function split(value) {
        return (value || '').split(',').map(function (item) { return item.trim(); }).filter(Boolean);
    }

    function escapeHtml(value) {
        return String(value).replace(/[&<>"']/g, function (character) {
            return {
                '&': '&amp;',
                '<': '&lt;',
                '>': '&gt;',
                '"': '&quot;',
                "'": '&#39;'
            }[character];
        });
    }

    function triState(value) {
        if (value === 'true') return true;
        if (value === 'false') return false;
        return null;
    }

    function loadAll() {
        Promise.allSettled([loadStatus(), loadProviders()]).then(function () {
            render();
            loadTab();
        });
    }

    function loadTab() {
        if (activeTab === 'settings' && !settings) loadSettings();
        if (activeTab === 'pending') loadPending();
        if (activeTab === 'links') loadLinks();
        if (activeTab === 'audit') loadAudit();
    }

    function loadStatus() {
        return api('/sso/admin/status').then(function (data) {
            status = data;
        }).catch(function (error) {
            showNotice(error.message, 'error');
        });
    }

    function loadProviders() {
        return api('/sso/admin/providers').then(function (data) {
            providers = Array.isArray(data) ? data : [];
            render();
        }).catch(function (error) {
            showNotice(error.message, 'error');
            render();
        });
    }

    function loadSettings() {
        return api('/sso/admin/settings').then(function (data) {
            settings = data;
            render();
        }).catch(function (error) {
            showNotice(error.message, 'error');
        });
    }

    function loadPending() {
        return Promise.all([api('/sso/admin/pending'), api('/sso/admin/users')]).then(function (results) {
            pending = Array.isArray(results[0]) ? results[0] : [];
            users = Array.isArray(results[1]) ? results[1] : [];
            render();
        }).catch(function (error) {
            showNotice(error.message, 'error');
        });
    }

    function loadLinks() {
        return api('/sso/admin/links').then(function (data) {
            links = Array.isArray(data) ? data : [];
            render();
        }).catch(function (error) {
            showNotice(error.message, 'error');
        });
    }

    function loadAudit() {
        return api('/sso/admin/audit?limit=200').then(function (data) {
            audit = Array.isArray(data) ? data : [];
            render();
        }).catch(function (error) {
            showNotice(error.message, 'error');
        });
    }

    loadAll();
})();
