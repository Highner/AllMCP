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
        drinkingWindowDisplay: '[data-bottle-management-drinking-window-display]',
        drinkingWindowStartInput: '[data-bottle-management-drinking-window-start]',
        drinkingWindowEndInput: '[data-bottle-management-drinking-window-end]',
        drinkingWindowSaveButton: '[data-bottle-management-save-drinking-window]',
        error: '[data-bottle-management-error]',
        tableBody: '[data-bottle-management-rows]',
        metaSeparators: '.bottle-management-meta-separator',
        triggers: '[data-open-bottle-management]',
        addButton: '[data-bottle-management-add]',
        locationSelect: '[data-bottle-management-location]',
        quantitySelect: '[data-bottle-management-quantity]',
        pendingCheckbox: '[data-bottle-management-pending]',
        rowLocationSelect: '[data-bottle-management-location-field]',
        rowPriceInput: '[data-bottle-management-price-input]',
        rowPendingCheckbox: '[data-bottle-management-pending-field]'
    };

    const state = {
        wineVintageId: null,
        abortController: null,
        isOpen: false,
        isAdding: false,
        hasGroup: false,
        locations: [],
        selectedLocationId: null,
        quantity: 1,
        pendingDelivery: false,
        drinkingWindowStart: null,
        drinkingWindowEnd: null,
        isSavingDrinkingWindow: false
    };

    const bottleDetailMap = new Map();

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

    const normalizeYearValue = (value) => {
        const coerceYear = (input) => {
            if (!Number.isFinite(input)) {
                return null;
            }

            const year = Math.trunc(input);
            if (!Number.isFinite(year)) {
                return null;
            }

            return year >= 1900 && year <= 2200 ? year : null;
        };

        if (typeof value === 'number') {
            return coerceYear(value);
        }

        if (typeof value === 'string') {
            const trimmed = value.trim();
            if (!trimmed) {
                return null;
            }

            const numeric = Number.parseInt(trimmed, 10);
            if (Number.isFinite(numeric)) {
                return coerceYear(numeric);
            }

            return null;
        }

        return null;
    };

    const formatDisplayYear = (year) => {
        if (!Number.isFinite(year)) {
            return null;
        }

        const integral = Math.trunc(year);
        if (!Number.isFinite(integral)) {
            return null;
        }

        return String(integral);
    };

    const getDrinkingWindowDraft = () => {
        const startInput = qs(SELECTORS.drinkingWindowStartInput);
        const endInput = qs(SELECTORS.drinkingWindowEndInput);

        const start = startInput ? normalizeYearValue(startInput.value) : null;
        const end = endInput ? normalizeYearValue(endInput.value) : null;

        return { start, end };
    };

    const resetDrinkingWindowControls = (root) => {
        const scope = root || qs(SELECTORS.dialog) || document;
        const startInput = qs(SELECTORS.drinkingWindowStartInput, scope);
        const endInput = qs(SELECTORS.drinkingWindowEndInput, scope);
        const display = qs(SELECTORS.drinkingWindowDisplay, scope);
        const saveButton = qs(SELECTORS.drinkingWindowSaveButton, scope);

        state.drinkingWindowStart = null;
        state.drinkingWindowEnd = null;
        state.isSavingDrinkingWindow = false;

        if (startInput) {
            startInput.value = '';
            startInput.setAttribute('disabled', '');
            startInput.removeAttribute('aria-invalid');
        }

        if (endInput) {
            endInput.value = '';
            endInput.setAttribute('disabled', '');
            endInput.removeAttribute('aria-invalid');
        }

        if (display) {
            display.textContent = 'Drinking window: —';
            display.removeAttribute('title');
        }

        if (saveButton) {
            saveButton.setAttribute('disabled', '');
            saveButton.setAttribute('aria-disabled', 'true');
        }
    };

    const refreshDrinkingWindowControls = () => {
        const startInput = qs(SELECTORS.drinkingWindowStartInput);
        const endInput = qs(SELECTORS.drinkingWindowEndInput);
        const saveButton = qs(SELECTORS.drinkingWindowSaveButton);

        const { start, end } = getDrinkingWindowDraft();
        const hasGroup = state.hasGroup;
        const isSaving = state.isSavingDrinkingWindow;
        const startHasValue = Boolean(startInput && startInput.value && startInput.value.trim());
        const endHasValue = Boolean(endInput && endInput.value && endInput.value.trim());
        const bothEmpty = !startHasValue && !endHasValue;
        const hasAny = startHasValue || endHasValue;
        const hasValidStart = Number.isFinite(start);
        const hasValidEnd = Number.isFinite(end);
        const isValid = bothEmpty || (hasValidStart && hasValidEnd && end >= start);
        const startChanged = start !== state.drinkingWindowStart;
        const endChanged = end !== state.drinkingWindowEnd;
        const hasChanges = startChanged || endChanged;

        if (startInput) {
            if (!hasGroup || isSaving) {
                startInput.setAttribute('disabled', '');
            } else {
                startInput.removeAttribute('disabled');
            }

            if (!isValid && hasAny && startHasValue) {
                startInput.setAttribute('aria-invalid', 'true');
            } else {
                startInput.removeAttribute('aria-invalid');
            }
        }

        if (endInput) {
            if (!hasGroup || isSaving) {
                endInput.setAttribute('disabled', '');
            } else {
                endInput.removeAttribute('disabled');
            }

            if (!isValid && hasAny && endHasValue) {
                endInput.setAttribute('aria-invalid', 'true');
            } else {
                endInput.removeAttribute('aria-invalid');
            }
        }

        if (saveButton) {
            const shouldDisable = !hasGroup || isSaving || !isValid || !hasChanges;

            if (shouldDisable) {
                saveButton.setAttribute('disabled', '');
                saveButton.setAttribute('aria-disabled', 'true');
            } else {
                saveButton.removeAttribute('disabled');
                saveButton.removeAttribute('aria-disabled');
            }
        }
    };

    const handleDrinkingWindowInputChange = () => {
        refreshDrinkingWindowControls();
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

        const pendingCheckbox = qs(SELECTORS.pendingCheckbox, root);
        if (pendingCheckbox) {
            pendingCheckbox.checked = false;
            pendingCheckbox.setAttribute('disabled', '');
            pendingCheckbox.setAttribute('aria-disabled', 'true');
        }

        resetDrinkingWindowControls(root);
        refreshDrinkingWindowControls();
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

        const pendingCheckbox = qs(SELECTORS.pendingCheckbox);
        if (pendingCheckbox) {
            state.pendingDelivery = pendingCheckbox.checked === true;
        } else {
            state.pendingDelivery = false;
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
        state.pendingDelivery = false;
        resetControls(dialog);
        syncControlState();
        updateAddButtonState();
        updatePendingCheckboxState();
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
        state.pendingDelivery = false;
        resetControls();
        syncControlState();
        updateAddButtonState();
        updatePendingCheckboxState();
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
        refreshDrinkingWindowControls();
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
        if (window.WineInventoryTables?.updateLocationSummaries) {
            try {
                window.WineInventoryTables.updateLocationSummaries(locations);
            } catch (error) {
                console.error('[BottleManagementModal] Failed to sync location summaries', error);
            }
        }
        state.hasGroup = Boolean(group);
        updateSummary(group, details);
        renderRows(details);
        updateAddButtonState();
        refreshDrinkingWindowControls();
    };

    const updateSummary = (group, details) => {
        const wineNameEl = qs(SELECTORS.wineName);
        const vintageEl = qs(SELECTORS.vintageLabel);
        const countEl = qs(SELECTORS.countLabel);
        const statusEl = qs(SELECTORS.statusLabel);
        const averageEl = qs(SELECTORS.averageLabel);
        const drinkingWindowDisplay = qs(SELECTORS.drinkingWindowDisplay);
        const drinkingWindowStartInput = qs(SELECTORS.drinkingWindowStartInput);
        const drinkingWindowEndInput = qs(SELECTORS.drinkingWindowEndInput);
        const separators = qsa(SELECTORS.metaSeparators);

        if (!group) {
            if (wineNameEl) {
                wineNameEl.textContent = 'No bottles found';
            }
            if (vintageEl) {
                vintageEl.textContent = '- —';
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
            if (drinkingWindowDisplay) {
                drinkingWindowDisplay.textContent = 'Drinking window: —';
                drinkingWindowDisplay.removeAttribute('title');
            }
            if (drinkingWindowStartInput) {
                drinkingWindowStartInput.value = '';
            }
            if (drinkingWindowEndInput) {
                drinkingWindowEndInput.value = '';
            }
            state.drinkingWindowStart = null;
            state.drinkingWindowEnd = null;
            state.isSavingDrinkingWindow = false;
            separators.forEach((separator) => {
                separator.hidden = true;
            });
            refreshDrinkingWindowControls();
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
            vintageEl.textContent = `- ${vintage}`;
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

        const rawStart = group.UserDrinkingWindowStartYear ?? group.userDrinkingWindowStartYear ?? null;
        const rawEnd = group.UserDrinkingWindowEndYear ?? group.userDrinkingWindowEndYear ?? null;
        const normalizedStart = normalizeYearValue(rawStart);
        const normalizedEnd = normalizeYearValue(rawEnd);
        state.drinkingWindowStart = normalizedStart;
        state.drinkingWindowEnd = normalizedEnd;

        if (drinkingWindowStartInput) {
            drinkingWindowStartInput.value = normalizedStart != null ? String(normalizedStart) : '';
        }

        if (drinkingWindowEndInput) {
            drinkingWindowEndInput.value = normalizedEnd != null ? String(normalizedEnd) : '';
        }

        if (drinkingWindowDisplay) {
            const formattedStart = formatDisplayYear(normalizedStart);
            const formattedEnd = formatDisplayYear(normalizedEnd);
            let label = 'Drinking window: —';

            if (formattedStart && formattedEnd) {
                label = `Drinking window: ${formattedStart} – ${formattedEnd}`;
            } else if (normalizedStart == null && normalizedEnd == null) {
                label = 'Drinking window: —';
            } else if (formattedStart && !formattedEnd) {
                label = `Drinking window: ${formattedStart}`;
            } else if (!formattedStart && formattedEnd) {
                label = `Drinking window: ${formattedEnd}`;
            }

            drinkingWindowDisplay.textContent = label;
            drinkingWindowDisplay.removeAttribute('title');
        }

        separators.forEach((separator) => {
            separator.hidden = false;
        });

        refreshDrinkingWindowControls();
    };

    const parseNumeric = (value) => {
        if (typeof value === 'number') {
            return Number.isFinite(value) ? value : null;
        }

        if (typeof value === 'string') {
            const parsed = Number.parseFloat(value);
            return Number.isFinite(parsed) ? parsed : null;
        }

        return null;
    };

    const normalizeId = (value) => {
        if (value == null) {
            return '';
        }

        if (typeof value === 'string') {
            return value.trim();
        }

        return String(value).trim();
    };

    const buildBottleLabel = (wineName, vintage) => {
        const segments = [];

        if (wineName) {
            segments.push(wineName);
        }

        if (vintage) {
            segments.push(`Vintage ${vintage}`);
        }

        return segments.join(' · ');
    };

    const buildLocationOptionsMarkup = (selectedId) => {
        const normalizedSelected = typeof selectedId === 'string'
            ? selectedId.trim()
            : '';

        const options = ['<option value="">No location</option>'];

        state.locations.forEach((location) => {
            const value = typeof location?.id === 'string' ? location.id : '';
            if (!value) {
                return;
            }

            const label = buildLocationLabel(location);
            const isSelected = normalizedSelected && normalizedSelected === value;
            options.push(
                `<option value="${escapeHtml(value)}"${isSelected ? ' selected' : ''}>${escapeHtml(label)}</option>`
            );
        });

        return options.join('');
    };

    const renderRows = (details) => {
        const tbody = qs(SELECTORS.tableBody);
        if (!tbody) {
            return;
        }

        bottleDetailMap.clear();

        if (!Array.isArray(details) || details.length === 0) {
            tbody.innerHTML = '<tr class="empty-row"><td colspan="6">You do not have bottles for this vintage yet.</td></tr>';
            return;
        }

        const rows = details.map((detail) => {
            const bottleId = normalizeId(detail?.BottleId ?? detail?.bottleId ?? detail?.Id ?? detail?.id);
            const wineVintageId = normalizeId(detail?.WineVintageId ?? detail?.wineVintageId);
            const locationId = normalizeId(detail?.BottleLocationId ?? detail?.bottleLocationId);
            const location = escapeHtml(detail?.BottleLocation ?? detail?.bottleLocation ?? '—');
            const priceValue = detail?.Price ?? detail?.price;
            const priceNumber = parseNumeric(priceValue);
            const priceInputValue = formatPriceInputValue(priceNumber);
            const scoreValue = detail?.CurrentUserScore ?? detail?.currentUserScore ?? detail?.AverageScore ?? detail?.averageScore;
            const score = formatScore(scoreValue);
            const isDrunk = detail?.IsDrunk ?? detail?.isDrunk ?? false;
            const isPending = detail?.PendingDelivery ?? detail?.pendingDelivery ?? false;
            const drunkAtRaw = detail?.DrunkAt ?? detail?.drunkAt ?? null;
            const status = escapeHtml(formatEnjoyedStatus(isDrunk, drunkAtRaw, isPending));
            const noteId = normalizeId(detail?.CurrentUserNoteId ?? detail?.currentUserNoteId ?? detail?.currentUserNoteID);
            const note = typeof detail?.CurrentUserNote === 'string'
                ? detail.CurrentUserNote
                : typeof detail?.currentUserNote === 'string'
                    ? detail.currentUserNote
                    : '';
            const rawScore = parseNumeric(detail?.CurrentUserScore ?? detail?.currentUserScore);
            const wineName = typeof detail?.WineName === 'string'
                ? detail.WineName
                : typeof detail?.wineName === 'string'
                    ? detail.wineName
                    : '';
            const vintageRaw = detail?.Vintage ?? detail?.vintage;
            const vintageNumeric = parseNumeric(vintageRaw);
            const vintage = Number.isFinite(vintageNumeric) && vintageNumeric > 0
                ? String(Math.trunc(vintageNumeric))
                : '';

            if (bottleId) {
                bottleDetailMap.set(bottleId, {
                    bottleId,
                    wineVintageId,
                    bottleLocationId: locationId || null,
                    price: parseNumeric(priceValue),
                    isDrunk,
                    pendingDelivery: Boolean(isPending),
                    drunkAt: drunkAtRaw || null,
                    noteId: noteId || null,
                    note: note || '',
                    score: rawScore,
                    wineName,
                    vintage
                });
            }

            const actions = bottleId
                ? buildActionButtons(bottleId)
                : '';

            const safeBottleId = bottleId ? escapeHtml(bottleId) : '';
            const locationField = bottleId
                ? (
                    `<div class="bottle-management-inline-field">` +
                        `<label class="sr-only" for="bottle-location-${safeBottleId}">Bottle location</label>` +
                        `<select class="bottle-management-select bottle-management-inline-select" id="bottle-location-${safeBottleId}" data-bottle-management-location-field data-bottle-id="${safeBottleId}" aria-label="Bottle location">${buildLocationOptionsMarkup(locationId)}</select>` +
                    '</div>'
                )
                : location;

            const priceField = bottleId
                ? (
                    `<div class="bottle-management-inline-field">` +
                        `<label class="sr-only" for="bottle-price-${safeBottleId}">Bottle price</label>` +
                        `<input type="text" id="bottle-price-${safeBottleId}" class="bottle-management-price-input" data-bottle-management-price-input data-bottle-id="${safeBottleId}" inputmode="decimal" pattern="[0-9.,]*" placeholder="0.00" autocomplete="off" aria-label="Bottle price" value="${escapeHtml(priceInputValue)}" />` +
                    '</div>'
                )
                : escapeHtml(priceInputValue || '—');

            const pendingField = bottleId
                ? (
                    `<div class="bottle-management-pending-toggle">` +
                        `<input type="checkbox" id="bottle-pending-${safeBottleId}" class="bottle-management-pending-checkbox" data-bottle-management-pending-field data-bottle-id="${safeBottleId}" aria-label="Pending delivery"${isPending ? ' checked' : ''} />` +
                    '</div>'
                )
                : (isPending ? 'Yes' : 'No');

            return (
                `<tr data-bottle-management-row${safeBottleId ? ` data-bottle-id="${safeBottleId}"` : ''}>` +
                    `<td class="bottle-management-col-location">${locationField}</td>` +
                    `<td class="bottle-management-col-price">${priceField}</td>` +
                    `<td class="bottle-management-col-score">${score}</td>` +
                    `<td class="bottle-management-col-pending">${pendingField}</td>` +
                    `<td class="bottle-management-col-status">${status}</td>` +
                    `<td class="bottle-management-col-actions">${actions}</td>` +
                '</tr>'
            );
        }).join('');

        tbody.innerHTML = rows;
    };

    const formatPriceInputValue = (value) => {
        if (!Number.isFinite(value)) {
            return '';
        }

        return Number(value).toFixed(2);
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

    const parseDateValue = (value) => {
        if (value == null || value === '') {
            return null;
        }

        if (value instanceof Date) {
            const time = value.getTime();
            return Number.isFinite(time) ? value : null;
        }

        if (typeof value === 'number') {
            if (!Number.isFinite(value)) {
                return null;
            }

            const fromNumber = new Date(value);
            return Number.isFinite(fromNumber.getTime()) ? fromNumber : null;
        }

        if (typeof value === 'string') {
            const trimmed = value.trim();
            if (!trimmed) {
                return null;
            }

            const parsed = new Date(trimmed);
            return Number.isFinite(parsed.getTime()) ? parsed : null;
        }

        return null;
    };

    const formatEnjoyedStatus = (isDrunk, drunkAt, isPending = false) => {
        if (isPending) {
            return 'Pending delivery';
        }

        if (!isDrunk) {
            return 'No';
        }

        const parsed = parseDateValue(drunkAt);
        if (!parsed) {
            return 'Yes';
        }

        return parsed.toLocaleDateString(undefined, {
            year: 'numeric',
            month: 'short',
            day: 'numeric'
        });
    };

    const getBottleRecord = (bottleId) => {
        const normalized = typeof bottleId === 'string'
            ? bottleId.trim()
            : bottleId != null
                ? String(bottleId).trim()
                : '';

        if (!normalized) {
            return null;
        }

        return bottleDetailMap.get(normalized) ?? null;
    };

    const updateBottle = async (bottleId, updates = {}) => {
        const record = getBottleRecord(bottleId);
        if (!record || !record.wineVintageId) {
            showError('We could not find that bottle in your inventory.');
            return false;
        }

        const payload = {
            wineVintageId: record.wineVintageId,
            price: null,
            isDrunk: updates.isDrunk !== undefined ? Boolean(updates.isDrunk) : Boolean(record.isDrunk),
            drunkAt: updates.drunkAt !== undefined ? updates.drunkAt : (record.drunkAt ?? null),
            pendingDelivery: updates.pendingDelivery !== undefined
                ? Boolean(updates.pendingDelivery)
                : Boolean(record.pendingDelivery),
            bottleLocationId: null
        };

        const nextPrice = updates.price !== undefined ? updates.price : record.price;
        if (nextPrice == null || nextPrice === '') {
            payload.price = null;
        } else if (typeof nextPrice === 'number') {
            payload.price = Number.isFinite(nextPrice) ? Number(nextPrice) : null;
        } else if (typeof nextPrice === 'string') {
            const parsed = Number(nextPrice);
            payload.price = Number.isFinite(parsed) ? parsed : null;
        } else {
            payload.price = null;
        }

        const nextLocation = updates.bottleLocationId !== undefined
            ? updates.bottleLocationId
            : record.bottleLocationId;
        if (nextLocation == null || nextLocation === '') {
            payload.bottleLocationId = null;
        } else if (typeof nextLocation === 'string') {
            payload.bottleLocationId = nextLocation;
        } else {
            payload.bottleLocationId = String(nextLocation);
        }

        if (payload.drunkAt === '') {
            payload.drunkAt = null;
        }

        try {
            const response = await fetch(`/wine-manager/bottles/${bottleId}`, {
                method: 'PUT',
                headers: {
                    'Content-Type': 'application/json',
                    'Accept': 'application/json'
                },
                body: JSON.stringify(payload)
            });

            const raw = await response.text();
            const contentType = response.headers.get('Content-Type') ?? '';
            const isJson = raw && contentType.toLowerCase().includes('application/json');
            let data = null;

            if (raw && isJson) {
                try {
                    data = JSON.parse(raw);
                } catch (parseError) {
                    console.error('Unable to parse bottle update response', parseError);
                }
            }

            if (!response.ok) {
                const fallback = raw?.trim()
                    ? raw.trim()
                    : 'We could not update that bottle right now. Please try again.';
                const message = extractProblemMessage(data, fallback);
                showError(message);
                return false;
            }

            clearError();

            if (data) {
                renderDetails(data);
            } else if (record.wineVintageId) {
                await fetchBottles(record.wineVintageId);
            }

            return true;
        } catch (error) {
            console.error('Failed to update bottle', error);
            showError('We could not update that bottle right now. Please try again.');
            return false;
        }
    };

    const buildActionButtons = (bottleId) => {
        const record = getBottleRecord(bottleId);
        if (!record) {
            return '';
        }

        const safeBottleId = escapeHtml(record.bottleId);
        const wineVintageAttr = record.wineVintageId
            ? ` data-wine-vintage-id="${escapeHtml(record.wineVintageId)}"`
            : '';

        const buttons = [];

        if (!record.isDrunk) {
            const removeButton =
                `<button type="button" class="wine-surfer-button wine-surfer-button--red bottle-management-remove-button" data-bottle-management-remove data-bottle-id="${safeBottleId}"${wineVintageAttr} aria-label="Remove bottle from your inventory">Remove</button>`;
            buttons.push(removeButton);
        }

        if (record.isDrunk) {
            const noteAttr = record.noteId
                ? ` data-note-id="${escapeHtml(record.noteId)}"`
                : '';

            const editNoteButton =
                `<button type="button" class="wine-surfer-button wine-surfer-button--orange bottle-management-edit-note-button" data-bottle-management-edit-note data-bottle-id="${safeBottleId}"${wineVintageAttr}${noteAttr} aria-label="Edit tasting note for this bottle">Edit Note</button>`;
            buttons.push(editNoteButton);

            const undrinkButton =
                `<button type="button" class="wine-surfer-button wine-surfer-button--green bottle-management-undrink-button" data-bottle-management-undrink data-bottle-id="${safeBottleId}"${wineVintageAttr}${noteAttr} aria-label="Mark bottle as not drunk">Undrink</button>`;
            buttons.push(undrinkButton);
            return `<div class="bottle-management-row-actions">${buttons.join('')}</div>`;
        }

        const drinkButton =
            `<button type="button" class="wine-surfer-button wine-surfer-button--orange bottle-management-drink-button" data-bottle-management-drink data-bottle-id="${safeBottleId}"${wineVintageAttr} aria-label="Drink this bottle">Drink</button>`;
        buttons.push(drinkButton);

        return `<div class="bottle-management-row-actions">${buttons.join('')}</div>`;
    };

    const setButtonLoading = (button, isLoading) => {
        if (!(button instanceof HTMLElement)) {
            return;
        }

        if (isLoading) {
            button.dataset.state = 'loading';
            button.setAttribute('aria-busy', 'true');
            button.setAttribute('disabled', 'disabled');
        } else {
            button.removeAttribute('disabled');
            button.removeAttribute('aria-busy');
            delete button.dataset.state;
        }
    };

    const openDrinkModalForBottle = (record) => {
        if (!record) {
            showError('We could not find that bottle in your inventory.');
            return;
        }

        const label = buildBottleLabel(record.wineName, record.vintage);
        const successMessage = record.isDrunk
            ? 'Tasting note updated.'
            : 'Bottle marked as drunk.';
        const detail = {
            context: 'inventory',
            bottleId: record.bottleId,
            label: label || 'Bottle',
            noteId: record.noteId ?? '',
            note: record.note ?? '',
            score: record.score != null ? record.score : '',
            date: record.drunkAt ?? '',
            mode: record.noteId ? 'edit' : 'create',
            requireDate: true,
            successMessage,
            extras: {
                wineVintageId: record.wineVintageId,
                bottleLocationId: record.bottleLocationId,
                price: record.price
            }
        };

        window.dispatchEvent(new CustomEvent('drinkmodal:open', { detail }));
    };

    const handleDrinkClick = (button) => {
        const bottleId = button?.getAttribute('data-bottle-id')
            || button?.dataset?.bottleId
            || '';
        const record = getBottleRecord(bottleId);
        if (!record) {
            showError('We could not find that bottle in your inventory.');
            return;
        }

        openDrinkModalForBottle(record);
    };

    const handleEditNoteClick = (button) => {
        const bottleId = button?.getAttribute('data-bottle-id')
            || button?.dataset?.bottleId
            || '';
        const record = getBottleRecord(bottleId);
        if (!record) {
            showError('We could not find that bottle in your inventory.');
            return;
        }

        if (!record.isDrunk) {
            showError('You can only edit notes for bottles you have marked as drunk.');
            return;
        }

        openDrinkModalForBottle(record);
    };

    const handleRemoveClick = async (button) => {
        const bottleId = button?.getAttribute('data-bottle-id')
            || button?.dataset?.bottleId
            || '';

        if (!bottleId) {
            showError('We could not determine which bottle to remove.');
            return;
        }

        const record = getBottleRecord(bottleId);
        const wineVintageId = button?.getAttribute('data-wine-vintage-id')
            || button?.dataset?.wineVintageId
            || record?.wineVintageId
            || '';

        setButtonLoading(button, true);

        try {
            const response = await fetch(`/wine-manager/bottles/${bottleId}`, {
                method: 'DELETE',
                headers: {
                    'Accept': 'application/json'
                }
            });

            const raw = await response.text();
            const contentType = response.headers.get('Content-Type') ?? '';
            const isJson = contentType.toLowerCase().includes('application/json');
            let data = null;

            if (raw && isJson) {
                try {
                    data = JSON.parse(raw);
                } catch (parseError) {
                    console.error('Unable to parse remove bottle response', parseError);
                }
            }

            if (!response.ok) {
                const fallback = raw?.trim()
                    ? raw.trim()
                    : 'We could not remove that bottle right now. Please try again.';
                const message = extractProblemMessage(data, fallback);
                showError(message);
                return;
            }

            clearError();

            if (data) {
                renderDetails(data);
            } else if (wineVintageId) {
                await fetchBottles(wineVintageId);
            } else if (state.wineVintageId) {
                await fetchBottles(state.wineVintageId);
            }
        } catch (error) {
            console.error('Failed to remove bottle', error);
            showError('We could not remove that bottle right now. Please try again.');
        } finally {
            setButtonLoading(button, false);
        }
    };

    const handleUndrinkClick = async (button) => {
        const bottleId = button?.getAttribute('data-bottle-id')
            || button?.dataset?.bottleId
            || '';

        if (!bottleId) {
            showError('We could not determine which bottle to update.');
            return;
        }

        const record = getBottleRecord(bottleId);
        if (!record || !record.wineVintageId) {
            showError('We could not find that bottle in your inventory.');
            return;
        }

        const payload = {
            wineVintageId: record.wineVintageId,
            price: record.price,
            isDrunk: false,
            drunkAt: null,
            pendingDelivery: Boolean(record.pendingDelivery)
        };

        if (record.bottleLocationId) {
            payload.bottleLocationId = record.bottleLocationId;
        }

        setButtonLoading(button, true);

        try {
            const response = await fetch(`/wine-manager/bottles/${bottleId}`, {
                method: 'PUT',
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
                    console.error('Unable to parse undrink response', parseError);
                }
            }

            if (!response.ok) {
                const fallback = raw?.trim()
                    ? raw.trim()
                    : 'We could not update that bottle right now. Please try again.';
                const message = extractProblemMessage(data, fallback);
                showError(message);
                return;
            }

            if (record.noteId) {
                try {
                    const noteResponse = await fetch(`/wine-manager/notes/${record.noteId}`, {
                        method: 'DELETE',
                        headers: {
                            'Accept': 'application/json'
                        }
                    });

                    if (!noteResponse.ok && noteResponse.status !== 404) {
                        const noteRaw = await noteResponse.text();
                        const noteContentType = noteResponse.headers.get('Content-Type') ?? '';
                        const noteIsJson = noteContentType.toLowerCase().includes('application/json');
                        let noteData = null;

                        if (noteRaw && noteIsJson) {
                            try {
                                noteData = JSON.parse(noteRaw);
                            } catch (parseError) {
                                console.error('Unable to parse tasting note delete response', parseError);
                            }
                        }

                        const fallback = noteRaw?.trim()
                            ? noteRaw.trim()
                            : 'We could not delete your tasting note. Please try again.';
                        const message = extractProblemMessage(noteData, fallback);
                        showError(message);
                        return;
                    }
                } catch (noteError) {
                    console.error('Failed to delete tasting note', noteError);
                    showError('We could not delete your tasting note. Please try again.');
                    return;
                }
            }

            clearError();
            await fetchBottles(record.wineVintageId);
        } catch (error) {
            console.error('Failed to undrink bottle', error);
            showError('We could not update that bottle right now. Please try again.');
        } finally {
            setButtonLoading(button, false);
        }
    };

    const handleLocationFieldChange = async (select) => {
        if (!(select instanceof HTMLSelectElement)) {
            return;
        }

        const bottleId = select.getAttribute('data-bottle-id')
            || select.dataset?.bottleId
            || '';
        if (!bottleId) {
            return;
        }

        const record = getBottleRecord(bottleId);
        if (!record) {
            showError('We could not find that bottle in your inventory.');
            select.value = '';
            return;
        }

        const newValueRaw = typeof select.value === 'string' ? select.value.trim() : '';
        const currentValue = record.bottleLocationId ?? '';
        if (newValueRaw === currentValue || (!newValueRaw && !currentValue)) {
            select.value = currentValue;
            return;
        }

        select.setAttribute('disabled', '');
        select.dataset.state = 'saving';

        let success = false;
        try {
            success = await updateBottle(bottleId, {
                bottleLocationId: newValueRaw ? newValueRaw : null
            });
        } catch (error) {
            console.error('Failed to update bottle location', error);
            showError('We could not update that bottle right now. Please try again.');
        } finally {
            if (!success) {
                select.value = currentValue;
            }

            select.removeAttribute('disabled');
            delete select.dataset.state;
        }
    };

    const handlePriceInputChange = async (input) => {
        if (!(input instanceof HTMLInputElement)) {
            return;
        }

        const bottleId = input.getAttribute('data-bottle-id')
            || input.dataset?.bottleId
            || '';
        if (!bottleId) {
            return;
        }

        const record = getBottleRecord(bottleId);
        if (!record) {
            showError('We could not find that bottle in your inventory.');
            input.value = '';
            return;
        }

        const rawValue = typeof input.value === 'string' ? input.value : '';
        const trimmed = rawValue.trim();
        let normalizedPrice = null;

        if (trimmed) {
            const sanitized = trimmed.replace(/[^0-9.,]/g, '');
            if (!sanitized) {
                showError('Enter a valid price using numbers.');
                input.value = formatPriceInputValue(record.price);
                return;
            }

            let normalized = sanitized;
            const lastDot = sanitized.lastIndexOf('.');
            const lastComma = sanitized.lastIndexOf(',');

            if (lastDot !== -1 || lastComma !== -1) {
                if (lastDot > lastComma) {
                    normalized = sanitized.replace(/,/g, '');
                } else {
                    const parts = sanitized.split(',');
                    const lastPart = parts[parts.length - 1];
                    const isLikelyThousands = sanitized.indexOf('.') === -1
                        && parts.length > 1
                        && lastPart.length === 3
                        && parts.slice(0, -1).every((segment) => segment.length > 0 && segment.length <= 3);

                    if (isLikelyThousands) {
                        normalized = sanitized.replace(/,/g, '');
                    } else {
                        normalized = sanitized.replace(/\./g, '').replace(/,/g, '.');
                    }
                }
            }

            const hasDigits = /\d/.test(normalized);
            if (!hasDigits || !/^\d*(\.\d{0,2})?$/.test(normalized)) {
                showError('Enter a valid price using numbers (up to two decimals).');
                input.value = formatPriceInputValue(record.price);
                return;
            }

            const numeric = Number.parseFloat(normalized);
            if (!Number.isFinite(numeric)) {
                showError('Enter a valid price using numbers.');
                input.value = formatPriceInputValue(record.price);
                return;
            }

            normalizedPrice = Math.round(numeric * 100) / 100;
        }

        const currentPrice = Number.isFinite(record.price)
            ? Math.round(record.price * 100) / 100
            : null;

        if (normalizedPrice === currentPrice) {
            input.value = formatPriceInputValue(record.price);
            return;
        }

        input.value = formatPriceInputValue(normalizedPrice);
        input.setAttribute('disabled', '');
        input.dataset.state = 'saving';

        let success = false;
        try {
            success = await updateBottle(bottleId, { price: normalizedPrice });
        } catch (error) {
            console.error('Failed to update bottle price', error);
            showError('We could not update that bottle right now. Please try again.');
        } finally {
            if (!success) {
                input.value = formatPriceInputValue(record.price);
            }

            input.removeAttribute('disabled');
            delete input.dataset.state;
        }
    };

    const handlePendingFieldChange = async (input) => {
        if (!(input instanceof HTMLInputElement)) {
            return;
        }

        const bottleId = input.getAttribute('data-bottle-id')
            || input.dataset?.bottleId
            || '';
        if (!bottleId) {
            return;
        }

        const record = getBottleRecord(bottleId);
        if (!record) {
            showError('We could not find that bottle in your inventory.');
            input.checked = false;
            return;
        }

        const desiredState = input.checked === true;
        const currentState = Boolean(record.pendingDelivery);
        if (desiredState === currentState) {
            input.checked = currentState;
            return;
        }

        input.setAttribute('disabled', '');
        input.dataset.state = 'saving';

        let success = false;
        try {
            success = await updateBottle(bottleId, { pendingDelivery: desiredState });
        } catch (error) {
            console.error('Failed to update pending delivery state', error);
            showError('We could not update that bottle right now. Please try again.');
        } finally {
            if (!success) {
                input.checked = currentState;
            }

            input.removeAttribute('disabled');
            delete input.dataset.state;
        }
    };

    const wireTableBodyActions = () => {
        const tbody = qs(SELECTORS.tableBody);
        if (!tbody) {
            return;
        }

        tbody.addEventListener('click', (event) => {
            const element = event.target instanceof Element
                ? event.target
                : null;

            if (!element) {
                return;
            }

            const removeButton = element.closest('[data-bottle-management-remove]');
            if (removeButton) {
                event.preventDefault();
                void handleRemoveClick(removeButton);
                return;
            }

            const editNoteButton = element.closest('[data-bottle-management-edit-note]');
            if (editNoteButton) {
                event.preventDefault();
                handleEditNoteClick(editNoteButton);
                return;
            }

            const drinkButton = element.closest('[data-bottle-management-drink]');
            if (drinkButton) {
                event.preventDefault();
                handleDrinkClick(drinkButton);
                return;
            }

            const undrinkButton = element.closest('[data-bottle-management-undrink]');
            if (undrinkButton) {
                event.preventDefault();
                void handleUndrinkClick(undrinkButton);
            }
        });

        tbody.addEventListener('change', (event) => {
            const element = event.target instanceof Element
                ? event.target
                : null;

            if (!element) {
                return;
            }

            if (element.matches(SELECTORS.rowLocationSelect)) {
                event.preventDefault();
                void handleLocationFieldChange(element);
                return;
            }

            if (element.matches(SELECTORS.rowPriceInput)) {
                event.preventDefault();
                void handlePriceInputChange(element);
                return;
            }

            if (element.matches(SELECTORS.rowPendingCheckbox)) {
                event.preventDefault();
                void handlePendingFieldChange(element);
            }
        });
    };

    const handleDrinkModalSubmit = (event) => {
        const detail = event?.detail ?? {};
        if ((detail.context ?? 'external') !== 'inventory') {
            return;
        }

        event.preventDefault();

        const bottleId = detail.bottleId ?? '';
        const record = getBottleRecord(bottleId);

        if (!record || !record.wineVintageId) {
            if (typeof detail.showError === 'function') {
                detail.showError('We could not find that bottle in your inventory.');
            }
            return;
        }

        const normalizedDate = typeof detail.date === 'string' && detail.date.trim()
            ? detail.date.trim()
            : null;
        const normalizedNote = typeof detail.note === 'string'
            ? detail.note.trim()
            : '';
        const normalizedScore = typeof detail.score === 'number' && Number.isFinite(detail.score)
            ? detail.score
            : null;
        const rawNoteId = detail.noteId ?? record.noteId ?? null;
        const normalizedNoteId = typeof rawNoteId === 'string'
            ? rawNoteId.trim()
            : rawNoteId != null
                ? String(rawNoteId).trim()
                : '';

        const payload = {
            wineVintageId: record.wineVintageId,
            price: record.price,
            isDrunk: true,
            drunkAt: normalizedDate,
            pendingDelivery: false
        };

        if (record.bottleLocationId) {
            payload.bottleLocationId = record.bottleLocationId;
        }

        const shouldSaveNote = normalizedNote.length > 0 || normalizedScore != null;
        const shouldDeleteNote = !shouldSaveNote && normalizedNoteId.length > 0;

        const submitPromise = (async () => {
            const updateResponse = await fetch(`/wine-manager/bottles/${record.bottleId}/drink`, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'Accept': 'application/json'
                },
                body: JSON.stringify(payload)
            });

            const updateRaw = await updateResponse.text();
            const updateContentType = updateResponse.headers.get('Content-Type') ?? '';
            const updateIsJson = updateContentType.toLowerCase().includes('application/json');
            let updateData = null;

            if (updateRaw && updateIsJson) {
                try {
                    updateData = JSON.parse(updateRaw);
                } catch (parseError) {
                    console.error('Unable to parse drink bottle response', parseError);
                }
            }

            if (!updateResponse.ok) {
                const fallback = updateRaw?.trim()
                    ? updateRaw.trim()
                    : 'We could not mark that bottle as drunk. Please try again.';
                const message = extractProblemMessage(updateData, fallback);
                throw new Error(message);
            }

            if (shouldSaveNote) {
                const notePayload = {
                    note: normalizedNote,
                    score: normalizedScore
                };

                if (normalizedNoteId) {
                    const noteResponse = await fetch(`/wine-manager/notes/${normalizedNoteId}`, {
                        method: 'PUT',
                        headers: {
                            'Content-Type': 'application/json',
                            'Accept': 'application/json'
                        },
                        body: JSON.stringify(notePayload)
                    });

                    const noteRaw = await noteResponse.text();
                    const noteContentType = noteResponse.headers.get('Content-Type') ?? '';
                    const noteIsJson = noteContentType.toLowerCase().includes('application/json');
                    let noteData = null;

                    if (noteRaw && noteIsJson) {
                        try {
                            noteData = JSON.parse(noteRaw);
                        } catch (parseError) {
                            console.error('Unable to parse tasting note update response', parseError);
                        }
                    }

                    if (!noteResponse.ok) {
                        const fallback = noteRaw?.trim()
                            ? noteRaw.trim()
                            : 'We could not save your tasting note. Please try again.';
                        const message = extractProblemMessage(noteData, fallback);
                        throw new Error(message);
                    }
                } else {
                    const createPayload = {
                        bottleId: record.bottleId,
                        note: normalizedNote,
                        score: normalizedScore
                    };

                    const noteResponse = await fetch('/wine-manager/notes', {
                        method: 'POST',
                        headers: {
                            'Content-Type': 'application/json',
                            'Accept': 'application/json'
                        },
                        body: JSON.stringify(createPayload)
                    });

                    const noteRaw = await noteResponse.text();
                    const noteContentType = noteResponse.headers.get('Content-Type') ?? '';
                    const noteIsJson = noteContentType.toLowerCase().includes('application/json');
                    let noteData = null;

                    if (noteRaw && noteIsJson) {
                        try {
                            noteData = JSON.parse(noteRaw);
                        } catch (parseError) {
                            console.error('Unable to parse tasting note create response', parseError);
                        }
                    }

                    if (!noteResponse.ok) {
                        const fallback = noteRaw?.trim()
                            ? noteRaw.trim()
                            : 'We could not save your tasting note. Please try again.';
                        const message = extractProblemMessage(noteData, fallback);
                        throw new Error(message);
                    }
                }
            } else if (shouldDeleteNote) {
                const noteResponse = await fetch(`/wine-manager/notes/${normalizedNoteId}`, {
                    method: 'DELETE',
                    headers: {
                        'Accept': 'application/json'
                    }
                });

                if (!noteResponse.ok && noteResponse.status !== 404) {
                    const noteRaw = await noteResponse.text();
                    const noteContentType = noteResponse.headers.get('Content-Type') ?? '';
                    const noteIsJson = noteContentType.toLowerCase().includes('application/json');
                    let noteData = null;

                    if (noteRaw && noteIsJson) {
                        try {
                            noteData = JSON.parse(noteRaw);
                        } catch (parseError) {
                            console.error('Unable to parse tasting note delete response', parseError);
                        }
                    }

                    const fallback = noteRaw?.trim()
                        ? noteRaw.trim()
                        : 'We could not delete your tasting note. Please try again.';
                    const message = extractProblemMessage(noteData, fallback);
                    throw new Error(message);
                }
            }

            await fetchBottles(record.wineVintageId);
            clearError();
            return { message: detail.successMessage ?? 'Bottle marked as drunk.' };
        })();

        if (typeof detail.setSuccessMessage === 'function') {
            detail.setSuccessMessage('Bottle marked as drunk.');
        }

        if (typeof detail.setSubmitPromise === 'function') {
            detail.setSubmitPromise(submitPromise);
        } else {
            submitPromise.catch((error) => {
                const message = error instanceof Error ? error.message : String(error ?? '');
                if (typeof detail.showError === 'function') {
                    detail.showError(message || 'Unable to mark bottle as drunk.');
                }
            });
        }
    };

    const wireDrinkModalSubmit = () => {
        window.addEventListener('drinkmodal:submit', handleDrinkModalSubmit);
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
        tbody.innerHTML = `<tr class="empty-row"><td colspan="6">${safeMessage}</td></tr>`;
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

    const wirePendingCheckbox = () => {
        const checkbox = qs(SELECTORS.pendingCheckbox);
        if (!checkbox) {
            return;
        }

        checkbox.addEventListener('change', () => {
            state.pendingDelivery = checkbox.checked === true;
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

    const updatePendingCheckboxState = () => {
        const checkbox = qs(SELECTORS.pendingCheckbox);
        if (!checkbox) {
            return;
        }

        checkbox.checked = state.pendingDelivery === true;

        const shouldDisable = !state.isOpen
            || !state.wineVintageId
            || !state.hasGroup
            || state.isAdding;

        if (shouldDisable) {
            checkbox.setAttribute('disabled', '');
            checkbox.setAttribute('aria-disabled', 'true');
        } else {
            checkbox.removeAttribute('disabled');
            checkbox.removeAttribute('aria-disabled');
        }
    };

    const updateAddButtonState = () => {
        const button = qs(SELECTORS.addButton);
        if (!button) {
            updatePendingCheckboxState();
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

        updatePendingCheckboxState();
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
                quantity: normalizeQuantity(state.quantity),
                pendingDelivery: state.pendingDelivery === true
            };

            state.quantity = payload.quantity;

            const normalizedLocationId = typeof state.selectedLocationId === 'string'
                ? state.selectedLocationId.trim()
                : '';

            payload.bottleLocationId = normalizedLocationId ? normalizedLocationId : null;

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

    const saveDrinkingWindow = async () => {
        if (!state.isOpen || state.isSavingDrinkingWindow || !state.wineVintageId) {
            return;
        }

        const { start, end } = getDrinkingWindowDraft();
        const bothEmpty = start == null && end == null;
        const hasBoth = start != null && end != null;
        if (!bothEmpty && !hasBoth) {
            showError('Enter both a start and end year for the drinking window.');
            return;
        }

        if (start != null && end != null && end < start) {
            showError('Drinking window end year must be on or after the start year.');
            return;
        }

        state.isSavingDrinkingWindow = true;
        refreshDrinkingWindowControls();

        try {
            const response = await fetch(`/wine-manager/bottles/${state.wineVintageId}/drinking-window`, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'Accept': 'application/json'
                },
                body: JSON.stringify({
                    startYear: start ?? null,
                    endYear: end ?? null
                })
            });

            const raw = await response.text();
            const contentType = response.headers.get('Content-Type') ?? '';
            const isJson = raw && contentType.toLowerCase().includes('application/json');
            let data = null;

            if (raw && isJson) {
                try {
                    data = JSON.parse(raw);
                } catch (parseError) {
                    console.error('Unable to parse drinking window response', parseError);
                }
            }

            if (!response.ok) {
                const fallback = raw?.trim() ? raw.trim() : 'We could not save your drinking window. Please try again.';
                const message = extractProblemMessage(data, fallback);
                showError(message);
                return;
            }

            clearError();

            if (data) {
                renderDetails(data);
            } else if (state.wineVintageId) {
                await fetchBottles(state.wineVintageId);
            }
        } catch (error) {
            console.error('Failed to save drinking window', error);
            showError('We could not save your drinking window. Please try again.');
        } finally {
            state.isSavingDrinkingWindow = false;
            refreshDrinkingWindowControls();
        }
    };

    const wireDrinkingWindowControls = () => {
        const startInput = qs(SELECTORS.drinkingWindowStartInput);
        const endInput = qs(SELECTORS.drinkingWindowEndInput);
        const saveButton = qs(SELECTORS.drinkingWindowSaveButton);

        if (startInput) {
            startInput.addEventListener('input', handleDrinkingWindowInputChange);
            startInput.addEventListener('change', handleDrinkingWindowInputChange);
        }

        if (endInput) {
            endInput.addEventListener('input', handleDrinkingWindowInputChange);
            endInput.addEventListener('change', handleDrinkingWindowInputChange);
        }

        if (saveButton) {
            saveButton.addEventListener('click', (event) => {
                event.preventDefault();
                saveDrinkingWindow();
            });
        }

        refreshDrinkingWindowControls();
    };

    document.addEventListener('DOMContentLoaded', () => {
        wireCloseButtons();
        wireTriggers();
        wireLocationSelect();
        wireQuantitySelect();
        wirePendingCheckbox();
        wireAddButton();
        wireDrinkingWindowControls();
        wireTableBodyActions();
        wireDrinkModalSubmit();
        syncControlState();
        updatePendingCheckboxState();
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
