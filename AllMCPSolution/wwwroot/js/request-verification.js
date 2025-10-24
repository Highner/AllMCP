(() => {
    const headerName = 'RequestVerificationToken';
    const safeMethods = new Set(['GET', 'HEAD', 'OPTIONS', 'TRACE']);
    let cachedToken = null;

    const updateCachedToken = (value) => {
        const trimmed = typeof value === 'string' ? value.trim() : '';
        if (!trimmed) {
            return null;
        }

        if (cachedToken !== trimmed) {
            cachedToken = trimmed;
        }

        return cachedToken;
    };

    const findTokenInDom = () => {
        const inputs = document.querySelectorAll('input[name="__RequestVerificationToken"]');
        for (const input of inputs) {
            if (input instanceof HTMLInputElement) {
                const next = updateCachedToken(input.value);
                if (next) {
                    return next;
                }
            }
        }

        return cachedToken;
    };

    const getRequestVerificationToken = () => {
        const token = findTokenInDom();
        if (token) {
            return token;
        }

        return cachedToken;
    };

    const ensureFormToken = (form) => {
        if (!(form instanceof HTMLFormElement)) {
            return;
        }

        const method = (form.method || 'GET').toUpperCase();
        if (safeMethods.has(method)) {
            return;
        }

        if (form.querySelector('input[name="__RequestVerificationToken"]')) {
            return;
        }

        const token = getRequestVerificationToken();
        if (!token) {
            return;
        }

        const hidden = document.createElement('input');
        hidden.type = 'hidden';
        hidden.name = '__RequestVerificationToken';
        hidden.value = token;
        form.appendChild(hidden);
    };

    document.addEventListener('submit', (event) => {
        if (!(event.target instanceof HTMLFormElement)) {
            return;
        }

        ensureFormToken(event.target);
    }, true);

    const originalFetch = window.fetch.bind(window);

    const resolveRequestMethod = (input, init) => {
        if (init?.method) {
            return String(init.method).toUpperCase();
        }

        if (input instanceof Request && input.method) {
            return String(input.method).toUpperCase();
        }

        return 'GET';
    };

    const resolveRequestHeaders = (input, init) => {
        if (init?.headers) {
            return new Headers(init.headers);
        }

        if (input instanceof Request) {
            return new Headers(input.headers);
        }

        return new Headers();
    };

    const resolveRequestCredentials = (input, init) => {
        if (init && typeof init.credentials !== 'undefined') {
            return init.credentials;
        }

        if (input instanceof Request) {
            return input.credentials;
        }

        return undefined;
    };

    window.fetch = (input, init) => {
        const method = resolveRequestMethod(input, init);
        const headers = resolveRequestHeaders(input, init);
        const originalCredentials = resolveRequestCredentials(input, init);
        let credentials = originalCredentials;

        if (!safeMethods.has(method)) {
            if (!headers.has(headerName)) {
                const token = getRequestVerificationToken();
                if (token) {
                    headers.set(headerName, token);
                }
            }

            if (credentials === undefined || credentials === 'same-origin' || credentials === 'include') {
                credentials = 'same-origin';
            }
        } else if (credentials === undefined) {
            credentials = 'same-origin';
        }

        const nextInit = init ? { ...init } : {};
        nextInit.method = method;
        nextInit.headers = headers;
        if (credentials !== undefined) {
            nextInit.credentials = credentials;
        }

        if (input instanceof Request) {
            return originalFetch(input, nextInit);
        }

        return originalFetch(input, nextInit);
    };

    const observer = new MutationObserver(() => {
        findTokenInDom();
    });

    observer.observe(document.documentElement, { childList: true, subtree: true });
})();
