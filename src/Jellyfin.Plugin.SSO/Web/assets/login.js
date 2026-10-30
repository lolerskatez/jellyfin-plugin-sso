(function () {
    'use strict';

    var webIndex = window.location.pathname.indexOf('/web');
    var basePath = webIndex >= 0 ? window.location.pathname.substring(0, webIndex) : '';
    var apiBase = window.location.origin + basePath;
    var providers = null;
    var maxInlineButtons = 3;
    var hideLocalLogin = false;
    var showLocalAccountLink = false;
    var localLoginRequested = false;
    var localLoginStyleId = 'sso-hide-local-login';
    var observer = null;

    function getReturnUrl() {
        var hash = window.location.hash || '';
        var queryIndex = hash.indexOf('?');
        if (queryIndex >= 0) {
            try {
                var params = new URLSearchParams(hash.substring(queryIndex + 1));
                var target = params.get('url');
                if (target) {
                    return target;
                }
            } catch (e) {
                // Ignore malformed login URLs.
            }
        }

        return '/web/index.html#/home';
    }

    function startLogin(provider) {
        var url = apiBase + '/sso/start/' + encodeURIComponent(provider.slug)
            + '?returnUrl=' + encodeURIComponent(getReturnUrl());
        window.location.href = url;
    }

    function buildButton(provider, primary) {
        var button = document.createElement('button');
        button.type = 'button';
        button.setAttribute('is', 'emby-button');
        button.className = 'raised block emby-button ssoProviderButton'
            + (primary ? ' button-submit' : '');
        button.setAttribute('aria-label', provider.buttonText || ('Sign in with ' + provider.displayName));
        button.addEventListener('click', function () {
            startLogin(provider);
        });

        var label = document.createElement('span');
        label.className = 'ssoProviderLabel';

        if (provider.iconUrl) {
            var icon = document.createElement('img');
            icon.className = 'ssoProviderIcon';
            icon.src = provider.iconUrl;
            icon.alt = '';
            icon.setAttribute('aria-hidden', 'true');
            label.appendChild(icon);
        }

        var text = document.createElement('span');
        text.textContent = provider.buttonText || ('Sign in with ' + provider.displayName);
        label.appendChild(text);
        button.appendChild(label);
        return button;
    }

    function setLocalLoginHidden(hidden) {
        var style = document.getElementById(localLoginStyleId);
        if (hidden && !style) {
            style = document.createElement('style');
            style.id = localLoginStyleId;
            style.textContent = '.manualLoginForm, .btnManual, .btnForgotPassword, #divUsers { display: none !important; }';
            document.head.appendChild(style);
        } else if (!hidden && style) {
            style.remove();
        }
    }

    function addLocalAccountLink(section) {
        if (!showLocalAccountLink || section.querySelector('.ssoLocalAccountLink')) {
            return;
        }

        var link = document.createElement('button');
        link.type = 'button';
        link.className = 'ssoLocalAccountLink';
        link.textContent = 'Use a local account';
        link.addEventListener('click', function () {
            localLoginRequested = true;
            setLocalLoginHidden(false);
            var manual = document.querySelector('.btnManual');
            if (manual) {
                manual.click();
            }
            link.remove();
        });
        section.appendChild(link);
    }

    function applyLocalLoginMode(section) {
        var hide = hideLocalLogin && !localLoginRequested && providers && providers.length > 0;
        setLocalLoginHidden(hide);
        if (!section) {
            return;
        }

        var existing = section.querySelector('.ssoLocalAccountLink');
        if (hide && showLocalAccountLink) {
            addLocalAccountLink(section);
        } else if (existing) {
            existing.remove();
        }
    }

    function render(container) {
        if (!providers || providers.length === 0) {
            setLocalLoginHidden(false);
            return;
        }

        var signature = providers.map(function (p) {
            return p.slug + '|' + p.buttonText + '|' + (p.iconUrl || '');
        }).join(';') + '|' + hideLocalLogin + '|' + showLocalAccountLink + '|' + localLoginRequested;

        var section = container.querySelector('.ssoLoginSection');
        if (section && section.getAttribute('data-signature') === signature) {
            applyLocalLoginMode(section);
            return;
        }

        if (!section) {
            section = document.createElement('div');
            section.className = 'ssoLoginSection';
            container.insertBefore(section, container.firstChild);
        }

        section.setAttribute('data-signature', signature);
        section.replaceChildren();

        var heading = document.createElement('div');
        heading.className = 'ssoLoginHeading';
        heading.textContent = 'or';
        section.appendChild(heading);

        var limit = Math.max(1, maxInlineButtons || 3);
        var visible = providers.length <= limit ? providers : providers.slice(0, limit);

        visible.forEach(function (provider, index) {
            section.appendChild(buildButton(provider, providers.length === 1 && index === 0));
        });

        if (providers.length > visible.length) {
            var chooser = document.createElement('select');
            chooser.className = 'ssoProviderChooser';
            chooser.setAttribute('aria-label', 'More sign-in options');
            var placeholder = document.createElement('option');
            placeholder.value = '';
            placeholder.textContent = 'More sign-in options…';
            chooser.appendChild(placeholder);

            providers.slice(visible.length).forEach(function (provider) {
                var option = document.createElement('option');
                option.value = provider.slug;
                option.textContent = provider.buttonText || ('Sign in with ' + provider.displayName);
                chooser.appendChild(option);
            });

            chooser.addEventListener('change', function () {
                var selected = providers.find(function (p) {
                    return p.slug === chooser.value;
                });
                if (selected) {
                    startLogin(selected);
                }
            });
            section.appendChild(chooser);
        }

        applyLocalLoginMode(section);
    }

    function refresh() {
        var loginPage = document.getElementById('loginPage');
        if (!loginPage) {
            return;
        }

        var container = loginPage.querySelector('.readOnlyContent');
        if (!container) {
            return;
        }

        render(container);
    }

    function loadProviders() {
        fetch(apiBase + '/sso/providers', {
            credentials: 'same-origin',
            headers: { Accept: 'application/json; profile="CamelCase"' }
        }).then(function (response) {
            if (!response.ok) {
                throw new Error('SSO provider request failed: ' + response.status);
            }
            return response.json();
        }).then(function (data) {
            providers = Array.isArray(data.providers) ? data.providers : [];
            maxInlineButtons = data.maxInlineButtons || 3;
            hideLocalLogin = data.hideLocalLogin === true;
            showLocalAccountLink = data.showLocalAccountLink === true;
            if (data.showProviderButtons === false) {
                providers = [];
            }
            refresh();
        }).catch(function (error) {
            console.warn('[SSO] Unable to load providers', error);
        });
    }

    function start() {
        if (observer) {
            return;
        }

        observer = new MutationObserver(function () {
            refresh();
        });

        if (document.body) {
            observer.observe(document.body, { childList: true, subtree: true });
        }

        document.addEventListener('viewshow', refresh);
        window.addEventListener('hashchange', refresh);
        loadProviders();
        refresh();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', start);
    } else {
        start();
    }
})();
