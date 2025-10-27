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
        triggers: '[data-open-bottle-management]',
        addButton: '[data-bottle-management-add]',
        locationSelect: '[data-bottle-management-location]',
        quantitySelect: '[data-bottle-management-quantity]'
    };

    const state = {
        wineVintageId: null,
        abortController: null,
        isOpen: false,
        isAdding: false,
        hasGroup: false,
        locations: [],
        selectedLocationId: null,
        quantity: 1
    };

    const qs = (selector, root = document) => root.querySelector(selector);
    const qsa = (selector, root = document) => Array.from(root.querySelectorAll(selector));

    const normalizeQuantity = (value) => {
        const numeric = typeof value === 'number' ? value : Number(value);
        if (!Number.isFinite(numeric)) {
            return 1;
        }

        const integral = Math.trunc(numeric);
        if (!Number.isFinite(integral)) {
            return 1;
        }

        if (integral < 1) {
            return 1;
        }

        if (integral > 12) {
            return 12;
        }

        return integral;
    };

    const buildLocationLabel = (location) => {
        const id = typeof location?.id === 'string' ? location.id : '';
        const rawName = typeof location?.name === 'string' ? location.name : '';
        const name = rawName.trim() || id;
        const capacity = location?.capacity;

        if (Number.isFinite(capacity)) {
            return `${name} (${capacity} capacity)`;
        }

        return name;
    };

    const resetControls = (dialog) => {
        const root = dialog || qs(SELECTORS.dialog);
        if (!root) {
            return;
        }

        const locationSelect = qs(SELECTORS.locationSelect, root);
        if (locationSelect) {
            locationSelect.innerHTML = '<option value="">No location</option>';
            locationSelect.value = '';
            locationSelect.setAttribute('disabled', '');
        }

        const quantitySelect = qs(SELECTORS.quantitySelect, root);
        if (quantitySelect) {
            const defaultQuantity = normalizeQuantity(1);
            quantitySelect.value = String(defaultQuantity);
        }
    };

    const updateLocationOptions = (locations) => {
        const select = qs(SELECTORS.locationSelect);
        if (!select) {
            state.locations = [];
            state.selectedLocationId = null;
            return;
        }

        const normalized = Array.isArray(locations)
            ? locations
                .map((location) => {
                    const rawId = location?.Id ?? location?.id;
                    if (rawId == null) {
                        return null;
                    }

                    const id = String(rawId).trim();
                    if (!id) {
                        return null;
                    }

                    const rawName = location?.Name ?? location?.name;
                    const name = typeof rawName === 'string' ? rawName.trim() : '';
                    const rawCapacity = location?.Capacity ?? location?.capacity;
                    const numericCapacity = typeof rawCapacity === 'number'
                        ? rawCapacity
                        : Number(rawCapacity);
                    const capacity = Number.isFinite(numericCapacity) ? numericCapacity : null;

                    return {
                        id,
                        name,
                        capacity
                    };
                })
                .filter((value) => value !== null)
            : [];

        const previousSelection = state.selectedLocationId;
        state.locations = normalized;

        select.innerHTML = '';
        const defaultOption = document.createElement('option');
        defaultOption.value = '';
        defaultOption.textContent = 'No location';
        select.appendChild(defaultOption);

        if (normalized.length === 0) {
            select.value = '';
            select.removeAttribute('disabled');
            state.selectedLocationId = null;
            return;
        }

        normalized.forEach((location) => {
            const option = document.createElement('option');
            option.value = location.id;
            option.textContent = buildLocationLabel(location);
            select.appendChild(option);
        });

        const hasPrevious = typeof previousSelection === 'string'
            && normalized.some((location) => location.id === previousSelection);
        const targetValue = hasPrevious ? previousSelection : '';
        select.value = targetValue;
        state.selectedLocationId = targetValue || null;
        select.removeAttribute('disabled');
    };

    const syncControlState = () => {
        const locationSelect = qs(SELECTORS.locationSelect);
        if (locationSelect) {
            state.selectedLocationId = locationSelect.value ? locationSelect.value : null;
        }

        const quantitySelect = qs(SELECTORS.quantitySelect);
        if (quantitySelect) {
            const normalizedQuantity = normalizeQuantity(quantitySelect.value);
            state.quantity = normalizedQuantity;
            quantitySelect.value = String(normalizedQuantity);
        } else {
            state.quantity = normalizeQuantity(state.quantity);
        }
    };

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
        state.isAdding = false;
        state.hasGroup = false;
        state.locations = [];
        state.selectedLocationId = null;
        state.quantity = normalizeQuantity(1);
        resetControls(dialog);
        syncControlState();
        updateAddButtonState();
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
        state.isAdding = false;
        state.hasGroup = false;
        state.locations = [];
        state.selectedLocationId = null;
        state.quantity = normalizeQuantity(1);
        resetControls();
        syncControlState();
        updateAddButtonState();
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

        state.hasGroup = false;
        updateAddButtonState();
        setLoadingState();

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
            setTableMessage('Unable to load bottles.');
            updateAddButtonState();
        } finally {
            state.abortController = null;
        }
    };

    const renderDetails = (payload) => {
        const details = Array.isArray(payload?.Details)
            ? payload.Details
            : (Array.isArray(payload?.details) ? payload.details : []);
        const group = payload?.Group ?? payload?.group ?? null;
        const locations = Array.isArray(payload?.Locations)
            ? payload.Locations
            : (Array.isArray(payload?.locations) ? payload.locations : []);

        console.info('[BottleManagementModal] Loaded bottle details', details);

        updateLocationOptions(locations);
        updateSummary(group, details);
        renderRows(details);
        state.hasGroup = Boolean(group);
        updateAddButtonState();
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

    const setLoadingState = (message = 'Loading bottles…') => {
        setTableMessage(message);
    };

    const setTableMessage = (message) => {
        const tbody = qs(SELECTORS.tableBody);
        if (!tbody) {
            return;
        }

        const safeMessage = escapeHtml(message ?? '');
        tbody.innerHTML = `<tr class="empty-row"><td colspan="4">${safeMessage}</td></tr>`;
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

    const wireLocationSelect = () => {
        const select = qs(SELECTORS.locationSelect);
        if (!select) {
            return;
        }

        select.addEventListener('change', () => {
            state.selectedLocationId = select.value ? select.value : null;
        });
    };

    const wireQuantitySelect = () => {
        const select = qs(SELECTORS.quantitySelect);
        if (!select) {
            return;
        }

        select.addEventListener('change', () => {
            const normalized = normalizeQuantity(select.value);
            state.quantity = normalized;
            select.value = String(normalized);
        });
    };

    const wireAddButton = () => {
        const button = qs(SELECTORS.addButton);
        if (!button) {
            return;
        }

        button.addEventListener('click', (event) => {
            event.preventDefault();
            addBottle();
        });
    };

    const updateAddButtonState = () => {
        const button = qs(SELECTORS.addButton);
        if (!button) {
            return;
        }

        const shouldDisable = !state.isOpen
            || !state.wineVintageId
            || state.isAdding
            || !state.hasGroup;

        if (shouldDisable) {
            button.setAttribute('disabled', '');
            button.setAttribute('aria-disabled', 'true');
        } else {
            button.removeAttribute('disabled');
            button.removeAttribute('aria-disabled');
        }
    };

    const addBottle = async () => {
        if (!state.isOpen || state.isAdding || !state.wineVintageId) {
            return;
        }

        state.isAdding = true;
        updateAddButtonState();

        try {
            syncControlState();
            const payload = {
                wineVintageId: state.wineVintageId,
                quantity: normalizeQuantity(state.quantity)
            };

            state.quantity = payload.quantity;

            if (state.selectedLocationId) {
                payload.bottleLocationId = state.selectedLocationId;
            }

            const response = await fetch('/wine-manager/bottles', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'Accept': 'application/json'
                },
                body: JSON.stringify(payload)
            });

            const raw = await response.text();
            const contentType = response.headers.get('Content-Type') ?? '';
            const isJson = contentType.toLowerCase().includes('application/json');
            let data = null;

            if (raw && isJson) {
                try {
                    data = JSON.parse(raw);
                } catch (parseError) {
                    console.error('Unable to parse add bottle response', parseError);
                }
            }

            if (!response.ok) {
                const fallback = raw?.trim() ? raw.trim() : 'We could not add a bottle right now. Please try again.';
                const message = extractProblemMessage(data, fallback);
                showError(message);
                return;
            }

            if (!data) {
                showError('We could not add a bottle right now. Please try again.');
                return;
            }

            clearError();
            renderDetails(data);
        } catch (error) {
            console.error('Failed to add bottle', error);
            showError('We could not add a bottle right now. Please try again.');
        } finally {
            state.isAdding = false;
            updateAddButtonState();
        }
    };

    const extractProblemMessage = (problem, fallbackMessage) => {
        if (!problem || typeof problem !== 'object') {
            return fallbackMessage;
        }

        const candidates = [
            problem.message,
            problem.Message,
            problem.error,
            problem.Error,
            problem.title,
            problem.Title,
            problem.detail,
            problem.Detail
        ];

        for (const candidate of candidates) {
            if (typeof candidate === 'string' && candidate.trim()) {
                return candidate.trim();
            }
        }

        const errors = problem.errors || problem.Errors;
        if (errors && typeof errors === 'object') {
            for (const key of Object.keys(errors)) {
                const value = errors[key];
                if (Array.isArray(value) && value.length > 0) {
                    const [first] = value;
                    if (typeof first === 'string' && first.trim()) {
                        return first.trim();
                    }
                } else if (typeof value === 'string' && value.trim()) {
                    return value.trim();
                }
            }
        }

        return fallbackMessage;
    };

    document.addEventListener('DOMContentLoaded', () => {
        wireCloseButtons();
        wireTriggers();
        wireLocationSelect();
        wireQuantitySelect();
        wireAddButton();
        syncControlState();
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
