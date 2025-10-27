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
    // --- Begin: Drink Bottle Modal integration for Bottle Management context ---
    (function attachDrinkModalHandlerOnce(){
        if (global.__BottleManagementDrinkHandlerBound) {
            return;
        }
        global.__BottleManagementDrinkHandlerBound = true;

        async function sendJson(url, options) {
            const defaultHeaders = {
                'Accept': 'application/json',
                'Content-Type': 'application/json',
                'X-Requested-With': 'XMLHttpRequest'
            };

            const opts = Object.assign({ method: 'GET' }, options || {});
            // Merge headers so caller-supplied headers (e.g., anti-forgery token) don't wipe defaults
            const callerHeaders = (opts && opts.headers) ? opts.headers : {};
            opts.headers = Object.assign({}, defaultHeaders, callerHeaders);

            // If sending a JSON body and Content-Type is missing/null, set it
            if (opts.body != null && !opts.headers['Content-Type']) {
                opts.headers['Content-Type'] = 'application/json';
            }

            const res = await fetch(url, opts);
            const text = await res.text();
            let data = null;
            try {
                data = text ? JSON.parse(text) : null;
            } catch {
                data = null;
            }
            if (!res.ok) {
                const msg = (data && (data.message || data.error)) || text || 'Request failed.';
                throw new Error(typeof msg === 'string' ? msg : 'Request failed.');
            }
            return data;
        }

        function getAntiForgeryToken() {
            const formToken = document.querySelector('#drink-bottle-popover form input[name="__RequestVerificationToken"]');
            return formToken ? formToken.value : '';
        }

        window.addEventListener('drinkmodal:submit', (event) => {
            const d = event && event.detail;
            // Handle only when Bottle Management modal is open; otherwise let other contexts handle it
            if (!d || !global.BottleManagementModal || !global.BottleManagementModal.isOpen()) {
                return;
            }

            event.preventDefault();

            const promise = (async () => {
                const bottleId = (d.bottleId || '').toString();
                if (!bottleId) {
                    d.showError && d.showError('Unable to determine the selected bottle.');
                    throw new Error('Unable to determine the selected bottle.');
                }

                const noteOnly = d.noteOnly === true || d.noteOnly === 'true' || d.noteOnly === 1 || d.noteOnly === '1';
                const token = getAntiForgeryToken();

                const headers = token ? { 'RequestVerificationToken': token } : undefined;

                // If not note-only, mark bottle as drunk (or update drunk state) first
                if (!noteOnly) {
                    const payload = {
                        isDrunk: true,
                        drunkAt: d.date || null
                    };
                    // Try PUT /bottles/{id}, fallback to POST /bottles/{id}/drink
                    try {
                        await sendJson(`/wine-manager/bottles/${encodeURIComponent(bottleId)}`, {
                            method: 'PUT',
                            headers,
                            body: JSON.stringify(payload)
                        });
                    } catch (err) {
                        // Fallback endpoint
                        try {
                            await sendJson(`/wine-manager/bottles/${encodeURIComponent(bottleId)}/drink`, {
                                method: 'POST',
                                headers,
                                body: JSON.stringify(payload)
                            });
                        } catch {
                            throw err;
                        }
                    }
                }

                // Now upsert tasting note
                const notePayload = {
                    note: d.note || '',
                    score: d.score
                };
                let noteUrl = '/wine-manager/notes';
                let noteMethod = 'POST';
                if (d.noteId) {
                    noteUrl = `/wine-manager/notes/${encodeURIComponent(d.noteId)}`;
                    noteMethod = 'PUT';
                } else {
                    notePayload.bottleId = bottleId;
                }

                await sendJson(noteUrl, {
                    method: noteMethod,
                    headers,
                    body: JSON.stringify(notePayload)
                });

                const successMessage = noteOnly ? 'Tasting note saved.' : 'Bottle marked as drunk and tasting note saved.';
                return { message: successMessage };
            })();

            if (d && typeof d.setSubmitPromise === 'function') {
                d.setSubmitPromise(promise);
            }
            if (d && typeof d.setSuccessMessage === 'function') {
                const isNoteOnly = d.noteOnly === true || d.noteOnly === 'true' || d.noteOnly === 1 || d.noteOnly === '1';
                d.setSuccessMessage(isNoteOnly ? 'Tasting note saved.' : 'Bottle marked as drunk and tasting note saved.');
            }
        });
    })();
    // --- End: Drink Bottle Modal integration ---
})(window);
