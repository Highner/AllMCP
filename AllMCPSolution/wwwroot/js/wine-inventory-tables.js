(function () {
    const MAX_LOCATION_CAPACITY = 10000;

    window.WineInventoryTables = window.WineInventoryTables || {};
    window.WineInventoryTables.initialize = function initializeWineInventoryTables() {
        if (window.WineInventoryTables.__initialized) {
            return;
        }

        window.WineInventoryTables.__initialized = true;

        const filtersForm = document.querySelector('form.filters');
        const clearFiltersButton = filtersForm?.querySelector('[data-clear-filters]');
        const headerFilterInput = document.querySelector('[data-inventory-header-filter-input]');
        const headerFilterClearButton = document.querySelector('[data-inventory-header-filter-clear]');
        const filtersSearchInput = filtersForm?.querySelector('input[name="search"]');
        const filtersStatusInput = filtersForm?.querySelector('input[name="status"]');
        const filtersLocationInput = filtersForm?.querySelector('input[name="locationId"]');
        const inventoryView = document.getElementById('inventory-view');
        const statusToggleButton = document.querySelector('[data-inventory-status-toggle]');
        const statusToggleLabel = statusToggleButton?.querySelector('[data-inventory-status-toggle-label]');
        const statusToggleHint = statusToggleButton?.querySelector('[data-inventory-status-toggle-hint]');
        const statusToggleSr = statusToggleButton?.querySelector('[data-inventory-status-toggle-sr]');
        const headerFilterFocusStorageKey = 'wine-inventory:restore-header-focus';
        let headerFilterDebounceId = null;
        let maintainHeaderFilterFocus = false;
        let activeLocationFilterId = filtersLocationInput?.value ?? '';
        let inlineDetailsAbortController = null;

        const STATUS_TOGGLE_COPY = {
            all: {
                label: 'All bottles',
                hint: 'Tap to show cellared only',
                announcement: 'Showing all bottles. Activate to show only cellared bottles.',
            },
            cellared: {
                label: 'Cellared only',
                hint: 'Tap to show all bottles',
                announcement: 'Showing only cellared bottles. Activate to show all bottles.',
            },
        };

        function normalizeStatusValue(value) {
            if (typeof value !== 'string') {
                return 'all';
            }

            const normalized = value.trim().toLowerCase();
            switch (normalized) {
                case 'pending':
                case 'drunk':
                case 'cellared':
                    return normalized;
                case 'undrunk':
                    return 'cellared';
                default:
                    return 'all';
            }
        }

        function getActiveLocationFilterId() {
            if (typeof activeLocationFilterId !== 'string') {
                return '';
            }

            return activeLocationFilterId.trim();
        }

        function buildVintageCacheKey(status, locationId) {
            const normalizedStatus = normalizeStatusValue(status);
            const normalizedLocation = typeof locationId === 'string'
                ? locationId.trim().toLowerCase()
                : '';

            return `${normalizedStatus}::${normalizedLocation || 'all'}`;
        }

        function getActiveStatusFilter() {
            const raw = filtersStatusInput?.value ?? statusToggleButton?.dataset.state ?? 'all';
            return normalizeStatusValue(raw);
        }

        function updateStatusToggleUI(status) {
            if (!statusToggleButton) {
                return;
            }

            const normalized = normalizeStatusValue(status);
            const copy = STATUS_TOGGLE_COPY[normalized] ?? STATUS_TOGGLE_COPY.all;

            statusToggleButton.dataset.state = normalized;
            statusToggleButton.setAttribute('aria-pressed', normalized === 'cellared' ? 'true' : 'false');
            statusToggleButton.setAttribute('aria-label', copy.announcement);

            if (statusToggleLabel) {
                statusToggleLabel.textContent = copy.label;
            }

            if (statusToggleHint) {
                statusToggleHint.textContent = copy.hint;
            }

            if (statusToggleSr) {
                statusToggleSr.textContent = copy.announcement;
            }
        }

        function updateHeaderFilterClearButtonVisibility() {
            if (!headerFilterInput || !headerFilterClearButton) {
                return;
            }

            const hasValue = headerFilterInput.value.trim().length > 0;
            headerFilterClearButton.hidden = !hasValue;
        }

        function formatCountWithLabel(count, singular, plural) {
            const safeCount = Number.isFinite(count) ? count : 0;
            const formatted = safeCount.toLocaleString();
            const noun = safeCount === 1 ? singular : plural;
            return `${formatted} ${noun}`;
        }

        function buildBottleCountCopy(pending, cellared, drunk, statusFilter) {
            const normalizedStatus = normalizeStatusValue(statusFilter);
            const safePending = Number.isFinite(pending) ? pending : 0;
            const safeCellared = Number.isFinite(cellared) ? cellared : 0;
            const safeDrunk = Number.isFinite(drunk) ? drunk : 0;
            const pendingFormatted = safePending.toLocaleString();
            const cellaredFormatted = safeCellared.toLocaleString();
            const drunkFormatted = safeDrunk.toLocaleString();
            const pendingPart = formatCountWithLabel(safePending, 'pending bottle', 'pending bottles');
            const cellaredPart = formatCountWithLabel(safeCellared, 'cellared bottle', 'cellared bottles');
            const drunkPart = formatCountWithLabel(safeDrunk, 'enjoyed bottle', 'enjoyed bottles');

            switch (normalizedStatus) {
                case 'pending':
                    return {
                        hidden: pendingFormatted,
                        accessible: pendingPart,
                        ariaLabelSuffix: `(${pendingFormatted} pending)`
                    };
                case 'cellared':
                    return {
                        hidden: `${pendingFormatted}/${cellaredFormatted}`,
                        accessible: `${pendingPart}, ${cellaredPart}`,
                        ariaLabelSuffix: `(${pendingFormatted} pending, ${cellaredFormatted} cellared)`
                    };
                case 'drunk':
                    return {
                        hidden: drunkFormatted,
                        accessible: drunkPart,
                        ariaLabelSuffix: `(${drunkFormatted} enjoyed)`
                    };
                default:
                    return {
                        hidden: `${pendingFormatted}/${cellaredFormatted}/${drunkFormatted}`,
                        accessible: `${pendingPart}, ${cellaredPart}, ${drunkPart}`,
                        ariaLabelSuffix: `(${pendingFormatted} pending, ${cellaredFormatted} cellared, ${drunkFormatted} enjoyed)`
                    };
            }
        }

        function cancelInlineDetailsRequest() {
            if (inlineDetailsAbortController) {
                inlineDetailsAbortController.abort();
                inlineDetailsAbortController = null;
            }
        }

        function setActiveLocationFilter(value) {
            if (typeof value === 'string') {
                activeLocationFilterId = value;
            } else {
                activeLocationFilterId = '';
            }

            updateLocationFilterHighlights();
        }

        function syncLocationSectionDataset() {
            if (!locationSection) {
                return;
            }

            if (activeLocationFilterId) {
                locationSection.dataset.activeLocationId = activeLocationFilterId;
            } else if ('activeLocationId' in locationSection.dataset) {
                delete locationSection.dataset.activeLocationId;
            }
        }

        function updateLocationFilterHighlights() {
            if (!locationList) {
                return;
            }

            const normalizedActiveId = typeof activeLocationFilterId === 'string'
                ? activeLocationFilterId.trim()
                : '';

            const cards = Array.from(locationList.querySelectorAll('[data-location-card]'));
            cards.forEach((card) => {
                const cardId = (card.dataset.locationId ?? '').trim();
                const shouldHighlight = normalizedActiveId.length > 0
                    && cardId.length > 0
                    && cardId.localeCompare(normalizedActiveId, undefined, { sensitivity: 'accent' }) === 0;

                card.classList.toggle('location-card--highlight', shouldHighlight);

                if (shouldHighlight) {
                    card.setAttribute('aria-current', 'true');
                } else {
                    card.removeAttribute('aria-current');
                }
            });
        }

        function toggleLocationFilter(locationId) {
            if (!filtersLocationInput) {
                return;
            }

            const normalizedId = typeof locationId === 'string' ? locationId.trim() : '';
            const currentValue = (filtersLocationInput.value ?? '').trim();

            if (!normalizedId && !currentValue) {
                return;
            }

            const isClearing = normalizedId && normalizedId.localeCompare(currentValue, undefined, { sensitivity: 'accent' }) === 0;
            const nextValue = isClearing ? '' : normalizedId;
            filtersLocationInput.value = nextValue;
            setActiveLocationFilter(nextValue);
            syncLocationSectionDataset();

            if (headerFilterDebounceId) {
                window.clearTimeout(headerFilterDebounceId);
                headerFilterDebounceId = null;
            }

            maintainHeaderFilterFocus = false;
            cancelInlineDetailsRequest();
            submitFiltersForm();
        }

        function focusHeaderFilterInput() {
            if (!headerFilterInput) {
                return;
            }

            try {
                headerFilterInput.focus({ preventScroll: true });
            } catch (error) {
                headerFilterInput.focus();
            }

            if (typeof headerFilterInput.setSelectionRange === 'function') {
                const length = headerFilterInput.value.length;
                headerFilterInput.setSelectionRange(length, length);
            }
        }

        function submitFiltersForm() {
            if (!filtersForm) {
                return;
            }

            if (headerFilterInput) {
                try {
                    if (document.activeElement === headerFilterInput) {
                        sessionStorage.setItem(headerFilterFocusStorageKey, 'true');
                        maintainHeaderFilterFocus = true;
                    } else {
                        sessionStorage.removeItem(headerFilterFocusStorageKey);
                        maintainHeaderFilterFocus = false;
                    }
                } catch (error) {
                    // Ignore storage failures (e.g., disabled cookies).
                    maintainHeaderFilterFocus = document.activeElement === headerFilterInput;
                }
            }

            if (maintainHeaderFilterFocus) {
                window.requestAnimationFrame(() => focusHeaderFilterInput());
            }

            cancelInlineDetailsRequest();

            if (typeof filtersForm.requestSubmit === 'function') {
                filtersForm.requestSubmit();
            } else {
                filtersForm.submit();
            }
        }

        updateStatusToggleUI(filtersStatusInput?.value ?? statusToggleButton?.dataset.state ?? 'all');

        if (statusToggleButton && filtersForm && filtersStatusInput) {
            statusToggleButton.addEventListener('click', (event) => {
                event.preventDefault();

                const current = normalizeStatusValue(
                    filtersStatusInput.value || statusToggleButton.dataset.state,
                );
                const next = current === 'cellared' ? 'all' : 'cellared';

                filtersStatusInput.value = next;
                updateStatusToggleUI(next);

                maintainHeaderFilterFocus = false;
                cancelInlineDetailsRequest();
                submitFiltersForm();
            });
        }

        if (headerFilterInput) {
            let shouldRestoreFocus = false;

            try {
                shouldRestoreFocus = sessionStorage.getItem(headerFilterFocusStorageKey) === 'true';
            } catch (error) {
                shouldRestoreFocus = false;
            }

            if (shouldRestoreFocus) {
                maintainHeaderFilterFocus = true;
                window.requestAnimationFrame(() => {
                    focusHeaderFilterInput();
                    maintainHeaderFilterFocus = false;
                });

                try {
                    sessionStorage.removeItem(headerFilterFocusStorageKey);
                } catch (error) {
                    // Ignore storage failures.
                }
            }

            headerFilterInput.addEventListener('blur', () => {
                if (maintainHeaderFilterFocus) {
                    return;
                }

                try {
                    sessionStorage.removeItem(headerFilterFocusStorageKey);
                } catch (error) {
                    // Ignore storage failures.
                }

                maintainHeaderFilterFocus = false;
            });
        }

        if (headerFilterInput && headerFilterClearButton) {
            updateHeaderFilterClearButtonVisibility();

            headerFilterClearButton.addEventListener('click', (event) => {
                event.preventDefault();

                if (headerFilterInput.value.length === 0) {
                    focusHeaderFilterInput();
                    return;
                }

                if (headerFilterDebounceId) {
                    window.clearTimeout(headerFilterDebounceId);
                    headerFilterDebounceId = null;
                }

                headerFilterInput.value = '';
                updateHeaderFilterClearButtonVisibility();
                headerFilterInput.dispatchEvent(new Event('input', { bubbles: true }));
                focusHeaderFilterInput();
            });
        }

        if (filtersForm && clearFiltersButton) {
            clearFiltersButton.addEventListener('click', (event) => {
                event.preventDefault();
                const controls = Array.from(filtersForm.querySelectorAll('[data-default-value]'));

                controls.forEach((control) => {
                    const defaultValue = control.getAttribute('data-default-value');
                    if (defaultValue == null) {
                        return;
                    }

                    if (
                        control instanceof HTMLInputElement
                        || control instanceof HTMLSelectElement
                        || control instanceof HTMLTextAreaElement
                    ) {
                        control.value = defaultValue;
                    }
                });

                updateStatusToggleUI(filtersStatusInput?.value ?? 'all');

                if (filtersLocationInput) {
                    setActiveLocationFilter(filtersLocationInput.value ?? '');
                    syncLocationSectionDataset();
                }

                if (headerFilterDebounceId) {
                    window.clearTimeout(headerFilterDebounceId);
                    headerFilterDebounceId = null;
                }

                if (filtersSearchInput && headerFilterInput) {
                    headerFilterInput.value = filtersSearchInput.value;
                    updateHeaderFilterClearButtonVisibility();
                }

                maintainHeaderFilterFocus = true;
                cancelInlineDetailsRequest();
                submitFiltersForm();
            });
        }

        if (filtersForm && filtersSearchInput && headerFilterInput) {
            headerFilterInput.value = filtersSearchInput.value;
            updateHeaderFilterClearButtonVisibility();

            const syncHeaderFilterWithSearch = () => {
                if (document.activeElement !== headerFilterInput) {
                    headerFilterInput.value = filtersSearchInput.value;
                    updateHeaderFilterClearButtonVisibility();
                }
            };

            filtersSearchInput.addEventListener('input', syncHeaderFilterWithSearch);
            filtersSearchInput.addEventListener('change', syncHeaderFilterWithSearch);

            headerFilterInput.addEventListener('input', () => {
                updateHeaderFilterClearButtonVisibility();
                filtersSearchInput.value = headerFilterInput.value;

                if (headerFilterDebounceId) {
                    window.clearTimeout(headerFilterDebounceId);
                }

                headerFilterDebounceId = null;
                maintainHeaderFilterFocus = true;
                cancelInlineDetailsRequest();

                const trimmed = headerFilterInput.value.trim();

                if (trimmed.length >= 3 || trimmed.length === 0) {
                    headerFilterDebounceId = window.setTimeout(() => {
                        submitFiltersForm();
                    }, 500);
                }
            });

            headerFilterInput.addEventListener('keydown', (event) => {
                if (event.key !== 'Enter') {
                    return;
                }

                const trimmed = headerFilterInput.value.trim();

                if (trimmed.length >= 3 || trimmed.length === 0) {
                    event.preventDefault();

                    if (headerFilterDebounceId) {
                        window.clearTimeout(headerFilterDebounceId);
                        headerFilterDebounceId = null;
                    }

                    maintainHeaderFilterFocus = true;
                    cancelInlineDetailsRequest();
                    filtersSearchInput.value = headerFilterInput.value;
                    submitFiltersForm();
                }
            });

            filtersForm.addEventListener('submit', () => {
                if (headerFilterDebounceId) {
                    window.clearTimeout(headerFilterDebounceId);
                    headerFilterDebounceId = null;
                }

                maintainHeaderFilterFocus = maintainHeaderFilterFocus
                    || document.activeElement === headerFilterInput;
                cancelInlineDetailsRequest();
            });
        }

        if (inventoryView && headerFilterInput && typeof MutationObserver === 'function') {
            const observer = new MutationObserver(() => {
                if (!maintainHeaderFilterFocus) {
                    return;
                }

                if (document.activeElement !== headerFilterInput) {
                    focusHeaderFilterInput();
                }

                maintainHeaderFilterFocus = false;
            });

            observer.observe(inventoryView, { childList: true, subtree: true });
        }

        const locationSection = document.getElementById('inventory-locations');
        const locationList = locationSection?.querySelector('[data-location-list]');
        const locationTemplate = document.getElementById('inventory-location-template');
        const locationMessage = locationSection?.querySelector('[data-location-message]');
        const locationEmpty = locationSection?.querySelector('[data-location-empty]');
        const locationCreateCard = locationSection?.querySelector('[data-location-create]');
        const locationCreateForm = locationCreateCard?.querySelector('[data-location-create-form]');
        const locationCreateInput = locationCreateForm?.querySelector('[data-location-input]');
        const locationCreateCapacity = locationCreateForm?.querySelector('[data-location-capacity-input]');
        const locationCreateCancel = locationCreateForm?.querySelector('[data-location-cancel]');
        const locationAddButton = locationSection?.querySelector('[data-location-add]');
        const currentUserId = locationSection?.dataset?.currentUserId
            ?? locationSection?.getAttribute('data-current-user-id')
            ?? '';
        const addWineLocationSelect = document.querySelector('.inventory-add-location');
        const headerActionMenus = Array.from(
            document.querySelectorAll('[data-inventory-actions-menu] [data-action-menu]')
        );

        const referenceData = {
            bottleLocations: []
        };

        const inventoryTable = document.getElementById('inventory-table');
        const inventoryInlineTemplate = document.getElementById('inventory-wine-vintages-template');
        const inventoryInlineRowTemplate = document.getElementById('inventory-wine-vintage-row-template');
        const vintageSummaryCache = new Map();
        let expandedInventoryRow = null;
        let activeActionMenu = null;
        let actionMenuEventsBound = false;

        function formatLastDrinkingWindowDisplay(isoString) {
            if (typeof isoString !== 'string') {
                return null;
            }

            const trimmed = isoString.trim();
            if (!trimmed) {
                return null;
            }

            const parsed = new Date(trimmed);
            if (Number.isNaN(parsed.getTime())) {
                return null;
            }

            try {
                const formatter = new Intl.DateTimeFormat(undefined, {
                    dateStyle: 'medium'
                });

                return formatter.format(parsed);
            } catch {
                return parsed.toLocaleDateString();
            }
        }

        function extractGroupLastGeneratedIso(group) {
            if (!group || typeof group !== 'object') {
                return '';
            }

            const candidates = [
                group.lastDrinkingWindowGeneratedAtUtc,
                group.lastDrinkingWindowGeneratedAtUTC,
                group.lastDrinkingWindowGeneratedAt,
                group.drinkingWindowGeneratedAtUtc,
                group.drinkingWindowGeneratedAtUTC,
                group.drinkingWindowGeneratedAt,
                group.lastGeneratedAtUtc,
                group.lastGeneratedAtUTC,
                group.lastGeneratedAt
            ];

            for (const candidate of candidates) {
                if (typeof candidate === 'string' && candidate.trim().length > 0) {
                    return candidate.trim();
                }
            }

            return '';
        }

        function updateDrinkingWindowButtonMeta(button, wineId, isoOverride, options) {
            if (!(button instanceof HTMLButtonElement)) {
                return;
            }

            const settings = typeof options === 'object' && options !== null ? options : {};
            const skipTitle = settings.skipTitle === true;
            const baseAriaLabel = button.dataset.originalAriaLabel
                ?? button.dataset.originalLabel
                ?? button.getAttribute('aria-label')
                ?? 'Generate drinking windows';

            let isoCandidate = typeof isoOverride === 'string' ? isoOverride : '';
            if (!isoCandidate) {
                isoCandidate = button.dataset.lastGeneratedAt ?? '';
            }

            let normalizedIso = '';
            if (isoCandidate) {
                const parsed = new Date(isoCandidate);
                if (!Number.isNaN(parsed.getTime())) {
                    normalizedIso = parsed.toISOString();
                }
            }

            if (normalizedIso) {
                button.dataset.lastGeneratedAt = normalizedIso;
                const display = formatLastDrinkingWindowDisplay(normalizedIso);

                if (display) {
                    const meta = button.querySelector('[data-last-generated-label]');
                    if (meta) {
                        meta.textContent = display;
                        meta.hidden = false;
                    }

                    button.setAttribute('aria-label', `${baseAriaLabel}. Last generated on ${display}.`);

                    if (!skipTitle) {
                        button.title = display;
                    }

                    return;
                }
            } else if ('lastGeneratedAt' in button.dataset) {
                delete button.dataset.lastGeneratedAt;
            }
            const meta = button.querySelector('[data-last-generated-label]');
            if (meta) {
                meta.textContent = '';
                meta.hidden = true;
            }

            button.setAttribute('aria-label', `${baseAriaLabel}. Not generated yet.`);

            if (!skipTitle) {
                button.title = '';
            }
        }

        function setActionMenuCardOverflow(menu, isOpen) {
            if (!(menu instanceof HTMLElement)) {
                return;
            }

            const card = menu.closest('.wine-card-hover');
            if (!card) {
                return;
            }

            if (isOpen) {
                card.setAttribute('data-action-menu-open', '');
            } else {
                card.removeAttribute('data-action-menu-open');
            }
        }


        if (inventoryTable && inventoryInlineTemplate && inventoryInlineRowTemplate) {
            const summaryRows = Array.from(inventoryTable.querySelectorAll('[data-inventory-row]'));
            summaryRows.forEach((row) => {
                bindInventorySummaryRow(row);
            });
        }

        window.WineInventoryTables.hideDetailsPanel = function hideDetailsPanel() {
            if (expandedInventoryRow) {
                collapseInventoryRow(expandedInventoryRow);
            }
        };

        function closeActionMenu(menu, { focusTrigger = false } = {}) {
            if (!(menu instanceof HTMLElement) || !menu.hasAttribute('data-open')) {
                return;
            }

            const trigger = menu.querySelector('[data-action-menu-trigger]');
            const content = menu.querySelector('[data-action-menu-content]');

            if (content) {
                content.setAttribute('hidden', '');
            }

            if (trigger) {
                trigger.setAttribute('aria-expanded', 'false');
                if (focusTrigger) {
                    window.requestAnimationFrame(() => trigger.focus());
                }
            }

            menu.removeAttribute('data-open');
            setActionMenuCardOverflow(menu, false);

            if (activeActionMenu === menu) {
                activeActionMenu = null;
            }
        }

        function bindActionMenuGlobalHandlers() {
            if (actionMenuEventsBound) {
                return;
            }

            document.addEventListener('click', (event) => {
                const target = event.target instanceof Element ? event.target : null;

                if (activeActionMenu && (!target || !activeActionMenu.contains(target))) {
                    closeActionMenu(activeActionMenu);
                }
            });

            document.addEventListener('keydown', (event) => {
                if (event.key === 'Escape' && activeActionMenu) {
                    closeActionMenu(activeActionMenu, { focusTrigger: true });
                }
            });

            actionMenuEventsBound = true;
        }

        function initializeActionMenu(menu) {
            if (!(menu instanceof HTMLElement) || menu.dataset.actionMenuBound === 'true') {
                return;
            }

            const trigger = menu.querySelector('[data-action-menu-trigger]');
            const content = menu.querySelector('[data-action-menu-content]');

            if (!trigger || !content) {
                return;
            }

            menu.dataset.actionMenuBound = 'true';

            trigger.addEventListener('click', (event) => {
                event.preventDefault();
                event.stopPropagation();

                const isOpen = menu.hasAttribute('data-open');

                if (activeActionMenu && activeActionMenu !== menu) {
                    closeActionMenu(activeActionMenu);
                }

                if (isOpen) {
                    closeActionMenu(menu);
                } else {
                    menu.setAttribute('data-open', '');
                    trigger.setAttribute('aria-expanded', 'true');
                    content.removeAttribute('hidden');
                    setActionMenuCardOverflow(menu, true);
                    activeActionMenu = menu;
                }
            });

            content.addEventListener('click', (event) => {
                event.stopPropagation();

                const target = event.target instanceof Element
                    ? event.target.closest('[data-action-menu-item]')
                    : null;

                if (target) {
                    closeActionMenu(menu);
                }
            });

            bindActionMenuGlobalHandlers();
        }

        if (headerActionMenus.length > 0) {
            headerActionMenus.forEach((menu) => initializeActionMenu(menu));
        }

        function configureLocationActionMenu(card) {
            if (!(card instanceof HTMLElement)) {
                return;
            }

            const menu = card.querySelector('[data-action-menu]');
            if (!menu) {
                return;
            }

            const trigger = menu.querySelector('[data-action-menu-trigger]');
            const content = menu.querySelector('[data-action-menu-content]');

            if (!trigger || !content) {
                return;
            }

            const locationId = card.dataset.locationId ?? '';
            if (locationId) {
                const menuId = `location-actions-menu-${locationId}`;
                content.id = menuId;
                trigger.setAttribute('aria-controls', menuId);
            }

            const displayName = (card.dataset.locationName ?? '').trim();
            const triggerLabel = displayName
                ? `Open actions for ${displayName}`
                : 'Open storage location actions';
            const menuLabel = displayName
                ? `${displayName} actions`
                : 'Storage location actions';

            trigger.setAttribute('aria-label', triggerLabel);
            const srLabel = trigger.querySelector('.sr-only');
            if (srLabel) {
                srLabel.textContent = triggerLabel;
            }

            content.setAttribute('aria-label', menuLabel);

            initializeActionMenu(menu);
        }

        function bindInventorySummaryRow(row) {
            if (!(row instanceof HTMLTableRowElement) || row.dataset.inlineBound === 'true') {
                return;
            }

            row.dataset.inlineBound = 'true';

            const generateButton = row.querySelector('[data-generate-drinking-windows]');
            if (generateButton instanceof HTMLButtonElement) {
                if (!generateButton.dataset.originalLabel) {
                    const ariaLabel = generateButton.getAttribute('aria-label');
                    const textLabel = (generateButton.textContent || '').trim();
                    generateButton.dataset.originalLabel = ariaLabel || textLabel || 'Generate drinking windows';
                }

                if (!generateButton.dataset.originalAriaLabel) {
                    const baseAriaLabel = generateButton.getAttribute('aria-label');
                    if (baseAriaLabel) {
                        generateButton.dataset.originalAriaLabel = baseAriaLabel;
                    }
                }

                if (!generateButton.dataset.originalContent) {
                    generateButton.dataset.originalContent = generateButton.innerHTML;
                }

                updateDrinkingWindowButtonMeta(generateButton, row.dataset.wineId ?? '');

                generateButton.addEventListener('click', (event) => {
                    event.preventDefault();
                    event.stopPropagation();
                    handleGenerateDrinkingWindows(row, generateButton);
                });
            }

            row.addEventListener('click', () => {
                toggleInventorySummaryRow(row);
            });

            row.addEventListener('keydown', (event) => {
                if (event.key === 'Enter' || event.key === ' ') {
                    event.preventDefault();
                    toggleInventorySummaryRow(row);
                }
            });
        }

        async function handleGenerateDrinkingWindows(row, button) {
            if (!(row instanceof HTMLTableRowElement)) {
                return;
            }

            const wineId = row.dataset.wineId ?? '';
            if (!wineId) {
                return;
            }

            const actionableButton = button instanceof HTMLButtonElement ? button : null;
            if (actionableButton && actionableButton.dataset.loading === 'true') {
                return;
            }

            const originalLabel = actionableButton?.dataset?.originalLabel
                ?? actionableButton?.getAttribute('aria-label')
                ?? actionableButton?.textContent
                ?? '';
            const originalContent = actionableButton?.dataset?.originalContent ?? '';

            if (actionableButton) {
                actionableButton.dataset.loading = 'true';
                actionableButton.disabled = true;
                actionableButton.title = '';
                actionableButton.textContent = 'Generating…';
            }

            let generatedAtIso = null;

            try {
                const response = await sendJson(`/wine-manager/wines/${encodeURIComponent(wineId)}/drinking-windows`, {
                    method: 'POST',
                    body: JSON.stringify({})
                });

                applyInventoryUpdateFromResponse(row, response, wineId);

                const group = response?.Group ?? response?.group ?? null;
                generatedAtIso = extractGroupLastGeneratedIso(group)
                    || actionableButton?.dataset.lastGeneratedAt
                    || new Date().toISOString();

                if (actionableButton) {
                    actionableButton.title = 'Drinking windows updated.';
                    actionableButton.textContent = 'Generated!';
                    actionableButton.dataset.lastGeneratedAt = generatedAtIso;
                    updateDrinkingWindowButtonMeta(actionableButton, wineId, generatedAtIso, { skipTitle: true });
                    window.setTimeout(() => {
                        if (!actionableButton.isConnected) {
                            return;
                        }

                        if (originalContent) {
                            actionableButton.innerHTML = originalContent;
                        } else {
                            actionableButton.textContent = originalLabel || 'Generate drinking windows';
                        }

                        actionableButton.title = '';
                        updateDrinkingWindowButtonMeta(actionableButton, wineId, generatedAtIso);
                    }, 2000);
                }
            } catch (error) {
                console.error('[WineInventoryTables] Failed to generate drinking windows', error);
                const message = typeof error?.message === 'string' && error.message
                    ? error.message
                    : 'We could not generate drinking windows. Please try again.';

                if (actionableButton) {
                    actionableButton.textContent = 'Try again';
                    actionableButton.title = message;
                    updateDrinkingWindowButtonMeta(actionableButton, wineId, undefined, { skipTitle: true });
                }
            } finally {
                if (actionableButton) {
                    actionableButton.disabled = false;
                    delete actionableButton.dataset.loading;
                }
            }
        }

        function applyInventoryUpdateFromResponse(row, payload, wineId) {
            if (!(row instanceof HTMLTableRowElement)) {
                return;
            }

            const group = payload?.Group ?? payload?.group ?? null;
            if (group) {
                updateSummaryRow(row, group);
            }

            const details = Array.isArray(payload?.Details)
                ? payload.Details
                : (Array.isArray(payload?.details) ? payload.details : []);

            if (wineId) {
                const statusFilter = getActiveStatusFilter();
                const locationFilterId = getActiveLocationFilterId();
                const aggregated = aggregateVintageCounts(details, statusFilter, locationFilterId);
                const cacheEntry = new Map();
                cacheEntry.set(buildVintageCacheKey(statusFilter, locationFilterId), aggregated);
                vintageSummaryCache.set(wineId, cacheEntry);
            }

            if (row.classList.contains('selected')) {
                collapseInventoryRow(row);
                row.classList.add('selected');
                row.setAttribute('aria-expanded', 'true');
                void expandInventorySummaryRow(row);
            }
        }

        function updateSummaryRow(row, group) {
            if (!(row instanceof HTMLTableRowElement) || !group) {
                return;
            }

            const wineName = typeof group?.wineName === 'string' ? group.wineName.trim() : '';
            const regionName = typeof group?.region === 'string' ? group.region.trim() : '';
            const colorName = typeof group?.color === 'string' ? group.color.trim() : '';
            const statusLabel = typeof group?.statusLabel === 'string' ? group.statusLabel.trim() : '';
            const statusClasses = typeof group?.statusCssClass === 'string'
                ? group.statusCssClass.split(/\s+/).filter(Boolean)
                : [];
            const summaryWineCell = row.querySelector('.summary-wine');
            if (summaryWineCell) {
                summaryWineCell.textContent = wineName || '—';
            }

            const summaryRegionCell = row.querySelector('.summary-region');
            if (summaryRegionCell) {
                summaryRegionCell.textContent = regionName || '—';
            }

            const summaryAppellationCell = row.querySelector('.summary-appellation');
            const appellationDisplay = buildAppellationDisplay(group);
            if (summaryAppellationCell) {
                summaryAppellationCell.textContent = appellationDisplay || '—';
            }

            const bottlesCell = row.querySelector('[data-field="bottle-count"]');
            if (bottlesCell) {
                const pending = Number.isFinite(group?.pendingBottleCount)
                    ? Number(group.pendingBottleCount)
                    : Number.isFinite(group?.pendingCount)
                        ? Number(group.pendingCount)
                        : 0;
                const cellared = Number.isFinite(group?.cellaredBottleCount)
                    ? Number(group.cellaredBottleCount)
                    : Number.isFinite(group?.availableBottleCount)
                        ? Number(group.availableBottleCount)
                        : 0;
                const drunk = Number.isFinite(group?.drunkBottleCount)
                    ? Number(group.drunkBottleCount)
                    : Number.isFinite(group?.drunkCount)
                        ? Number(group.drunkCount)
                        : 0;
                const hiddenDisplay = bottlesCell.querySelector('span[aria-hidden="true"]');
                const accessibleDisplay = bottlesCell.querySelector('.sr-only');
                const statusFilter = getActiveStatusFilter();
                const copy = buildBottleCountCopy(pending, cellared, drunk, statusFilter);

                if (hiddenDisplay) {
                    hiddenDisplay.textContent = copy.hidden;
                }

                if (accessibleDisplay) {
                    accessibleDisplay.textContent = copy.accessible;
                }
            }

            const colorCell = row.querySelector('.summary-color');
            if (colorCell) {
                colorCell.textContent = colorName || '—';
            }

            const statusCell = row.querySelector('.summary-status');
            if (statusCell) {
                const pill = statusCell.querySelector('.status-pill');
                if (pill) {
                    pill.textContent = statusLabel || '—';
                    pill.className = 'status-pill';
                    statusClasses.forEach((cls) => {
                        if (cls) {
                            pill.classList.add(cls);
                        }
                    });
                } else {
                    statusCell.textContent = statusLabel || '—';
                }
            }

            const startCell = row.querySelector('[data-field="drinking-window-start"]');
            if (startCell) {
                startCell.textContent = formatDrinkingWindowValue(group?.userDrinkingWindowStartYear);
                startCell.removeAttribute('title');
                delete startCell.dataset.explanation;
            }

            const endCell = row.querySelector('[data-field="drinking-window-end"]');
            if (endCell) {
                const endValue = group?.userDrinkingWindowEndYear;
                const formattedEnd = formatDrinkingWindowValue(endValue);
                const urgency = getDrinkingWindowUrgency(endValue);

                let urgencyIndicator = endCell.querySelector('.drinking-window-urgency');
                if (!urgencyIndicator) {
                    urgencyIndicator = document.createElement('span');
                }

                urgencyIndicator.textContent = formattedEnd;
                urgencyIndicator.setAttribute('aria-label', urgency.label);

                const urgencyClasses = ['status-pill', 'drinking-window-urgency'];

                if (urgency.cssClass) {
                    urgency.cssClass.split(/\s+/).forEach((cls) => {
                        if (cls) {
                            urgencyClasses.push(cls);
                        }
                    });
                }

                urgencyIndicator.className = urgencyClasses.join(' ');

                endCell.textContent = '';
                endCell.appendChild(urgencyIndicator);

                if (urgency.label) {
                    endCell.setAttribute('title', urgency.label);
                } else {
                    endCell.removeAttribute('title');
                }

                delete endCell.dataset.explanation;
            }

            const scoreCell = row.querySelector('[data-field="score"]');
            if (scoreCell) {
                scoreCell.textContent = formatAverageScore(group?.averageScore);
            }

            row.dataset.summaryWine = wineName || '';
            row.dataset.summaryRegion = regionName || '';
            row.dataset.summaryAppellation = appellationDisplay && appellationDisplay !== '—' ? appellationDisplay : '';
            row.dataset.summaryColor = colorName || '';
            row.dataset.summaryStatus = statusLabel || '';

            const generateButton = row.querySelector('[data-generate-drinking-windows]');
            if (generateButton instanceof HTMLButtonElement) {
                const providedIso = extractGroupLastGeneratedIso(group);
                if (providedIso) {
                    updateDrinkingWindowButtonMeta(generateButton, row.dataset.wineId ?? '', providedIso);
                } else {
                    updateDrinkingWindowButtonMeta(generateButton, row.dataset.wineId ?? '');
                }
            }
        }

        function toggleInventorySummaryRow(row) {
            if (!(row instanceof HTMLTableRowElement)) {
                return;
            }

            if (row.classList.contains('selected')) {
                collapseInventoryRow(row);
                return;
            }

            if (expandedInventoryRow && expandedInventoryRow !== row) {
                collapseInventoryRow(expandedInventoryRow);
            }

            row.classList.add('selected');
            row.setAttribute('aria-expanded', 'true');
            expandedInventoryRow = row;
            void expandInventorySummaryRow(row);
        }

        function collapseInventoryRow(row) {
            if (!(row instanceof HTMLTableRowElement)) {
                return;
            }

            row.classList.remove('selected');
            row.setAttribute('aria-expanded', 'false');

            const inlineRow = row.nextElementSibling;
            if (inlineRow instanceof HTMLTableRowElement && inlineRow.hasAttribute('data-inline-row')) {
                inlineRow.remove();
            }

            if (expandedInventoryRow === row) {
                cancelInlineDetailsRequest();
                expandedInventoryRow = null;
            }
        }

        async function expandInventorySummaryRow(row) {
            if (!(row instanceof HTMLTableRowElement)) {
                return;
            }

            if (!inventoryInlineTemplate || !inventoryInlineRowTemplate) {
                collapseInventoryRow(row);
                return;
            }

            const wineId = row.dataset.wineId ?? '';
            if (!wineId) {
                collapseInventoryRow(row);
                return;
            }

            const inlineRow = createInlineRow();
            if (!inlineRow) {
                collapseInventoryRow(row);
                return;
            }

            row.insertAdjacentElement('afterend', inlineRow);

            const statusElement = inlineRow.querySelector('[data-inline-status]');
            const tableElement = inlineRow.querySelector('[data-inline-table]');
            const tableBody = inlineRow.querySelector('[data-inline-body]');
            const emptyElement = inlineRow.querySelector('[data-inline-empty]');

            function showStatus(message, isError = false) {
                if (!statusElement) {
                    return;
                }

                statusElement.textContent = message;
                statusElement.removeAttribute('hidden');

                if (isError) {
                    statusElement.classList.add('inventory-inline-status--error');
                } else {
                    statusElement.classList.remove('inventory-inline-status--error');
                }

                if (tableElement) {
                    tableElement.setAttribute('hidden', 'hidden');
                }

                if (emptyElement) {
                    emptyElement.setAttribute('hidden', 'hidden');
                }
            }

            function renderVintageView(vintages, statusFilter) {
                if (!tableElement || !tableBody || !statusElement || !emptyElement) {
                    return;
                }

                if (!Array.isArray(vintages) || vintages.length === 0) {
                    tableElement.setAttribute('hidden', 'hidden');
                    statusElement.setAttribute('hidden', 'hidden');
                    statusElement.classList.remove('inventory-inline-status--error');
                    emptyElement.removeAttribute('hidden');
                    return;
                }

                renderVintageRows(tableBody, vintages, statusFilter);
                tableElement.removeAttribute('hidden');
                emptyElement.setAttribute('hidden', 'hidden');
                statusElement.setAttribute('hidden', 'hidden');
                statusElement.classList.remove('inventory-inline-status--error');
            }

            const statusFilter = getActiveStatusFilter();
            const locationFilterId = getActiveLocationFilterId();
            const normalizedStatus = normalizeStatusValue(statusFilter);
            const cacheKey = buildVintageCacheKey(normalizedStatus, locationFilterId);
            const cachedByStatus = vintageSummaryCache.get(wineId);
            const cached = cachedByStatus?.get(cacheKey);
            if (cached) {
                renderVintageView(cached, normalizedStatus);
                return;
            }

            showStatus('Loading vintages…');
            row.dataset.inlineLoading = 'true';

            cancelInlineDetailsRequest();
            inlineDetailsAbortController = new AbortController();
            const abortController = inlineDetailsAbortController;

            let requestUrl = `/wine-manager/wine/${encodeURIComponent(wineId)}/details`;
            if (locationFilterId) {
                const separator = requestUrl.includes('?') ? '&' : '?';
                requestUrl = `${requestUrl}${separator}locationId=${encodeURIComponent(locationFilterId)}`;
            }

            try {
                const response = await sendJson(requestUrl, {
                    method: 'GET',
                    signal: abortController.signal
                });
                const details = Array.isArray(response?.details) ? response.details : [];
                const aggregated = aggregateVintageCounts(details, normalizedStatus, locationFilterId);
                if (!vintageSummaryCache.has(wineId)) {
                    vintageSummaryCache.set(wineId, new Map());
                }
                vintageSummaryCache.get(wineId).set(cacheKey, aggregated);

                if (!inlineRow.isConnected || expandedInventoryRow !== row) {
                    return;
                }

                const activeLocationKey = getActiveLocationFilterId().trim().toLowerCase();
                const expectedLocationKey = typeof locationFilterId === 'string'
                    ? locationFilterId.trim().toLowerCase()
                    : '';
                if (activeLocationKey !== expectedLocationKey) {
                    return;
                }

                renderVintageView(aggregated, normalizedStatus);
            } catch (error) {
                if (abortController.signal.aborted || error?.name === 'AbortError') {
                    return;
                }

                if (!inlineRow.isConnected) {
                    return;
                }

                const message = typeof error?.message === 'string' && error.message
                    ? error.message
                    : 'Unable to load vintages.';
                showStatus(message, true);
            } finally {
                if (inlineDetailsAbortController === abortController) {
                    inlineDetailsAbortController = null;
                }
                delete row.dataset.inlineLoading;
            }
        }


        function createInlineRow() {
            if (!inventoryInlineTemplate?.content?.firstElementChild) {
                return null;
            }

            return inventoryInlineTemplate.content.firstElementChild.cloneNode(true);
        }

        function renderVintageRows(tbody, vintages, statusFilter) {
            if (!tbody || !inventoryInlineRowTemplate?.content?.firstElementChild) {
                return;
            }

            tbody.innerHTML = '';

            const normalizedStatus = normalizeStatusValue(statusFilter);

            vintages.forEach((vintage) => {
                const templateRow = inventoryInlineRowTemplate.content.firstElementChild.cloneNode(true);
                const vintageCell = templateRow.querySelector('[data-vintage]');
                const scoreCell = templateRow.querySelector('[data-score]');
                const bottleCell = templateRow.querySelector('[data-bottle-count]');
                const bottleDisplay = templateRow.querySelector('[data-bottle-count-display]');
                const bottleAccessible = templateRow.querySelector('[data-bottle-count-accessible]');
                const drinkingWindowCell = templateRow.querySelector('[data-drinking-window]');
                const surfScoreCell = templateRow.querySelector('[data-surf-score]');
                const noteCell = templateRow.querySelector('[data-note]');
                const locationsCell = templateRow.querySelector('[data-storage-locations]');

                if (vintageCell) {
                    vintageCell.textContent = formatVintageLabel(vintage?.vintage);
                }

                if (scoreCell) {
                    scoreCell.textContent = formatAverageScore(vintage?.averageScore);
                }

                if (drinkingWindowCell) {
                    const startYear = vintage?.userDrinkingWindowStartYear ?? vintage?.drinkingWindowStartYear;
                    const endYear = vintage?.userDrinkingWindowEndYear ?? vintage?.drinkingWindowEndYear;
                    drinkingWindowCell.textContent = formatDrinkingWindowRange(startYear, endYear);
                    drinkingWindowCell.removeAttribute('title');
                    delete drinkingWindowCell.dataset.explanation;
                }

                if (surfScoreCell) {
                    surfScoreCell.textContent = formatAlignmentScore(vintage?.alignmentScore);
                }

                const pendingCount = typeof vintage?.pendingCount === 'number'
                    ? vintage.pendingCount
                    : (typeof vintage?.pendingBottleCount === 'number' ? vintage.pendingBottleCount : 0);
                const cellaredCount = typeof vintage?.cellaredCount === 'number'
                    ? vintage.cellaredCount
                    : (typeof vintage?.availableCount === 'number'
                        ? vintage.availableCount
                        : (typeof vintage?.count === 'number' ? vintage.count : 0));
                const drunkCount = typeof vintage?.drunkCount === 'number'
                    ? vintage.drunkCount
                    : 0;
                const totalCount = typeof vintage?.totalCount === 'number'
                    ? vintage.totalCount
                    : pendingCount + cellaredCount + drunkCount;

                const formattedPending = Number.isFinite(pendingCount)
                    ? pendingCount
                    : 0;
                const formattedCellared = Number.isFinite(cellaredCount)
                    ? cellaredCount
                    : 0;
                const formattedDrunk = Number.isFinite(drunkCount)
                    ? drunkCount
                    : 0;

                if (bottleCell) {
                    bottleCell.dataset.pendingCount = String(formattedPending);
                    bottleCell.dataset.cellaredCount = String(formattedCellared);
                    bottleCell.dataset.drunkCount = String(formattedDrunk);
                }

                const copy = buildBottleCountCopy(formattedPending, formattedCellared, formattedDrunk, normalizedStatus);

                if (bottleDisplay) {
                    bottleDisplay.textContent = copy.hidden;
                }

                if (bottleAccessible) {
                    bottleAccessible.textContent = copy.accessible;
                }

                if (noteCell) {
                    const note = typeof vintage?.note === 'string' ? vintage.note.trim() : '';
                    noteCell.textContent = note || '—';
                    noteCell.title = note || '';
                }

                if (locationsCell) {
                    const locations = Array.isArray(vintage?.storageLocations)
                        ? vintage.storageLocations
                            .map((location) => normalizeStorageLocation(location))
                            .filter((location) => Boolean(location))
                        : [];
                    const uniqueLocations = Array.from(new Set(locations));
                    if (uniqueLocations.length === 0) {
                        locationsCell.textContent = '—';
                        locationsCell.title = '';
                    } else {
                        const joined = uniqueLocations.join(', ');
                        locationsCell.textContent = joined;
                        locationsCell.title = joined;
                    }
                }

                const counts = {
                    pendingCount: formattedPending,
                    cellaredCount: formattedCellared,
                    drunkCount: formattedDrunk,
                    totalCount: Number.isFinite(totalCount) ? totalCount : formattedPending + formattedCellared + formattedDrunk
                };

                bindVintageInlineRow(templateRow, { ...vintage, ...counts }, normalizedStatus);
                tbody.appendChild(templateRow);
            });
        }

        function bindVintageInlineRow(row, vintage, statusFilter) {
            if (!(row instanceof HTMLTableRowElement)) {
                return;
            }

            const rawId = vintage?.wineVintageId ?? '';
            const wineVintageId = typeof rawId === 'string'
                ? rawId
                : (rawId != null ? String(rawId) : '');

            if (!wineVintageId) {
                row.classList.remove('inventory-inline-row--interactive');
                row.removeAttribute('tabindex');
                row.removeAttribute('role');
                return;
            }

            row.dataset.wineVintageId = wineVintageId;
            row.classList.add('inventory-inline-row--interactive');
            row.setAttribute('tabindex', '0');
            row.setAttribute('role', 'button');

            const vintageLabel = formatVintageLabel(vintage?.vintage);
            const pendingCount = typeof vintage?.pendingCount === 'number'
                ? vintage.pendingCount
                : (typeof vintage?.pendingBottleCount === 'number' ? vintage.pendingBottleCount : 0);
            const cellaredCount = typeof vintage?.cellaredCount === 'number'
                ? vintage.cellaredCount
                : (typeof vintage?.availableCount === 'number'
                    ? vintage.availableCount
                    : (typeof vintage?.count === 'number' ? vintage.count : 0));
            const drunkCount = typeof vintage?.drunkCount === 'number' ? vintage.drunkCount : 0;
            const totalCount = typeof vintage?.totalCount === 'number'
                ? vintage.totalCount
                : pendingCount + cellaredCount + drunkCount;
            const totalBottleNoun = totalCount === 1 ? 'bottle' : 'bottles';
            const copy = buildBottleCountCopy(pendingCount, cellaredCount, drunkCount, statusFilter);
            row.setAttribute(
                'aria-label',
                `View ${totalCount} ${totalBottleNoun} from vintage ${vintageLabel} ${copy.ariaLabelSuffix}`
            );

            const openBottleModal = () => {
                if (window.BottleManagementModal?.open) {
                    window.BottleManagementModal.open(wineVintageId);
                }
            };

            row.addEventListener('click', (event) => {
                event.preventDefault();
                openBottleModal();
            });

            row.addEventListener('keydown', (event) => {
                if (event.key === 'Enter' || event.key === ' ') {
                    event.preventDefault();
                    openBottleModal();
                }
            });
        }

        function aggregateVintageCounts(details, statusFilter, locationFilterId) {
            const results = new Map();

            if (!Array.isArray(details)) {
                return [];
            }

            const normalizedStatus = normalizeStatusValue(statusFilter);
            const normalizedLocationId = typeof locationFilterId === 'string'
                ? locationFilterId.trim().toLowerCase()
                : '';

            details.forEach((detail) => {
                const vintageId = detail?.wineVintageId;
                if (!vintageId) {
                    return;
                }

                const detailLocationId = typeof detail?.bottleLocationId === 'string'
                    ? detail.bottleLocationId.trim().toLowerCase()
                    : '';
                if (normalizedLocationId && detailLocationId !== normalizedLocationId) {
                    return;
                }

                const hasScore = typeof detail?.currentUserScore === 'number'
                    && Number.isFinite(detail.currentUserScore);
                const scoreValue = hasScore ? detail.currentUserScore : 0;
                const noteText = typeof detail?.currentUserNote === 'string' ? detail.currentUserNote.trim() : '';
                const locationName = normalizeStorageLocation(detail?.bottleLocation);
                const startYear = normalizeDrinkingWindowYear(
                    detail?.userDrinkingWindowStartYear ?? detail?.drinkingWindowStartYear
                );
                const endYear = normalizeDrinkingWindowYear(
                    detail?.userDrinkingWindowEndYear ?? detail?.drinkingWindowEndYear
                );
                const alignmentScore = (() => {
                    const raw = detail?.userDrinkingWindowAlignmentScore;
                    if (typeof raw === 'number' && Number.isFinite(raw)) {
                        return raw;
                    }

                    if (typeof raw === 'string') {
                        const parsed = Number.parseFloat(raw);
                        if (Number.isFinite(parsed)) {
                            return parsed;
                        }
                    }

                    return null;
                })();
                const isDrunk = Boolean(detail?.isDrunk);
                const isPending = Boolean(detail?.pendingDelivery ?? detail?.PendingDelivery);

                const include = (() => {
                    switch (normalizedStatus) {
                        case 'pending':
                            return isPending;
                        case 'cellared':
                            return !isPending && !isDrunk;
                        case 'drunk':
                            return !isPending && isDrunk;
                        default:
                            return true;
                    }
                })();

                if (!include) {
                    return;
                }

                const existing = results.get(vintageId);

                if (existing) {
                    existing.totalCount += 1;
                    if (isPending) {
                        existing.pendingCount += 1;
                    } else if (isDrunk) {
                        existing.drunkCount += 1;
                    } else {
                        existing.cellaredCount += 1;
                    }
                    if (hasScore) {
                        existing.scoreTotal += scoreValue;
                        existing.scoreCount += 1;
                    }
                    if (existing.alignmentScore == null && alignmentScore != null) {
                        existing.alignmentScore = alignmentScore;
                    }
                    if (!existing.note && noteText) {
                        existing.note = noteText;
                    }
                    if (existing.userDrinkingWindowStartYear == null && startYear != null) {
                        existing.userDrinkingWindowStartYear = startYear;
                    }
                    if (existing.userDrinkingWindowEndYear == null && endYear != null) {
                        existing.userDrinkingWindowEndYear = endYear;
                    }
                    if (locationName) {
                        existing.locations.add(locationName);
                    }
                } else {
                    results.set(vintageId, {
                        wineVintageId: vintageId,
                        vintage: detail?.vintage,
                        totalCount: 1,
                        pendingCount: isPending ? 1 : 0,
                        cellaredCount: (!isPending && !isDrunk) ? 1 : 0,
                        drunkCount: (!isPending && isDrunk) ? 1 : 0,
                        scoreTotal: scoreValue,
                        scoreCount: hasScore ? 1 : 0,
                        alignmentScore,
                        note: noteText,
                        userDrinkingWindowStartYear: startYear,
                        userDrinkingWindowEndYear: endYear,
                        locations: locationName ? new Set([locationName]) : new Set()
                    });
                }
            });

            const aggregated = Array.from(results.values()).map((entry) => {
                const averageScore = entry.scoreCount > 0
                    ? Math.round((entry.scoreTotal / entry.scoreCount) * 10) / 10
                    : null;

                const total = Number.isFinite(entry.totalCount)
                    ? entry.totalCount
                    : entry.pendingCount + entry.cellaredCount + entry.drunkCount;

                const storageLocations = Array.from(entry.locations ?? [])
                    .filter((value) => typeof value === 'string' && value.trim().length > 0)
                    .sort((a, b) => a.localeCompare(b, undefined, { numeric: true, sensitivity: 'base' }));

                return {
                    wineVintageId: entry.wineVintageId,
                    vintage: entry.vintage,
                    pendingCount: entry.pendingCount,
                    cellaredCount: entry.cellaredCount,
                    drunkCount: entry.drunkCount,
                    totalCount: total,
                    averageScore,
                    alignmentScore: entry.alignmentScore,
                    note: entry.note,
                    userDrinkingWindowStartYear: entry.userDrinkingWindowStartYear,
                    userDrinkingWindowEndYear: entry.userDrinkingWindowEndYear,
                    storageLocations
                };
            });
            aggregated.sort((a, b) => {
                const aVintage = typeof a.vintage === 'number' ? a.vintage : Number.NEGATIVE_INFINITY;
                const bVintage = typeof b.vintage === 'number' ? b.vintage : Number.NEGATIVE_INFINITY;
                return bVintage - aVintage;
            });

            return aggregated.filter((entry) => {
                return Number.isFinite(entry.totalCount) && entry.totalCount > 0;
            });
        }

        function normalizeDrinkingWindowYear(value) {
            if (typeof value === 'number' && Number.isFinite(value)) {
                return value;
            }

            if (typeof value === 'string') {
                const parsed = Number.parseInt(value, 10);
                if (Number.isFinite(parsed)) {
                    return parsed;
                }
            }

            return null;
        }

        function normalizeStorageLocation(value) {
            if (typeof value !== 'string') {
                return '';
            }

            const trimmed = value.trim();
            if (trimmed === '-') {
                return '';
            }
            if (!trimmed) {
                return '';
            }

            return trimmed;
        }

        function buildAppellationDisplay(group) {
            const subAppellation = typeof group?.subAppellation === 'string'
                ? group.subAppellation.trim()
                : '';
            const appellation = typeof group?.appellation === 'string'
                ? group.appellation.trim()
                : '';

            if (subAppellation && appellation) {
                if (subAppellation.localeCompare(appellation, undefined, { sensitivity: 'accent' }) !== 0) {
                    return `${subAppellation} (${appellation})`;
                }

                return subAppellation;
            }

            if (subAppellation) {
                return subAppellation;
            }

            if (appellation) {
                return appellation;
            }

            return '—';
        }

        function formatVintageLabel(value) {
            if (typeof value === 'number' && Number.isFinite(value) && value > 0) {
                return String(value);
            }

            return '—';
        }

        function formatAverageScore(value) {
            if (typeof value === 'number' && Number.isFinite(value) && value > 0) {
                return value.toFixed(1);
            }

            return '—';
        }

        function formatAlignmentScore(value) {
            if (typeof value === 'number' && Number.isFinite(value)) {
                return value.toFixed(1);
            }

            return '—';
        }

        function formatDrinkingWindowValue(value) {
            if (typeof value === 'number' && Number.isFinite(value) && value > 0) {
                return String(value);
            }

            return '—';
        }

        function parseYearValue(value) {
            if (typeof value === 'number' && Number.isFinite(value)) {
                return Math.trunc(value);
            }

            if (typeof value === 'string') {
                const trimmed = value.trim();
                if (trimmed) {
                    const parsed = Number.parseInt(trimmed, 10);
                    if (Number.isFinite(parsed)) {
                        return parsed;
                    }
                }
            }

            return null;
        }

        function getDrinkingWindowUrgency(endValue) {
            const numericYear = parseYearValue(endValue);
            const defaultState = {
                cssClass: 'drinking-window-urgency--unknown',
                label: 'No suggested end to the drinking window.'
            };

            if (typeof numericYear !== 'number' || !Number.isFinite(numericYear) || numericYear <= 0) {
                return defaultState;
            }

            const currentYear = new Date().getUTCFullYear();
            const yearValue = String(numericYear);

            if (numericYear <= currentYear - 1) {
                return {
                    cssClass: 'drinking-window-urgency--overdue drunk',
                    label: `Past recommended drinking window (best by ${yearValue}).`
                };
            }

            if (numericYear >= currentYear + 2) {
                return {
                    cssClass: 'drinking-window-urgency--future cellared',
                    label: `Comfortable window until ${yearValue}.`
                };
            }

            return {
                cssClass: 'drinking-window-urgency--approaching pending',
                label: `Approaching the end of the drinking window (best by ${yearValue}).`
            };
        }

        function formatDrinkingWindowRange(startValue, endValue) {
            const formattedStart = formatDrinkingWindowValue(startValue);
            const formattedEnd = formatDrinkingWindowValue(endValue);

            if (formattedStart === '—' && formattedEnd === '—') {
                return '—';
            }

            if (formattedStart === '—') {
                return `—-${formattedEnd}`;
            }

            if (formattedEnd === '—') {
                return `${formattedStart}-—`;
            }

            return `${formattedStart}-${formattedEnd}`;
        }

        if (!locationSection || !locationList) {
            return;
        }

        if (locationAddButton) {
            locationAddButton.addEventListener('click', (event) => {
                event.preventDefault();
                openLocationCreateForm();
            });
        }

        if (locationCreateCancel) {
            locationCreateCancel.addEventListener('click', (event) => {
                event.preventDefault();
                closeLocationCreateForm();
            });
        }

        if (locationCreateForm) {
            locationCreateForm.addEventListener('submit', (event) => {
                event.preventDefault();
                handleLocationCreate();
            });
        }

        if (!activeLocationFilterId && typeof locationSection?.dataset?.activeLocationId === 'string') {
            setActiveLocationFilter(locationSection.dataset.activeLocationId);
            if (filtersLocationInput) {
                filtersLocationInput.value = activeLocationFilterId;
            }
        }

        syncLocationSectionDataset();

        const existingCards = Array.from(locationList.querySelectorAll('[data-location-card]'));
        existingCards.forEach((card) => {
            setLocationDatasetCounts(card);
            updateLocationCardCounts(card);
            bindLocationCard(card);
        });

        updateLocationFilterHighlights();

        initializeReferenceLocations();
        updateLocationEmptyState();

        function initializeReferenceLocations() {
            referenceData.bottleLocations = sortLocations(collectLocationsFromDom());
            refreshLocationOptions();
        }

        function collectLocationsFromDom() {
            if (!locationList) {
                return [];
            }

            return Array.from(locationList.querySelectorAll('[data-location-card]'))
                .map((card) => {
                    const id = card.dataset.locationId ?? '';
                    if (!id) {
                        return null;
                    }

                    return {
                        id,
                        name: card.dataset.locationName
                            ?? card.querySelector('[data-location-name]')?.textContent
                            ?? '',
                        capacity: getLocationCapacity(card)
                    };
                })
                .filter((location) => location && location.id);
        }

        function openLocationCreateForm() {
            if (!locationCreateCard || !locationCreateForm) {
                return;
            }

            locationCreateCard.removeAttribute('hidden');
            setLocationFormLoading(locationCreateForm, false);
            setLocationError(locationCreateForm, '');

            if (locationCreateInput) {
                locationCreateInput.value = '';
                locationCreateInput.focus();
            }

            if (locationCreateCapacity) {
                locationCreateCapacity.value = '';
            }

            updateLocationEmptyState();
        }

        function closeLocationCreateForm() {
            if (!locationCreateCard || !locationCreateForm) {
                return;
            }

            locationCreateCard.setAttribute('hidden', 'hidden');
            setLocationFormLoading(locationCreateForm, false);
            setLocationError(locationCreateForm, '');

            if (locationCreateInput) {
                locationCreateInput.value = '';
            }

            if (locationCreateCapacity) {
                locationCreateCapacity.value = '';
            }

            updateLocationEmptyState();
        }

        async function handleLocationCreate() {
            if (!locationCreateForm) {
                return;
            }

            const nameField = locationCreateForm.querySelector('[data-location-input]');
            const capacityField = locationCreateForm.querySelector('[data-location-capacity-input]');
            const proposedName = (nameField?.value ?? '').trim();

            if (!proposedName) {
                setLocationError(locationCreateForm, 'Location name is required.');
                nameField?.focus();
                return;
            }

            if (!currentUserId) {
                setLocationError(locationCreateForm, 'Unable to determine current user.');
                return;
            }

            const { value: parsedCapacity, error: capacityError } = parseCapacityInputValue(capacityField?.value);
            if (capacityError) {
                setLocationError(locationCreateForm, capacityError);
                if (capacityField) {
                    capacityField.focus();
                    capacityField.select?.();
                }
                return;
            }

            setLocationError(locationCreateForm, '');
            setLocationFormLoading(locationCreateForm, true);

            try {
                const payload = {
                    name: proposedName,
                    userId: currentUserId,
                    capacity: parsedCapacity
                };

                const response = await sendJson('/api/BottleLocations', {
                    method: 'POST',
                    body: JSON.stringify(payload)
                });

                const normalized = normalizeLocation(response);
                if (!normalized) {
                    throw new Error('Location could not be created.');
                }

                const card = createLocationCardElement(normalized, {
                    bottleCount: 0,
                    uniqueCount: 0,
                    cellaredCount: 0,
                    drunkCount: 0
                });

                if (card) {
                    insertLocationCard(card);
                }

                addLocationToReference(normalized);
                setLocationMessage(`Location '${normalized.name}' created.`, 'success');
                closeLocationCreateForm();
            } catch (error) {
                setLocationError(locationCreateForm, error?.message ?? String(error));
            } finally {
                setLocationFormLoading(locationCreateForm, false);
            }
        }

        function bindLocationCard(card) {
            if (!card) {
                return;
            }

            if (card.dataset.locationBound === 'true') {
                return;
            }

            configureLocationActionMenu(card);

            const editButton = card.querySelector('[data-location-edit]');
            const deleteButton = card.querySelector('[data-location-delete]');
            const form = card.querySelector('[data-location-edit-form]');
            const cancelButton = form?.querySelector('[data-location-cancel]');
            const input = form?.querySelector('[data-location-input]');
            const capacityInput = form?.querySelector('[data-location-capacity-input]');

            card.addEventListener('click', (event) => {
                if (!filtersLocationInput) {
                    return;
                }

                const target = event.target instanceof Element ? event.target : null;
                if (target) {
                    if (target.closest('button, a, input, select, textarea, label, form')) {
                        return;
                    }

                    if (target.closest('[data-action-menu]') || target.closest('[data-location-edit-form]')) {
                        return;
                    }
                }

                const locationId = card.dataset.locationId ?? '';
                if (!locationId) {
                    return;
                }

                const editForm = card.querySelector('[data-location-edit-form]');
                const view = card.querySelector('[data-location-view]');
                if ((editForm && !editForm.hasAttribute('hidden')) || (view && view.hasAttribute('hidden'))) {
                    return;
                }

                event.preventDefault();
                toggleLocationFilter(locationId);
            });

            editButton?.addEventListener('click', (event) => {
                event.preventDefault();
                openLocationEdit(card);
            });

            cancelButton?.addEventListener('click', (event) => {
                event.preventDefault();
                closeLocationEdit(card);
            });

            if (form && input) {
                form.addEventListener('submit', (event) => {
                    event.preventDefault();
                    handleLocationUpdate(card, form, input, capacityInput);
                });
            }

            deleteButton?.addEventListener('click', (event) => {
                event.preventDefault();
                handleLocationDelete(card);
            });

            card.dataset.locationBound = 'true';
        }

        function openLocationEdit(card) {
            const view = card?.querySelector('[data-location-view]');
            const form = card?.querySelector('[data-location-edit-form]');
            const input = form?.querySelector('[data-location-input]');
            const capacityInput = form?.querySelector('[data-location-capacity-input]');

            if (!card || !view || !form || !input) {
                return;
            }

            view.setAttribute('hidden', 'hidden');
            form.removeAttribute('hidden');
            setLocationError(form, '');

            input.value = card.dataset.locationName ?? '';
            input.focus();
            input.select?.();

            if (capacityInput) {
                const capacity = getLocationCapacity(card);
                capacityInput.value = capacity != null ? String(capacity) : '';
            }
        }

        function closeLocationEdit(card) {
            const view = card?.querySelector('[data-location-view]');
            const form = card?.querySelector('[data-location-edit-form]');
            const input = form?.querySelector('[data-location-input]');
            const capacityInput = form?.querySelector('[data-location-capacity-input]');

            if (!card || !view || !form) {
                return;
            }

            view.removeAttribute('hidden');
            form.setAttribute('hidden', 'hidden');
            setLocationError(form, '');

            if (input) {
                input.value = card.dataset.locationName ?? '';
            }

            if (capacityInput) {
                const capacity = getLocationCapacity(card);
                capacityInput.value = capacity != null ? String(capacity) : '';
            }
        }

        async function handleLocationUpdate(card, form, input, capacityInput) {
            const locationId = card?.dataset?.locationId ?? '';
            if (!locationId) {
                return;
            }

            const proposedName = (input?.value ?? '').trim();
            if (!proposedName) {
                setLocationError(form, 'Location name is required.');
                input?.focus();
                return;
            }

            if (!currentUserId) {
                setLocationError(form, 'Unable to determine current user.');
                return;
            }

            const { value: parsedCapacity, error: capacityError } = parseCapacityInputValue(capacityInput?.value);
            if (capacityError) {
                setLocationError(form, capacityError);
                capacityInput?.focus();
                capacityInput?.select?.();
                return;
            }

            const currentName = (card.dataset.locationName ?? '').trim();
            const currentCapacity = getLocationCapacity(card);
            const hasSameName = currentName === proposedName;
            const hasSameCapacity = (currentCapacity ?? null) === (parsedCapacity ?? null);

            if (hasSameName && hasSameCapacity) {
                closeLocationEdit(card);
                return;
            }

            setLocationError(form, '');
            setLocationFormLoading(form, true);
            setLocationCardLoading(card, true);

            try {
                const payload = {
                    name: proposedName,
                    userId: currentUserId,
                    capacity: parsedCapacity
                };

                const response = await sendJson(`/api/BottleLocations/${encodeURIComponent(locationId)}`, {
                    method: 'PUT',
                    body: JSON.stringify(payload)
                });

                const normalized = normalizeLocation(response) ?? {
                    id: locationId,
                    name: proposedName,
                    capacity: parsedCapacity
                };

                const resolvedName = normalized.name ?? proposedName;
                const resolvedCapacity = normalizeCapacityValue(normalized.capacity ?? parsedCapacity);

                updateLocationCardName(card, resolvedName);
                setLocationCapacity(card, resolvedCapacity);
                updateLocationCardCounts(card);

                closeLocationEdit(card);
                reorderLocationCard(card);
                updateReferenceLocation({
                    id: normalized.id ?? locationId,
                    name: resolvedName,
                    capacity: resolvedCapacity
                });

                setLocationMessage(`Location '${resolvedName}' updated.`, 'success');
            } catch (error) {
                setLocationError(form, error?.message ?? String(error));
            } finally {
                setLocationFormLoading(form, false);
                setLocationCardLoading(card, false);
            }
        }

        async function handleLocationDelete(card) {
            const locationId = card?.dataset?.locationId ?? '';
            if (!locationId) {
                return;
            }

            const displayName = card.dataset.locationName
                || card.querySelector('[data-location-name]')?.textContent
                || 'this location';

            const confirmed = window.confirm(`Delete ${displayName}? Bottles assigned to this location will no longer be associated with it.`);
            if (!confirmed) {
                return;
            }

            setLocationCardLoading(card, true);

            try {
                await sendJson(`/api/BottleLocations/${encodeURIComponent(locationId)}`, { method: 'DELETE' });
                removeLocationCard(card);
                removeReferenceLocation(locationId);
                setLocationMessage(`Location '${displayName}' deleted.`, 'success');
            } catch (error) {
                setLocationCardLoading(card, false);
                setLocationMessage(error?.message ?? String(error), 'error');
            }
        }

        function createLocationCardElement(location, counts) {
            if (!locationTemplate?.content || !location) {
                return null;
            }

            const fragment = locationTemplate.content.cloneNode(true);
            const card = fragment.querySelector('[data-location-card]');
            if (!card) {
                return null;
            }

            updateLocationCardName(card, location.name ?? '');
            card.dataset.locationId = location.id ?? '';
            setLocationCapacity(card, location.capacity);
            setLocationDatasetCounts(card, counts ?? {});
            updateLocationCardCounts(card);
            bindLocationCard(card);

            return card;
        }

        function insertLocationCard(card) {
            if (!locationList || !card) {
                return;
            }

            const cards = Array.from(locationList.querySelectorAll('[data-location-card]')).filter((existing) => existing !== card);
            const newName = (card.dataset.locationName ?? '').toString().toLocaleLowerCase();
            const referenceNode = cards.find((existing) => {
                const existingName = (existing.dataset.locationName ?? '').toString().toLocaleLowerCase();
                if (existingName === newName) {
                    return (existing.dataset.locationId ?? '').localeCompare(card.dataset.locationId ?? '') > 0;
                }

                return existingName.localeCompare(newName) > 0;
            });

            if (referenceNode) {
                locationList.insertBefore(card, referenceNode);
            } else {
                locationList.appendChild(card);
            }

            updateLocationEmptyState();
            updateLocationFilterHighlights();
        }

        function reorderLocationCard(card) {
            if (!locationList || !card) {
                return;
            }

            locationList.removeChild(card);
            insertLocationCard(card);
        }

        function removeLocationCard(card) {
            if (!card) {
                return;
            }

            const menu = card.querySelector('[data-action-menu]');
            if (menu) {
                closeActionMenu(menu);
            }

            card.remove();
            updateLocationEmptyState();
            updateLocationFilterHighlights();
        }

        function setLocationDatasetCounts(card, counts) {
            if (!card) {
                return;
            }

            const bottleCount = Number(counts?.bottleCount ?? card.dataset.bottleCount ?? 0) || 0;
            const uniqueCount = Number(counts?.uniqueCount ?? card.dataset.uniqueCount ?? 0) || 0;
            const pendingCount = Number(counts?.pendingCount ?? card.dataset.pendingCount ?? 0) || 0;
            const drunkCount = Number(counts?.drunkCount ?? card.dataset.drunkCount ?? 0) || 0;
            let cellaredCount = Number(
                counts?.cellaredCount
                ?? card.dataset.cellaredCount
                ?? (bottleCount - pendingCount - drunkCount)
            ) || 0;

            if (cellaredCount < 0) {
                cellaredCount = 0;
            }

            card.dataset.bottleCount = String(Math.max(bottleCount, 0));
            card.dataset.uniqueCount = String(Math.max(uniqueCount, 0));
            card.dataset.pendingCount = String(Math.max(pendingCount, 0));
            card.dataset.cellaredCount = String(cellaredCount);
            card.dataset.drunkCount = String(Math.max(drunkCount, 0));
        }

        function updateLocationCardCounts(card) {
            if (!card) {
                return;
            }

            const bottleCount = Number(card.dataset.bottleCount ?? '0') || 0;
            const pendingCount = Number(card.dataset.pendingCount ?? '0') || 0;
            const cellaredCount = Number(card.dataset.cellaredCount ?? '0') || 0;
            const drunkCount = Number(card.dataset.drunkCount ?? '0') || 0;
            const capacity = getLocationCapacity(card);

            const summaryParts = [
                `${pendingCount} pending`,
                `${cellaredCount} cellared`,
                `${drunkCount} enjoyed`
            ];
            const bottleSummary = summaryParts.join(' · ');

            const bottleTarget = card.querySelector('[data-location-bottle-count]');
            if (bottleTarget) {
                bottleTarget.textContent = `${bottleCount} bottle${bottleCount === 1 ? '' : 's'}`;
            }

            const fillIndicator = card.querySelector('[data-location-fill-indicator]');
            if (fillIndicator) {
                const fillBar = fillIndicator.querySelector('[data-location-fill-bar]');
                const percentTarget = fillIndicator.querySelector('[data-location-fill-percent]');
                const remainingTarget = fillIndicator.querySelector('[data-location-fill-remaining]');

                if (capacity != null) {
                    const numericCapacity = Number(capacity) || 0;
                    const baseRatio = numericCapacity <= 0
                        ? (bottleCount > 0 ? 1 : 0)
                        : bottleCount / numericCapacity;
                    const clampedRatio = Math.min(Math.max(baseRatio, 0), 1);
                    const percent = Math.round(clampedRatio * 100);
                    const remaining = numericCapacity - bottleCount;

                    if (fillBar) {
                        fillBar.style.width = `${percent}%`;
                    }

                    if (percentTarget) {
                        percentTarget.textContent = `${percent}% full`;
                    }

                    if (remainingTarget) {
                        let fillSummary;
                        if (remaining > 0) {
                            fillSummary = `${remaining} open`;
                        } else if (remaining === 0) {
                            fillSummary = 'At capacity';
                        } else {
                            fillSummary = `Over by ${Math.abs(remaining)}`;
                        }
                        remainingTarget.textContent = fillSummary;
                    }

                    fillIndicator.classList.remove('location-fill-indicator--no-capacity');
                    fillIndicator.classList.toggle('location-fill-indicator--over', remaining < 0);
                } else {
                    if (fillBar) {
                        fillBar.style.width = bottleCount > 0 ? '100%' : '0%';
                    }

                    if (percentTarget) {
                        percentTarget.textContent = 'Capacity not set';
                    }

                    if (remainingTarget) {
                        const fillSummary = bottleCount > 0
                            ? bottleSummary
                            : 'Add a capacity to track fill';
                        remainingTarget.textContent = fillSummary;
                    }

                    fillIndicator.classList.add('location-fill-indicator--no-capacity');
                    fillIndicator.classList.remove('location-fill-indicator--over');
                }
            }

            const descriptionTarget = card.querySelector('[data-location-description]');
            if (descriptionTarget) {
                if (bottleCount > 0) {
                    let description = bottleSummary;

                    if (capacity != null) {
                        const remaining = capacity - bottleCount;
                        const capacitySummary = remaining > 0
                            ? `${remaining} open slot${remaining === 1 ? '' : 's'} remaining`
                            : remaining === 0
                                ? 'At capacity'
                                : `Over capacity by ${Math.abs(remaining)} bottle${Math.abs(remaining) === 1 ? '' : 's'}`;
                        description = `${description} · ${capacitySummary}`;
                    }

                    descriptionTarget.textContent = description;
                } else if (capacity != null) {
                    const remaining = capacity - bottleCount;
                    const base = `Capacity ${capacity} bottle${capacity === 1 ? '' : 's'}.`;
                    let capacitySummary;

                    if (remaining > 0) {
                        capacitySummary = `${remaining} open slot${remaining === 1 ? '' : 's'} available.`;
                    } else if (remaining === 0) {
                        capacitySummary = 'At capacity.';
                    } else {
                        capacitySummary = `Over capacity by ${Math.abs(remaining)} bottle${Math.abs(remaining) === 1 ? '' : 's'}.`;
                    }

                    descriptionTarget.textContent = `${base} ${capacitySummary}`.trim();
                } else {
                    descriptionTarget.textContent = 'No bottles stored here yet.';
                }
            }
        }

        function updateLocationCardName(card, name) {
            if (!card) {
                return;
            }

            const resolvedName = (name ?? '').toString();
            card.dataset.locationName = resolvedName;

            const title = card.querySelector('[data-location-name]');
            if (title) {
                title.textContent = resolvedName;
            }

            configureLocationActionMenu(card);
        }

        function setLocationCapacity(card, capacity) {
            if (!card) {
                return;
            }

            if (capacity == null) {
                delete card.dataset.locationCapacity;
            } else {
                card.dataset.locationCapacity = String(capacity);
            }
        }

        function getLocationCapacity(card) {
            if (!card) {
                return null;
            }

            const raw = card.dataset?.locationCapacity ?? null;
            return normalizeCapacityValue(raw);
        }

        function setLocationMessage(text, variant) {
            if (!locationMessage) {
                return;
            }

            if (!text) {
                locationMessage.textContent = '';
                locationMessage.setAttribute('hidden', 'hidden');
                locationMessage.removeAttribute('data-variant');
                return;
            }

            locationMessage.textContent = text;
            locationMessage.dataset.variant = variant ?? 'info';
            locationMessage.removeAttribute('hidden');
        }

        function setLocationError(container, message) {
            const target = container?.querySelector('[data-location-error]');
            if (!target) {
                return;
            }

            if (!message) {
                target.textContent = '';
                target.setAttribute('aria-hidden', 'true');
            } else {
                target.textContent = message;
                target.removeAttribute('aria-hidden');
            }
        }

        function setLocationFormLoading(form, state) {
            if (!form) {
                return;
            }

            const elements = form.querySelectorAll('input, button, select, textarea');
            elements.forEach((element) => {
                toggleDisabledWithMemory(element, state);
            });
        }

        function setLocationCardLoading(card, state) {
            if (!card) {
                return;
            }

            const actions = card.querySelectorAll('[data-location-edit], [data-location-delete]');
            actions.forEach((action) => {
                toggleDisabledWithMemory(action, state);
            });
        }

        function toggleDisabledWithMemory(element, shouldDisable) {
            if (!element) {
                return;
            }

            if (shouldDisable) {
                element.dataset.prevDisabled = element.disabled ? 'true' : 'false';
                element.disabled = true;
            } else {
                const previous = element.dataset.prevDisabled;
                if (previous === 'true') {
                    element.disabled = true;
                } else {
                    element.disabled = false;
                }
                delete element.dataset.prevDisabled;
            }
        }

        function updateLocationEmptyState() {
            if (!locationEmpty) {
                return;
            }

            const hasCards = Boolean(locationList?.querySelector('[data-location-card]'));
            const createVisible = locationCreateCard && !locationCreateCard.hasAttribute('hidden');

            if (hasCards || createVisible) {
                locationEmpty.setAttribute('hidden', 'hidden');
            } else {
                locationEmpty.removeAttribute('hidden');
            }
        }

        function parseCapacityInputValue(raw) {
            if (raw == null || raw === '') {
                return { value: null };
            }

            const trimmed = String(raw).trim();
            if (!trimmed) {
                return { value: null };
            }

            const parsed = Number.parseInt(trimmed, 10);
            if (!Number.isFinite(parsed) || parsed < 0) {
                return { value: null, error: 'Enter a whole number 0 or greater.' };
            }

            if (parsed > MAX_LOCATION_CAPACITY) {
                return {
                    value: null,
                    error: `Capacity cannot exceed ${MAX_LOCATION_CAPACITY} bottles.`
                };
            }

            return { value: parsed };
        }

        function normalizeCapacityValue(value) {
            if (value == null || value === '') {
                return null;
            }

            const parsed = Number(value);
            if (!Number.isFinite(parsed) || parsed < 0) {
                return null;
            }

            return Math.min(parsed, MAX_LOCATION_CAPACITY);
        }

        function normalizeLocation(raw) {
            if (!raw) {
                return null;
            }

            const id = raw.id ?? raw.Id ?? raw.locationId ?? raw.LocationId;
            if (!id) {
                return null;
            }

            const nameValue = raw.name ?? raw.Name ?? raw.label ?? raw.Label ?? '';
            const normalized = {
                id: String(id),
                name: typeof nameValue === 'string' ? nameValue : String(nameValue)
            };

            const capacityValue = raw.capacity ?? raw.Capacity ?? null;
            const normalizedCapacity = normalizeCapacityValue(capacityValue);
            if (normalizedCapacity != null) {
                normalized.capacity = normalizedCapacity;
            }

            return normalized;
        }

        function sortLocations(list) {
            const seen = new Map();
            list.forEach((item) => {
                const normalized = normalizeLocation(item);
                if (normalized?.id) {
                    seen.set(normalized.id, normalized);
                }
            });

            return Array.from(seen.values()).sort((a, b) => {
                const nameA = (a.name ?? '').toLocaleLowerCase();
                const nameB = (b.name ?? '').toLocaleLowerCase();

                if (nameA === nameB) {
                    return (a.id ?? '').localeCompare(b.id ?? '');
                }

                return nameA.localeCompare(nameB);
            });
        }

        function addLocationToReference(location) {
            referenceData.bottleLocations = sortLocations([
                ...referenceData.bottleLocations,
                location
            ]);
            refreshLocationOptions(location?.id ? String(location.id) : undefined);
        }

        function updateReferenceLocation(location) {
            const normalized = normalizeLocation(location);
            if (!normalized?.id) {
                return;
            }

            referenceData.bottleLocations = sortLocations([
                ...referenceData.bottleLocations.filter((item) => (normalizeLocation(item)?.id) !== normalized.id),
                normalized
            ]);
            refreshLocationOptions(normalized.id);
        }

        function removeReferenceLocation(locationId) {
            if (!locationId) {
                return;
            }

            referenceData.bottleLocations = referenceData.bottleLocations.filter((item) => {
                return (normalizeLocation(item)?.id) !== locationId;
            });
            refreshLocationOptions();
        }

        function refreshLocationOptions(preferredValue) {
            if (!addWineLocationSelect) {
                return;
            }

            const locations = sortLocations(referenceData.bottleLocations);
            referenceData.bottleLocations = locations;

            const previousValue = preferredValue ?? addWineLocationSelect.value ?? '';
            addWineLocationSelect.innerHTML = '';

            const placeholder = document.createElement('option');
            placeholder.value = '';
            placeholder.textContent = 'No location';
            addWineLocationSelect.appendChild(placeholder);

            locations.forEach((location) => {
                const option = document.createElement('option');
                option.value = location.id;

                if (location.capacity != null) {
                    option.textContent = `${location.name} (${location.capacity} capacity)`;
                } else {
                    option.textContent = location.name;
                }

                addWineLocationSelect.appendChild(option);
            });

            if (previousValue && locations.some((location) => location.id === previousValue)) {
                addWineLocationSelect.value = previousValue;
            } else {
                addWineLocationSelect.value = '';
            }
        }

        async function sendJson(url, options = {}) {
            const requestInit = {
                credentials: 'same-origin',
                ...options,
                headers: {
                    'Content-Type': 'application/json',
                    'Accept': 'application/json',
                    ...(options?.headers ?? {})
                }
            };

            const response = await fetch(url, requestInit);
            if (!response.ok) {
                let message = `${response.status} ${response.statusText}`;
                try {
                    const problem = await response.json();
                    if (typeof problem === 'string') {
                        message = problem;
                    } else if (problem?.title) {
                        message = problem.title;
                    } else if (problem?.message) {
                        message = problem.message;
                    } else if (problem?.errors) {
                        const firstError = Object.values(problem.errors)[0];
                        if (Array.isArray(firstError) && firstError.length > 0) {
                            message = firstError[0];
                        }
                    }
                } catch {
                    const text = await response.text();
                    if (text) {
                        message = text;
                    }
                }

                throw new Error(message);
            }

            if (response.status === 204) {
                return {};
            }

            return response.json();
        }

        window.WineInventoryTables.updateLocationSummaries = function updateLocationSummariesFromModal(summaries) {
            if (!Array.isArray(summaries) || !locationList) {
                return;
            }

            summaries.forEach((summary) => {
                const normalized = normalizeLocation(summary);
                const locationId = normalized?.id;

                if (!locationId) {
                    return;
                }

                const card = Array.from(locationList.querySelectorAll('[data-location-card]')).find((candidate) => {
                    return (candidate?.dataset?.locationId ?? '') === locationId;
                });
                if (!card) {
                    return;
                }

                const counts = {
                    bottleCount: Number(summary?.BottleCount ?? summary?.bottleCount ?? 0) || 0,
                    uniqueCount: Number(summary?.UniqueWineCount ?? summary?.uniqueWineCount ?? 0) || 0,
                    pendingCount: Number(summary?.PendingBottleCount ?? summary?.pendingBottleCount ?? 0) || 0,
                    cellaredCount: Number(summary?.CellaredBottleCount ?? summary?.cellaredBottleCount ?? 0) || 0,
                    drunkCount: Number(summary?.DrunkBottleCount ?? summary?.drunkBottleCount ?? 0) || 0
                };

                setLocationDatasetCounts(card, counts);

                const capacityValue = summary?.Capacity ?? summary?.capacity ?? normalized?.capacity ?? null;
                const normalizedCapacity = normalizeCapacityValue(capacityValue);
                setLocationCapacity(card, normalizedCapacity);

                updateLocationCardCounts(card);
                updateReferenceLocation({
                    id: locationId,
                    name: normalized?.name ?? summary?.Name ?? summary?.name ?? card.dataset.locationName ?? '',
                    capacity: normalizedCapacity
                });
            });

            updateLocationEmptyState();
        };
    };

    if (document.readyState === 'loading') {
        window.addEventListener('DOMContentLoaded', () => {
            window.WineInventoryTables.initialize();
        });
    } else {
        window.WineInventoryTables.initialize();
    }

    window.addEventListener('pageshow', (event) => {
        if (event.persisted) {
            window.WineInventoryTables?.hideDetailsPanel?.();
        }
    });
})();
