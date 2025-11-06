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
        const inventoryView = document.getElementById('inventory-view');
        const headerFilterFocusStorageKey = 'wine-inventory:restore-header-focus';
        let headerFilterDebounceId = null;
        let maintainHeaderFilterFocus = false;
        let inlineDetailsAbortController = null;

        function updateHeaderFilterClearButtonVisibility() {
            if (!headerFilterInput || !headerFilterClearButton) {
                return;
            }

            const hasValue = headerFilterInput.value.trim().length > 0;
            headerFilterClearButton.hidden = !hasValue;
        }

        function cancelInlineDetailsRequest() {
            if (inlineDetailsAbortController) {
                inlineDetailsAbortController.abort();
                inlineDetailsAbortController = null;
            }
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

            function renderVintageView(vintages) {
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

                renderVintageRows(tableBody, vintages);
                tableElement.removeAttribute('hidden');
                emptyElement.setAttribute('hidden', 'hidden');
                statusElement.setAttribute('hidden', 'hidden');
                statusElement.classList.remove('inventory-inline-status--error');
            }

            const cached = vintageSummaryCache.get(wineId);
            if (cached) {
                renderVintageView(cached);
                return;
            }

            showStatus('Loading vintages…');
            row.dataset.inlineLoading = 'true';

            cancelInlineDetailsRequest();
            inlineDetailsAbortController = new AbortController();
            const abortController = inlineDetailsAbortController;

            try {
                const response = await sendJson(`/wine-manager/wine/${encodeURIComponent(wineId)}/details`, {
                    method: 'GET',
                    signal: abortController.signal
                });
                const details = Array.isArray(response?.details) ? response.details : [];
                const aggregated = aggregateVintageCounts(details);
                vintageSummaryCache.set(wineId, aggregated);

                if (!inlineRow.isConnected || expandedInventoryRow !== row) {
                    return;
                }

                renderVintageView(aggregated);
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

        function renderVintageRows(tbody, vintages) {
            if (!tbody || !inventoryInlineRowTemplate?.content?.firstElementChild) {
                return;
            }

            tbody.innerHTML = '';

            vintages.forEach((vintage) => {
                const templateRow = inventoryInlineRowTemplate.content.firstElementChild.cloneNode(true);
                const vintageCell = templateRow.querySelector('[data-vintage]');
                const scoreCell = templateRow.querySelector('[data-score]');
                const bottleCell = templateRow.querySelector('[data-bottle-count]');
                const bottleDisplay = templateRow.querySelector('[data-bottle-count-display]');
                const bottleAccessible = templateRow.querySelector('[data-bottle-count-accessible]');
                const drinkingWindowStartCell = templateRow.querySelector('[data-drinking-window-start]');
                const drinkingWindowEndCell = templateRow.querySelector('[data-drinking-window-end]');
                const noteCell = templateRow.querySelector('[data-note]');

                if (vintageCell) {
                    vintageCell.textContent = formatVintageLabel(vintage?.vintage);
                }

                if (scoreCell) {
                    scoreCell.textContent = formatAverageScore(vintage?.averageScore);
                }

                if (drinkingWindowStartCell) {
                    drinkingWindowStartCell.textContent = formatDrinkingWindowValue(
                        vintage?.userDrinkingWindowStartYear ?? vintage?.drinkingWindowStartYear
                    );
                }

                if (drinkingWindowEndCell) {
                    drinkingWindowEndCell.textContent = formatDrinkingWindowValue(
                        vintage?.userDrinkingWindowEndYear ?? vintage?.drinkingWindowEndYear
                    );
                }

                const availableCount = typeof vintage?.availableCount === 'number'
                    ? vintage.availableCount
                    : (typeof vintage?.count === 'number' ? vintage.count : 0);
                const enjoyedCount = typeof vintage?.drunkCount === 'number'
                    ? vintage.drunkCount
                    : 0;
                const totalCount = typeof vintage?.totalCount === 'number'
                    ? vintage.totalCount
                    : availableCount + enjoyedCount;

                const formattedAvailable = Number.isFinite(availableCount)
                    ? availableCount
                    : 0;
                const formattedTotal = Number.isFinite(totalCount)
                    ? totalCount
                    : formattedAvailable + (Number.isFinite(enjoyedCount) ? enjoyedCount : 0);
                const formattedEnjoyed = Number.isFinite(enjoyedCount)
                    ? enjoyedCount
                    : 0;

                if (bottleCell) {
                    bottleCell.dataset.availableCount = String(formattedAvailable);
                    bottleCell.dataset.totalCount = String(formattedTotal);
                    bottleCell.dataset.enjoyedCount = String(formattedEnjoyed);
                }

                if (bottleDisplay) {
                    bottleDisplay.textContent = `${formattedAvailable.toLocaleString()}/${formattedTotal.toLocaleString()}`;
                }

                if (bottleAccessible) {
                    const availableNoun = formattedAvailable === 1 ? 'bottle' : 'bottles';
                    const totalNoun = formattedTotal === 1 ? 'bottle' : 'bottles';
                    const enjoyedNoun = formattedEnjoyed === 1 ? 'bottle' : 'bottles';
                    bottleAccessible.textContent = `${formattedAvailable} ${availableNoun} available of a total of ${formattedTotal} ${totalNoun} (${formattedEnjoyed} ${enjoyedNoun} enjoyed)`;
                }

                if (noteCell) {
                    const note = typeof vintage?.note === 'string' ? vintage.note.trim() : '';
                    noteCell.textContent = note || '—';
                    noteCell.title = note || '';
                }

                const counts = {
                    availableCount,
                    drunkCount: enjoyedCount,
                    totalCount
                };

                bindVintageInlineRow(templateRow, { ...vintage, ...counts });
                tbody.appendChild(templateRow);
            });
        }

        function bindVintageInlineRow(row, vintage) {
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
            const availableCount = typeof vintage?.availableCount === 'number'
                ? vintage.availableCount
                : (typeof vintage?.count === 'number' ? vintage.count : 0);
            const drunkCount = typeof vintage?.drunkCount === 'number' ? vintage.drunkCount : 0;
            const totalCount = typeof vintage?.totalCount === 'number'
                ? vintage.totalCount
                : availableCount + drunkCount;
            const totalBottleNoun = totalCount === 1 ? 'bottle' : 'bottles';
            row.setAttribute(
                'aria-label',
                `View ${totalCount} ${totalBottleNoun} from vintage ${vintageLabel} (${availableCount} available, ${drunkCount} enjoyed)`
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

        function aggregateVintageCounts(details) {
            const results = new Map();

            if (!Array.isArray(details)) {
                return [];
            }

            details.forEach((detail) => {
                const vintageId = detail?.wineVintageId;
                if (!vintageId) {
                    return;
                }

                const existing = results.get(vintageId);
                const hasScore = typeof detail?.currentUserScore === 'number'
                    && Number.isFinite(detail.currentUserScore);
                const scoreValue = hasScore ? detail.currentUserScore : 0;
                const noteText = typeof detail?.currentUserNote === 'string' ? detail.currentUserNote.trim() : '';
                const startYear = normalizeDrinkingWindowYear(
                    detail?.userDrinkingWindowStartYear ?? detail?.drinkingWindowStartYear
                );
                const endYear = normalizeDrinkingWindowYear(
                    detail?.userDrinkingWindowEndYear ?? detail?.drinkingWindowEndYear
                );

                const isDrunk = Boolean(detail?.isDrunk);

                if (existing) {
                    existing.totalCount += 1;
                    if (isDrunk) {
                        existing.drunkCount += 1;
                    } else {
                        existing.availableCount += 1;
                    }
                    if (hasScore) {
                        existing.scoreTotal += scoreValue;
                        existing.scoreCount += 1;
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
                } else {
                    results.set(vintageId, {
                        wineVintageId: vintageId,
                        vintage: detail?.vintage,
                        totalCount: 1,
                        availableCount: isDrunk ? 0 : 1,
                        drunkCount: isDrunk ? 1 : 0,
                        scoreTotal: scoreValue,
                        scoreCount: hasScore ? 1 : 0,
                        note: noteText,
                        userDrinkingWindowStartYear: startYear,
                        userDrinkingWindowEndYear: endYear
                    });
                }
            });

            const aggregated = Array.from(results.values()).map((entry) => {
                const averageScore = entry.scoreCount > 0
                    ? Math.round((entry.scoreTotal / entry.scoreCount) * 10) / 10
                    : null;

                return {
                    wineVintageId: entry.wineVintageId,
                    vintage: entry.vintage,
                    count: entry.availableCount,
                    availableCount: entry.availableCount,
                    drunkCount: entry.drunkCount,
                    totalCount: entry.totalCount,
                    averageScore,
                    note: entry.note,
                    userDrinkingWindowStartYear: entry.userDrinkingWindowStartYear,
                    userDrinkingWindowEndYear: entry.userDrinkingWindowEndYear
                };
            });
            aggregated.sort((a, b) => {
                const aVintage = typeof a.vintage === 'number' ? a.vintage : Number.NEGATIVE_INFINITY;
                const bVintage = typeof b.vintage === 'number' ? b.vintage : Number.NEGATIVE_INFINITY;
                return bVintage - aVintage;
            });

            return aggregated;
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

        function formatDrinkingWindowValue(value) {
            if (typeof value === 'number' && Number.isFinite(value) && value > 0) {
                return String(value);
            }

            return '—';
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

        const existingCards = Array.from(locationList.querySelectorAll('[data-location-card]'));
        existingCards.forEach((card) => {
            setLocationDatasetCounts(card);
            updateLocationCardCounts(card);
            bindLocationCard(card);
        });

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

            configureLocationActionMenu(card);

            const editButton = card.querySelector('[data-location-edit]');
            const deleteButton = card.querySelector('[data-location-delete]');
            const form = card.querySelector('[data-location-edit-form]');
            const cancelButton = form?.querySelector('[data-location-cancel]');
            const input = form?.querySelector('[data-location-input]');
            const capacityInput = form?.querySelector('[data-location-capacity-input]');

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
        }

        function setLocationDatasetCounts(card, counts) {
            if (!card) {
                return;
            }

            const bottleCount = Number(counts?.bottleCount ?? card.dataset.bottleCount ?? 0) || 0;
            const uniqueCount = Number(counts?.uniqueCount ?? card.dataset.uniqueCount ?? 0) || 0;
            const drunkCount = Number(counts?.drunkCount ?? card.dataset.drunkCount ?? 0) || 0;
            let cellaredCount = Number(counts?.cellaredCount ?? card.dataset.cellaredCount ?? (bottleCount - drunkCount)) || 0;

            if (cellaredCount < 0) {
                cellaredCount = 0;
            }

            card.dataset.bottleCount = String(Math.max(bottleCount, 0));
            card.dataset.uniqueCount = String(Math.max(uniqueCount, 0));
            card.dataset.cellaredCount = String(cellaredCount);
            card.dataset.drunkCount = String(Math.max(drunkCount, 0));
        }

        function updateLocationCardCounts(card) {
            if (!card) {
                return;
            }

            const bottleCount = Number(card.dataset.bottleCount ?? '0') || 0;
            const cellaredCount = Number(card.dataset.cellaredCount ?? '0') || 0;
            const drunkCount = Number(card.dataset.drunkCount ?? '0') || 0;
            const capacity = getLocationCapacity(card);

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
                            ? `${cellaredCount} cellared · ${drunkCount} enjoyed`
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
                    const safeCellared = Math.max(cellaredCount, 0);
                    let description = `${safeCellared} cellared · ${drunkCount} enjoyed`;

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
