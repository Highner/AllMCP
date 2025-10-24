(() => {
    const headerName = 'RequestVerificationToken';
    const safeMethods = new Set(['GET', 'HEAD', 'OPTIONS', 'TRACE']);
    let cachedToken = null;

    const findTokenInDom = () => {
        const inputs = document.querySelectorAll('input[name="__RequestVerificationToken"]');
        for (const input of inputs) {
            if (input instanceof HTMLInputElement) {
                const value = input.value?.trim();
                if (value) {
                    cachedToken = value;
                    return value;
                }
            }
        }
        return null;
    };

    const getRequestVerificationToken = () => {
        if (cachedToken) {
            return cachedToken;
        }

        return findTokenInDom();
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

    window.fetch = (input, init) => {
        const request = new Request(input, init);
        const method = (request.method || 'GET').toUpperCase();
        let headers = new Headers(request.headers);
        let credentials = request.credentials;

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
        }
        else if (credentials === undefined) {
            credentials = 'same-origin';
        }

        const cloned = new Request(request, {
            headers,
            credentials,
        });

        return originalFetch(cloned);
    };

    const observer = new MutationObserver(() => {
        if (!cachedToken) {
            findTokenInDom();
        }
    });

    observer.observe(document.documentElement, { childList: true, subtree: true });
})();
