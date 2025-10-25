(function () {
    'use strict';

    const state = {
        initialized: false,
        hasElements: false,
        overlay: null,
        popover: null,
        form: null,
        wineSearch: null,
        wineIdInput: null,
        wineResults: null,
        summary: null,
        vintageInput: null,
        wishlistSelect: null,
        createToggle: null,
        createField: null,
        createInput: null,
        errorElement: null,
        cancelButton: null,
        closeButton: null,
        submitButton: null,
        open: false,
        pending: false,
        wishlists: [],
        wishlistsLoaded: false,
        createMode: false,
        context: null,
        wineOptions: [],
        selectedWine: null,
        searchTimeoutId: null,
        searchController: null,
        preferredWishlistId: ''
    };

    function onReady(callback) {
        if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', callback, { once: true });
        } else {
            callback();
        }
    }

    function initialize() {
        if (state.initialized) {
            return;
        }

        state.initialized = true;
        state.overlay = document.getElementById('wishlist-overlay');
        state.popover = document.getElementById('wishlist-popover');

        if (!state.overlay || !state.popover) {
            state.hasElements = false;
            return;
        }

        state.hasElements = true;
        state.form = state.popover.querySelector('.wishlist-form');
        state.wineSearch = state.popover.querySelector('.wishlist-wine-search');
        state.wineIdInput = state.popover.querySelector('.wishlist-wine-id');
        state.wineResults = state.popover.querySelector('.wishlist-wine-results');
        state.summary = state.popover.querySelector('.wishlist-summary');
        state.vintageInput = state.popover.querySelector('.wishlist-vintage');
        state.wishlistSelect = state.popover.querySelector('.wishlist-select');
        state.createToggle = state.popover.querySelector('.wishlist-create-toggle');
        state.createField = state.popover.querySelector('.wishlist-create-field');
        state.createInput = state.popover.querySelector('.wishlist-name');
        state.errorElement = state.popover.querySelector('.wishlist-error');
        state.cancelButton = state.popover.querySelector('.wishlist-cancel');
        state.closeButton = state.popover.querySelector('[data-wishlist-close]');
        state.submitButton = state.popover.querySelector('.wishlist-submit');

        state.overlay.addEventListener('click', event => {
            if (event.target === state.overlay) {
                closePopover();
            }
        });

        document.addEventListener('keydown', event => {
            if (event.key === 'Escape' && state.open) {
                event.preventDefault();
                closePopover();
            }
        });

        state.cancelButton?.addEventListener('click', () => {
            closePopover();
        });

        state.closeButton?.addEventListener('click', () => {
            closePopover();
        });

        state.form?.addEventListener('submit', handleSubmit);
        state.wineSearch?.addEventListener('input', handleWineSearchInput);
        state.wineSearch?.addEventListener('focus', () => {
            if (state.wineOptions.length > 0) {
                setWineResultsVisible(true);
            }
        });
        state.wishlistSelect?.addEventListener('change', () => {
            clearError();
        });

        state.createToggle?.addEventListener('click', toggleCreateMode);
        state.createInput?.addEventListener('input', () => {
            clearError();
        });
    }

    function toggleCreateMode() {
        setCreateMode(!state.createMode);
    }

    function setCreateMode(enable) {
        state.createMode = Boolean(enable);
        if (!state.createField || !state.createToggle || !state.wishlistSelect) {
            return;
        }

        if (state.createMode) {
            state.createField.hidden = false;
            state.wishlistSelect.disabled = true;
            state.createToggle.textContent = 'Use existing wishlist';
            if (state.wishlists.length === 0) {
                state.wishlistSelect.innerHTML = '';
            }
            requestAnimationFrame(() => {
                state.createInput?.focus();
                state.createInput?.select();
            });
        } else {
            state.createField.hidden = true;
            state.wishlistSelect.disabled = false;
            state.createToggle.textContent = 'Create new wishlist';
            requestAnimationFrame(() => {
                state.wishlistSelect?.focus();
            });
        }
    }

    function setWineResultsVisible(visible) {
        if (!state.wineResults) {
            return;
        }

        const isVisible = Boolean(visible);
        if (isVisible) {
            state.wineResults.dataset.visible = 'true';
            state.wineResults.removeAttribute('hidden');
        } else {
            delete state.wineResults.dataset.visible;
            state.wineResults.setAttribute('hidden', '');
        }

        const expanded = isVisible ? 'true' : 'false';
        state.wineResults.setAttribute('aria-expanded', expanded);
        state.wineSearch?.setAttribute('aria-expanded', expanded);
    }

    function clearWineResults() {
        if (!state.wineResults) {
            return;
        }

        state.wineResults.innerHTML = '';
        setWineResultsVisible(false);
    }

    function clearError() {
        if (!state.errorElement) {
            return;
        }

        state.errorElement.textContent = '';
        state.errorElement.setAttribute('aria-hidden', 'true');
        state.errorElement.dataset.state = 'info';
    }

    function showError(message) {
        if (!state.errorElement) {
            return;
        }

        state.errorElement.textContent = message ?? '';
        state.errorElement.dataset.state = 'error';
        state.errorElement.setAttribute('aria-hidden', message ? 'false' : 'true');
    }

    function showInfo(message) {
        if (!state.errorElement) {
            return;
        }

        state.errorElement.textContent = message ?? '';
        state.errorElement.dataset.state = 'info';
        state.errorElement.setAttribute('aria-hidden', message ? 'false' : 'true');
    }

    function setBusy(isBusy) {
        if (!state.submitButton) {
            return;
        }

        state.pending = isBusy;
        state.submitButton.disabled = isBusy;
        if (isBusy) {
            state.submitButton.setAttribute('aria-busy', 'true');
        } else {
            state.submitButton.removeAttribute('aria-busy');
        }
    }

    function normalizeWineOption(raw) {
        if (!raw || !raw.id) {
            return null;
        }

        const vintages = Array.isArray(raw.vintages) ? raw.vintages : [];
        return {
            id: raw.id,
            name: (raw.name ?? '').toString().trim(),
            color: (raw.color ?? '').toString().trim(),
            subAppellation: raw.subAppellation ?? null,
            appellation: raw.appellation ?? null,
            region: raw.region ?? null,
            country: raw.country ?? null,
            vintages: vintages,
            label: raw.label ?? raw.name ?? ''
        };
    }

    function formatLocation(option) {
        const locationParts = [];
        if (option.subAppellation) {
            locationParts.push(option.subAppellation);
        }
        if (option.appellation && !locationParts.includes(option.appellation)) {
            locationParts.push(option.appellation);
        }
        if (option.region && !locationParts.includes(option.region)) {
            locationParts.push(option.region);
        }
        if (option.country && !locationParts.includes(option.country)) {
            locationParts.push(option.country);
        }
        return locationParts.join(' • ');
    }

    function buildSummary(option) {
        const parts = [];
        if (option.color) {
            parts.push(option.color);
        }
        const location = formatLocation(option);
        if (location) {
            parts.push(location);
        }
        return parts.length > 0
            ? parts.join(' · ')
            : 'No additional details available.';
    }

    function updateSummary(option) {
        if (!state.summary) {
            return;
        }

        if (!option) {
            state.summary.textContent = 'Start typing to find an existing wine.';
            return;
        }

        state.summary.textContent = buildSummary(option);
    }

    function handleWineSearchInput(event) {
        const value = typeof event?.target?.value === 'string'
            ? event.target.value.trim()
            : '';

        clearError();
        if (state.wineIdInput) {
            state.wineIdInput.value = '';
        }
        state.selectedWine = null;
        updateSummary(null);

        if (state.searchTimeoutId) {
            clearTimeout(state.searchTimeoutId);
            state.searchTimeoutId = null;
        }

        if (state.searchController) {
            state.searchController.abort();
            state.searchController = null;
        }

        if (!value || value.length < 3) {
            clearWineResults();
            showInfo(value.length > 0 ? 'Keep typing to search for wines.' : 'Start typing to find an existing wine.');
            return;
        }

        state.searchTimeoutId = setTimeout(() => {
            state.searchTimeoutId = null;
            performWineSearch(value);
        }, 200);
    }

    async function performWineSearch(query) {
        if (!state.wineResults) {
            return;
        }

        if (state.searchController) {
            state.searchController.abort();
        }

        state.searchController = new AbortController();
        const signal = state.searchController.signal;
        showInfo('Searching for wines…');
        try {
            const encoded = encodeURIComponent(query);
            const response = await sendJson(`/wine-manager/wines?search=${encoded}`, { method: 'GET', signal });
            if (signal.aborted) {
                return;
            }
            const options = Array.isArray(response)
                ? response.map(normalizeWineOption).filter(Boolean)
                : [];
            state.wineOptions = options;
            renderWineResults(options);
        } catch (error) {
            if (signal.aborted) {
                return;
            }
            console.error('Wishlist wine search failed', error);
            showError(error?.message ?? 'Unable to search for wines right now.');
            state.wineOptions = [];
            clearWineResults();
        }
    }

    function renderWineResults(options) {
        if (!state.wineResults) {
            return;
        }

        state.wineResults.innerHTML = '';
        if (!Array.isArray(options) || options.length === 0) {
            const empty = document.createElement('div');
            empty.className = 'inventory-add-wine-result-status inventory-add-wine-result-status--error';
            empty.textContent = 'No wines found.';
            state.wineResults.appendChild(empty);
            setWineResultsVisible(true);
            return;
        }

        const list = document.createElement('ul');
        list.className = 'inventory-add-wine-options';
        list.setAttribute('role', 'presentation');

        options.forEach(option => {
            const item = document.createElement('li');
            item.className = 'inventory-add-wine-option';
            item.setAttribute('role', 'option');
            item.tabIndex = -1;

            const name = document.createElement('div');
            name.className = 'inventory-add-wine-option__name';
            name.textContent = option.name || 'Unnamed wine';
            item.appendChild(name);

            const metaParts = [];
            const location = formatLocation(option);
            if (location) {
                metaParts.push(location);
            }
            if (option.color) {
                metaParts.push(option.color);
            }
            if (metaParts.length > 0) {
                const meta = document.createElement('div');
                meta.className = 'inventory-add-wine-option__meta';
                meta.textContent = metaParts.join(' · ');
                item.appendChild(meta);
            }

            item.addEventListener('click', () => {
                selectWine(option);
            });

            list.appendChild(item);
        });

        state.wineResults.appendChild(list);
        setWineResultsVisible(true);
    }

    function selectWine(option) {
        if (!option) {
            return;
        }

        state.selectedWine = option;
        if (state.wineSearch) {
            state.wineSearch.value = option.name ?? '';
        }
        if (state.wineIdInput) {
            state.wineIdInput.value = option.id;
        }
        if (state.searchTimeoutId) {
            clearTimeout(state.searchTimeoutId);
            state.searchTimeoutId = null;
        }
        if (state.searchController) {
            state.searchController.abort();
            state.searchController = null;
        }
        updateSummary(option);
        clearWineResults();
        state.wineOptions = [];
        clearError();
        if (Array.isArray(option.vintages) && option.vintages.length > 0 && state.vintageInput) {
            const first = option.vintages.find(v => Number.isFinite(Number(v)));
            if (first) {
                state.vintageInput.value = first;
            }
        }
    }

    async function ensureWishlistsLoaded(force = false) {
        if (state.wishlistsLoaded && !force) {
            populateWishlists(state.wishlists);
            return;
        }

        try {
            const response = await sendJson('/wine-manager/wishlists', { method: 'GET' });
            const wishlists = Array.isArray(response) ? response : [];
            state.wishlists = wishlists;
            state.wishlistsLoaded = true;
            populateWishlists(wishlists);
        } catch (error) {
            console.error('Failed to load wishlists', error);
            state.wishlists = [];
            state.wishlistsLoaded = false;
            populateWishlists([]);
            showError(error?.message ?? 'Unable to load wishlists right now.');
        }
    }

    function populateWishlists(wishlists) {
        if (!state.wishlistSelect) {
            return;
        }

        state.wishlistSelect.innerHTML = '';
        if (!Array.isArray(wishlists) || wishlists.length === 0) {
            const placeholder = document.createElement('option');
            placeholder.value = '';
            placeholder.textContent = 'No wishlists yet';
            placeholder.disabled = true;
            placeholder.selected = true;
            state.wishlistSelect.appendChild(placeholder);
            state.createToggle?.setAttribute('disabled', 'true');
            setCreateMode(true);
            state.preferredWishlistId = '';
            return;
        }

        const placeholder = document.createElement('option');
        placeholder.value = '';
        placeholder.textContent = 'Choose a wishlist';
        placeholder.selected = true;
        state.wishlistSelect.appendChild(placeholder);

        wishlists
            .slice()
            .sort((a, b) => {
                const nameA = (a?.name ?? '').toString().toLowerCase();
                const nameB = (b?.name ?? '').toString().toLowerCase();
                return nameA.localeCompare(nameB);
            })
            .forEach(entry => {
                if (!entry?.id) {
                    return;
                }
                const option = document.createElement('option');
                option.value = entry.id;
                option.textContent = entry.name ?? 'Wishlist';
                state.wishlistSelect.appendChild(option);
            });

        const preferredId = state.preferredWishlistId;
        if (preferredId) {
            const hasMatch = wishlists.some(entry => entry?.id === preferredId);
            if (hasMatch) {
                state.wishlistSelect.value = preferredId;
            } else {
                state.wishlistSelect.selectedIndex = 0;
            }
        } else {
            state.wishlistSelect.selectedIndex = 0;
        }

        state.createToggle?.removeAttribute('disabled');
        setCreateMode(false);
    }

    async function createWishlist(name) {
        const trimmed = name.trim();
        if (!trimmed) {
            throw new Error('Enter a name for the new wishlist.');
        }

        const payload = { name: trimmed };
        const response = await sendJson('/wine-manager/wishlists', {
            method: 'POST',
            body: payload
        });

        if (response?.id && response?.name) {
            state.wishlistsLoaded = false;
        }

        return response;
    }

    async function handleSubmit(event) {
        event.preventDefault();
        if (state.pending) {
            return;
        }

        clearError();

        const selected = state.selectedWine;
        if (!selected || !selected.id) {
            showError('Select a wine before adding it to a wishlist.');
            state.wineSearch?.focus();
            return;
        }

        const vintageValue = state.vintageInput?.value ? Number(state.vintageInput.value) : NaN;
        if (!Number.isFinite(vintageValue)) {
            showError('Enter the vintage for this wine.');
            state.vintageInput?.focus();
            return;
        }

        if (vintageValue < 1900 || vintageValue > 2100) {
            showError('Vintage must be between 1900 and 2100.');
            state.vintageInput?.focus();
            return;
        }

        let wishlistId = '';
        let wishlistName = '';

        try {
            setBusy(true);
            if (state.createMode || state.wishlists.length === 0) {
                if (!state.createInput) {
                    throw new Error('Enter a name for the new wishlist.');
                }

                const name = state.createInput.value.trim();
                if (!name) {
                    throw new Error('Enter a name for the new wishlist.');
                }

                const created = await createWishlist(name);
                wishlistId = created?.id ?? '';
                wishlistName = created?.name ?? name;
                if (!wishlistId) {
                    throw new Error('Wishlist could not be created.');
                }
                await ensureWishlistsLoaded(true);
            } else if (state.wishlistSelect) {
                wishlistId = state.wishlistSelect.value;
                if (!wishlistId) {
                    throw new Error('Choose a wishlist before continuing.');
                }
                const match = state.wishlists.find(w => w?.id === wishlistId);
                wishlistName = match?.name ?? '';
            }

            if (!wishlistId) {
                throw new Error('Choose a wishlist before continuing.');
            }

            const payload = {
                wineId: selected.id,
                vintage: Number.parseInt(vintageValue, 10)
            };

            const response = await sendJson(`/wine-manager/wishlists/${encodeURIComponent(wishlistId)}/wishes`, {
                method: 'POST',
                body: payload
            });

            closePopover();

            const detail = {
                wishlistId: response?.wishlistId ?? wishlistId,
                wishlistName: response?.wishlistName ?? wishlistName,
                wineName: response?.wineName ?? selected.name ?? '',
                vintage: response?.vintage ?? payload.vintage
            };

            document.dispatchEvent(new CustomEvent('wishlist:added', { detail }));
        } catch (error) {
            console.error('Unable to add wishlist item', error);
            showError(error?.message ?? 'Unable to add this wine to a wishlist right now.');
        } finally {
            setBusy(false);
        }
    }

    function resetForm() {
        if (state.form) {
            state.form.reset();
        }
        state.selectedWine = null;
        state.wineOptions = [];
        state.preferredWishlistId = '';
        updateSummary(null);
        clearWineResults();
        clearError();
    }

    function applyContext(context) {
        resetForm();
        state.context = context ?? null;
        state.preferredWishlistId = '';

        if (!context) {
            return;
        }

        if (context.wishlistId) {
            state.preferredWishlistId = context.wishlistId;
        }

        if (state.wineSearch && context.name) {
            state.wineSearch.value = context.name;
        }

        if (state.vintageInput && context.vintage) {
            state.vintageInput.value = context.vintage;
        }

        if (context.id && state.wineIdInput) {
            state.wineIdInput.value = context.id;
            state.selectedWine = {
                id: context.id,
                name: context.name ?? '',
                color: context.color ?? '',
                subAppellation: context.subAppellation ?? '',
                appellation: context.appellation ?? '',
                region: context.region ?? '',
                country: context.country ?? '',
                vintages: context.vintage ? [context.vintage] : []
            };
            updateSummary(state.selectedWine);
        } else {
            updateSummary({
                color: context.color ?? '',
                subAppellation: context.subAppellation ?? '',
                appellation: context.appellation ?? '',
                region: context.region ?? '',
                country: context.country ?? ''
            });
        }
    }

    function openPopover() {
        if (!state.overlay || !state.popover) {
            throw new Error('Wishlist popover is unavailable.');
        }

        state.overlay.hidden = false;
        state.overlay.setAttribute('aria-hidden', 'false');
        state.popover.setAttribute('aria-hidden', 'false');
        state.open = true;
        requestAnimationFrame(() => {
            state.wineSearch?.focus();
            if (state.wineSearch?.value) {
                state.wineSearch.select();
            }
        });
    }

    function closePopover() {
        if (!state.overlay || !state.popover || !state.open) {
            return;
        }

        if (state.searchController) {
            state.searchController.abort();
            state.searchController = null;
        }

        state.overlay.hidden = true;
        state.overlay.setAttribute('aria-hidden', 'true');
        state.popover.setAttribute('aria-hidden', 'true');
        state.open = false;
        resetForm();
    }

    async function openWishlistPopover(context) {
        if (!state.hasElements) {
            throw new Error('Wishlist popover is unavailable.');
        }

        applyContext(context);
        await ensureWishlistsLoaded();
        openPopover();
    }

    async function sendJson(url, options = {}) {
        const requestInit = {
            headers: {
                'Accept': 'application/json'
            },
            credentials: 'same-origin',
            ...options
        };

        if (requestInit.body && typeof requestInit.body !== 'string') {
            requestInit.headers['Content-Type'] = 'application/json';
            requestInit.body = JSON.stringify(requestInit.body);
        }

        const response = await fetch(url, requestInit);
        if (response.ok) {
            if (response.status === 204) {
                return null;
            }

            const text = await response.text();
            return text ? JSON.parse(text) : null;
        }

        const message = await readErrorMessage(response);
        throw new Error(message);
    }

    async function readErrorMessage(response) {
        try {
            const payload = await response.json();
            if (!payload) {
                return `${response.status} ${response.statusText}`;
            }

            if (typeof payload === 'string') {
                return payload;
            }

            if (payload.message) {
                return payload.message;
            }

            if (payload.title) {
                return payload.title;
            }

            if (payload.errors) {
                const firstError = Object.values(payload.errors)[0];
                if (Array.isArray(firstError) && firstError.length > 0) {
                    return firstError[0];
                }
            }
        } catch (error) {
            console.error('Failed to parse wishlist error response', error);
        }

        return `${response.status} ${response.statusText}`;
    }

    function requestOpenWishlistPopover(context = null) {
        return new Promise((resolve, reject) => {
            onReady(() => {
                try {
                    initialize();
                    if (!state.hasElements) {
                        throw new Error('Wishlist popover is unavailable.');
                    }

                    openWishlistPopover(context).then(resolve, reject);
                } catch (error) {
                    reject(error);
                }
            });
        });
    }

    const api = window.wishlistPopover ?? {};
    api.open = requestOpenWishlistPopover;
    api.close = closePopover;
    window.wishlistPopover = api;

    onReady(initialize);
})();
