(function (global) {
    const state = {
        initialized: false,
        overlay: null,
        dialog: null,
        container: null,
        lastFocusedElement: null
    };

    function ensureInitialized() {
        if (state.initialized) {
            return;
        }

        state.overlay = document.getElementById('bottle-management-overlay');
        state.dialog = document.getElementById('bottle-management-modal');
        state.container = resolveContainer();

        if (state.overlay) {
            state.overlay.setAttribute('aria-hidden', state.overlay.getAttribute('aria-hidden') ?? 'true');
            if (!state.overlay.hasAttribute('hidden')) {
                state.overlay.hidden = true;
            }
        }

        if (state.dialog) {
            state.dialog.setAttribute('aria-hidden', state.dialog.getAttribute('aria-hidden') ?? 'true');
        }

        state.initialized = true;
    }

    function resolveContainer() {
        if (state.dialog) {
            const container = state.dialog.querySelector('[data-bottle-management-container]');
            if (container) {
                return container;
            }
        }

        return document.getElementById('details-view');
    }

    function resolveFocusTarget(target) {
        if (!state.dialog) {
            return null;
        }

        if (target instanceof HTMLElement) {
            return target;
        }

        if (typeof target === 'string' && target) {
            const direct = state.dialog.querySelector(target);
            if (direct instanceof HTMLElement) {
                return direct;
            }
        }

        const explicit = state.dialog.querySelector('[data-bottle-management-initial-focus]');
        if (explicit instanceof HTMLElement) {
            return explicit;
        }

        return state.dialog;
    }

    function open(options = {}) {
        ensureInitialized();

        if (!state.overlay || !state.dialog) {
            return false;
        }

        state.lastFocusedElement = document.activeElement instanceof HTMLElement ? document.activeElement : null;

        state.overlay.hidden = false;
        state.overlay.setAttribute('aria-hidden', 'false');
        state.overlay.classList.add('is-open');

        state.dialog.setAttribute('aria-hidden', 'false');
        state.dialog.setAttribute('data-state', 'open');

        const focusTarget = resolveFocusTarget(options.focusTarget);
        if (focusTarget && typeof focusTarget.focus === 'function') {
            try {
                focusTarget.focus({ preventScroll: true });
            } catch {
                focusTarget.focus();
            }
        }

        state.dialog.dispatchEvent(new CustomEvent('bottle-management-modal:opened', {
            detail: {
                source: options.source ?? 'api'
            }
        }));

        return true;
    }

    function close(options = {}) {
        ensureInitialized();

        if (!state.overlay || !state.dialog) {
            return false;
        }

        const detail = {
            source: options.source ?? 'api'
        };

        const closingEvent = new CustomEvent('bottle-management-modal:closing', {
            cancelable: true,
            detail
        });

        if (!state.dialog.dispatchEvent(closingEvent)) {
            return false;
        }

        state.overlay.classList.remove('is-open');
        state.overlay.setAttribute('aria-hidden', 'true');
        state.overlay.hidden = true;

        state.dialog.setAttribute('aria-hidden', 'true');
        state.dialog.setAttribute('data-state', 'closed');

        state.dialog.dispatchEvent(new CustomEvent('bottle-management-modal:closed', {
            detail
        }));

        const shouldRestoreFocus = options.restoreFocus !== false;
        if (shouldRestoreFocus && state.lastFocusedElement && typeof state.lastFocusedElement.focus === 'function') {
            try {
                state.lastFocusedElement.focus({ preventScroll: true });
            } catch {
                state.lastFocusedElement.focus();
            }
        }

        state.lastFocusedElement = null;
        return true;
    }

    function isOpen() {
        ensureInitialized();
        return Boolean(state.overlay && state.overlay.hidden === false);
    }

    function getContainer() {
        ensureInitialized();
        if (state.container && document.body.contains(state.container)) {
            return state.container;
        }

        state.container = resolveContainer();
        return state.container;
    }

    function getElements() {
        ensureInitialized();
        return {
            overlay: state.overlay,
            dialog: state.dialog,
            container: getContainer()
        };
    }

    const api = {
        initialize: ensureInitialized,
        open,
        close,
        isOpen,
        getContainer,
        getElements
    };

    global.BottleManagementModal = api;

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', ensureInitialized, { once: true });
    } else {
        ensureInitialized();
    }
})(window);
