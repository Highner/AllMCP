(() => {
    'use strict';

    const SELECTORS = {
        overlay: '[data-bottle-management-overlay]',
        dialog: '[data-bottle-management-modal]',
        closeButtons: '[data-bottle-management-close]',
        vintageField: '[data-bottle-management-vintage-id]',
        wineName: '[data-bottle-management-wine]',
        vintageLabel: '[data-bottle-management-vintage]',
        countLabel: '[data-bottle-management-count]',
        statusLabel: '[data-bottle-management-status]',
        averageLabel: '[data-bottle-management-average]',
        error: '[data-bottle-management-error]',
        tableBody: '[data-bottle-management-rows]',
        metaSeparators: '.bottle-management-meta-separator',
        triggers: '[data-open-bottle-management]'
    };

    const state = {
        wineVintageId: null,
        abortController: null,
        isOpen: false
    };

    const qs = (selector, root = document) => root.querySelector(selector);
    const qsa = (selector, root = document) => Array.from(root.querySelectorAll(selector));

    const open = (wineVintageId) => {
        if (!wineVintageId) {
            showError('We could not determine which wine vintage to display.');
            return;
        }

        const overlay = qs(SELECTORS.overlay);
        const dialog = qs(SELECTORS.dialog);
        if (!overlay || !dialog) {
            return;
        }

        state.wineVintageId = wineVintageId;
        overlay.removeAttribute('hidden');
        overlay.setAttribute('aria-hidden', 'false');
        state.isOpen = true;
        attachKeydown();

        const hiddenField = qs(SELECTORS.vintageField, dialog);
        if (hiddenField) {
            hiddenField.value = wineVintageId;
        }

        clearError();
        setLoadingState();
        fetchBottles(wineVintageId);

        const focusTarget = qs(SELECTORS.closeButtons, dialog) || qs('button, [href], input, select, textarea', dialog);
        if (focusTarget && typeof focusTarget.focus === 'function') {
            focusTarget.focus();
        }
    };

    const close = () => {
        const overlay = qs(SELECTORS.overlay);
        if (!overlay) {
            return;
        }

        if (state.abortController) {
            state.abortController.abort();
            state.abortController = null;
        }

        overlay.setAttribute('aria-hidden', 'true');
        overlay.setAttribute('hidden', '');
        state.isOpen = false;
        state.wineVintageId = null;
        detachKeydown();
    };

    const attachKeydown = () => {
        document.addEventListener('keydown', handleKeydown);
    };

    const detachKeydown = () => {
        document.removeEventListener('keydown', handleKeydown);
    };

    const handleKeydown = (event) => {
        if (event.key === 'Escape' && state.isOpen) {
            event.preventDefault();
            close();
        }
    };

    const fetchBottles = async (wineVintageId) => {
        if (state.abortController) {
            state.abortController.abort();
        }

        state.abortController = new AbortController();
        const { signal } = state.abortController;

        const tbody = qs(SELECTORS.tableBody);
        if (tbody) {
            tbody.innerHTML = '<tr class="empty-row"><td colspan="4">Loading bottles…</td></tr>';
        }

        try {
            const response = await fetch(`/wine-manager/bottles/${wineVintageId}`, {
                method: 'GET',
                headers: {
                    'Accept': 'application/json'
                },
                signal
            });

            if (!response.ok) {
                throw new Error('Failed to load bottles');
            }

            const payload = await response.json();
            renderDetails(payload);
        } catch (error) {
            if (error?.name === 'AbortError') {
                return;
            }

            console.error(error);
            showError('We could not load your bottles right now. Please try again.');
            if (tbody) {
                tbody.innerHTML = '<tr class="empty-row"><td colspan="4">Unable to load bottles.</td></tr>';
            }
        } finally {
            state.abortController = null;
        }
    };

    const renderDetails = (payload) => {
        const details = Array.isArray(payload?.Details)
            ? payload.Details
            : (Array.isArray(payload?.details) ? payload.details : []);
        const group = payload?.Group ?? payload?.group ?? null;

        updateSummary(group, details);
        renderRows(details);
    };

    const updateSummary = (group, details) => {
        const wineNameEl = qs(SELECTORS.wineName);
        const vintageEl = qs(SELECTORS.vintageLabel);
        const countEl = qs(SELECTORS.countLabel);
        const statusEl = qs(SELECTORS.statusLabel);
        const averageEl = qs(SELECTORS.averageLabel);
        const separators = qsa(SELECTORS.metaSeparators);

        if (!group) {
            if (wineNameEl) {
                wineNameEl.textContent = 'No bottles found';
            }
            if (vintageEl) {
                vintageEl.textContent = 'Vintage —';
            }
            if (countEl) {
                countEl.textContent = 'Bottles: 0';
            }
            if (statusEl) {
                statusEl.textContent = 'Status: —';
            }
            if (averageEl) {
                averageEl.textContent = 'Avg. score: —';
            }
            separators.forEach((separator) => {
                separator.hidden = true;
            });
            return;
        }

        if (wineNameEl) {
            wineNameEl.textContent = `${group.WineName ?? group.wineName ?? 'Unknown wine'}`;
        }

        if (vintageEl) {
            const rawVintage = group.Vintage ?? group.vintage;
            const numericVintage = typeof rawVintage === 'number'
                ? rawVintage
                : Number(rawVintage);
            const hasVintage = Number.isFinite(numericVintage) && numericVintage > 0;
            const vintage = hasVintage ? numericVintage : '—';
            vintageEl.textContent = `Vintage ${vintage}`;
        }

        if (countEl) {
            const bottleCountSource = group.BottleCount ?? group.bottleCount;
            const numericCount = typeof bottleCountSource === 'number'
                ? bottleCountSource
                : Number(bottleCountSource);
            const bottleCount = Number.isFinite(numericCount) ? numericCount : details.length;
            const noun = bottleCount === 1 ? 'bottle' : 'bottles';
            countEl.textContent = `Bottles: ${bottleCount} ${noun}`;
        }

        if (statusEl) {
            const statusLabel = group.StatusLabel ?? group.statusLabel ?? '—';
            statusEl.textContent = `Status: ${statusLabel}`;
        }

        if (averageEl) {
            const score = formatScore(group.AverageScore ?? group.averageScore);
            averageEl.textContent = `Avg. score: ${score}`;
        }

        separators.forEach((separator) => {
            separator.hidden = false;
        });
    };

    const renderRows = (details) => {
        const tbody = qs(SELECTORS.tableBody);
        if (!tbody) {
            return;
        }

        if (!Array.isArray(details) || details.length === 0) {
            tbody.innerHTML = '<tr class="empty-row"><td colspan="4">You do not have bottles for this vintage yet.</td></tr>';
            return;
        }

        const rows = details.map((detail) => {
            const location = escapeHtml(detail?.BottleLocation ?? detail?.bottleLocation ?? '—');
            const price = formatPrice(detail?.Price ?? detail?.price);
            const score = formatScore(
                detail?.CurrentUserScore
                ?? detail?.currentUserScore
                ?? detail?.AverageScore
                ?? detail?.averageScore
            );
            const isDrunk = detail?.IsDrunk ?? detail?.isDrunk ?? false;
            const status = isDrunk ? 'Yes' : 'No';
            return (
                '<tr>' +
                    `<td class="bottle-management-col-location">${location}</td>` +
                    `<td class="bottle-management-col-price">${price}</td>` +
                    `<td class="bottle-management-col-score">${score}</td>` +
                    `<td class="bottle-management-col-status">${status}</td>` +
                '</tr>'
            );
        }).join('');

        tbody.innerHTML = rows;
    };

    const formatPrice = (value) => {
        if (value == null || value === '') {
            return '—';
        }

        const numeric = typeof value === 'number' ? value : Number(value);
        if (!Number.isFinite(numeric)) {
            return '—';
        }

        return numeric.toLocaleString(undefined, {
            style: 'currency',
            currency: 'USD',
            minimumFractionDigits: 2,
            maximumFractionDigits: 2
        });
    };

    const formatScore = (value) => {
        if (value == null || value === '') {
            return '—';
        }

        const numeric = typeof value === 'number' ? value : Number(value);
        if (!Number.isFinite(numeric)) {
            return '—';
        }

        return numeric.toFixed(1);
    };

    const escapeHtml = (value) => {
        return String(value ?? '')
            .replaceAll('&', '&amp;')
            .replaceAll('<', '&lt;')
            .replaceAll('>', '&gt;')
            .replaceAll('"', '&quot;')
            .replaceAll('\'', '&#39;');
    };

    const setLoadingState = () => {
        const tbody = qs(SELECTORS.tableBody);
        if (tbody) {
            tbody.innerHTML = '<tr class="empty-row"><td colspan="4">Loading bottles…</td></tr>';
        }
    };

    const showError = (message) => {
        const error = qs(SELECTORS.error);
        if (!error) {
            return;
        }

        error.textContent = message;
        error.hidden = false;
    };

    const clearError = () => {
        const error = qs(SELECTORS.error);
        if (!error) {
            return;
        }

        error.textContent = '';
        error.hidden = true;
    };

    const wireCloseButtons = () => {
        const dialog = qs(SELECTORS.dialog);
        if (!dialog) {
            return;
        }

        qsa(SELECTORS.closeButtons, dialog).forEach((button) => {
            button.addEventListener('click', (event) => {
                event.preventDefault();
                close();
            });
        });
    };

    const wireTriggers = () => {
        qsa(SELECTORS.triggers).forEach((trigger) => {
            trigger.addEventListener('click', (event) => {
                event.preventDefault();
                const wineVintageId = trigger.getAttribute('data-wine-vintage-id')
                    || trigger.dataset.wineVintageId
                    || trigger.getAttribute('data-wineVintageId');
                open(wineVintageId);
            });
        });
    };

    document.addEventListener('DOMContentLoaded', () => {
        wireCloseButtons();
        wireTriggers();
        const overlay = qs(SELECTORS.overlay);
        if (overlay) {
            overlay.addEventListener('click', (event) => {
                if (event.target === overlay) {
                    close();
                }
            });
        }
    });

    window.BottleManagementModal = {
        open,
        close
    };
})();
