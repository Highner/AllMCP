window.WineInventoryTables = window.WineInventoryTables || {};
window.WineInventoryTables.initialize = function () {
            if (window.WineInventoryTables.__initialized) {
                return;
            }

            window.WineInventoryTables.__initialized = true;

                        // Global suppression for accidental re-activation when closing details
                        let __suppressNextPointerSequenceUntil = 0;
                        function suppressNextPointerSequence(durationMs = 200) {
                            __suppressNextPointerSequenceUntil = Date.now() + Math.max(0, durationMs);
                        }
                        function isPointerSequenceSuppressed() {
                            return Date.now() < __suppressNextPointerSequenceUntil;
                        }

            const filtersForm = document.querySelector('form.filters');
            const clearFiltersButton = filtersForm?.querySelector('[data-clear-filters]');

            if (filtersForm && clearFiltersButton) {
                clearFiltersButton.addEventListener('click', (event) => {
                    event.preventDefault();
                    const controls = Array.from(filtersForm.querySelectorAll('[data-default-value]'));

                    controls.forEach((control) => {
                        const defaultValue = control.getAttribute('data-default-value');

                        if (defaultValue == null) {
                            return;
                        }

                        if (control instanceof HTMLInputElement || control instanceof HTMLSelectElement || control instanceof HTMLTextAreaElement) {
                            control.value = defaultValue;
                        }
                    });

                    if (typeof filtersForm.requestSubmit === 'function') {
                        filtersForm.requestSubmit();
                    } else {
                        filtersForm.submit();
                    }
                });
            }

            const inventoryTable = document.getElementById('inventory-table');
            const detailsTable = document.getElementById('details-table');
            const detailsBody = detailsTable?.querySelector('#details-table-body');
            const detailAddRow = document.getElementById('detail-add-row');
            const emptyRow = detailsBody?.querySelector('.empty-row');
            const detailsTitle = document.getElementById('details-title');
            const detailsTitleName = document.getElementById('details-title-name');
            const detailsTitleVintage = document.getElementById('details-title-vintage');
            const detailsSubtitle = document.getElementById('details-subtitle');
            const messageBanner = document.getElementById('details-message');
            const detailsPanel = document.querySelector('.details-panel') ?? document.querySelector('[data-crud-table="details"]');
            const notesPanel = document.getElementById('notes-panel');
            const notesTable = document.getElementById('notes-table');
            const notesBody = notesTable?.querySelector('tbody');
            const notesAddRow = notesTable?.querySelector('#note-add-row');
            const notesEmptyRow = notesBody?.querySelector('.empty-row');
            const notesAddUserDisplay = notesAddRow?.querySelector('.note-add-user-name');
            const notesAddScore = notesAddRow?.querySelector('.note-add-score');
            const notesAddText = notesAddRow?.querySelector('.note-add-text');
            const notesAddButton = notesAddRow?.querySelector('.note-add-submit');
            const notesMessage = document.getElementById('notes-message');
            const notesTitle = document.getElementById('notes-title');
            const notesSubtitle = document.getElementById('notes-subtitle');
            const notesCloseButton = document.getElementById('notes-close');
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
            const currentUserId = locationSection?.dataset?.currentUserId ?? locationSection?.getAttribute('data-current-user-id') ?? '';
            const notesEnabled = Boolean(notesPanel && notesTable && notesBody && notesAddRow && notesEmptyRow && notesAddUserDisplay && notesAddScore && notesAddText && notesAddButton && notesMessage && notesTitle && notesSubtitle && notesCloseButton);
            const summaryColumnCount = inventoryTable?.querySelectorAll('thead th')?.length ?? 1;
            const groupingSelect = document.querySelector('[data-inventory-group-select]');
            let activeGroupingKey = groupingSelect?.value ?? 'wine';
            let groupExpansionState = {};
            let nextSummaryRowIndex = 0;
            const MAX_LOCATION_CAPACITY = 10000;

            // Ensure the Inventory Add modal can open even if other sections fail initialization
            if (!window.WineInventoryTables.__addModalBound) {
                const __addBtn = document.querySelector('.inventory-add-trigger');
                const __overlay = document.getElementById('inventory-add-overlay');
                const __popover = document.getElementById('inventory-add-popover');
                const __sharedController = window.wineSurferFavorites;
                const __openSharedModal = typeof __sharedController?.openAddWineModal === 'function'
                    ? (event) => {
                        event?.preventDefault?.();
                        try {
                            const __result = __sharedController.openAddWineModal({ source: 'inventory' });
                            if (__result && typeof __result.catch === 'function') {
                                __result.catch(() => { /* status handled by shared controller */ });
                            }
                        } catch { /* no-op */ }
                    }
                    : null;

                if (!__addBtn) {
                    // No trigger available, skip binding entirely
                } else if (__openSharedModal) {
                    window.WineInventoryTables.__addModalBound = true;
                    __addBtn.addEventListener('click', __openSharedModal);
                } else if (__overlay && __popover) {
                    window.WineInventoryTables.__addModalBound = true;

                    // If the dedicated InventoryAddModal exists, delegate to it to avoid duplicate logic/listeners
                    if (window.InventoryAddModal) {
                        const __open = () => {
                            try {
                                window.InventoryAddModal.open();
                            } catch { /* no-op */ }
                        };
                        const __close = () => {
                            try {
                                window.InventoryAddModal.close();
                            } catch { /* no-op */ }
                        };
                        __addBtn.addEventListener('click', __open);
                        __overlay.addEventListener('click', (e) => { if (e.target === __overlay) { __close(); } });
                        __popover.querySelector('[data-add-wine-close]')?.addEventListener('click', __close);
                        __popover.querySelector('.inventory-add-cancel')?.addEventListener('click', __close);
                    } else {
                        // Fallback lightweight open/close if the dedicated modal controller is not available
                        const __open = () => {
                            try {
                                __overlay.hidden = false;
                                __overlay.setAttribute('aria-hidden', 'false');
                                __overlay.classList.add('is-open');
                                document.body.style.overflow = 'hidden';
                                const __search = __popover.querySelector('.inventory-add-wine-search');
                                __search && typeof __search.focus === 'function' && __search.focus();
                            } catch { /* no-op */ }
                        };
                        const __close = () => {
                            try {
                                __overlay.classList.remove('is-open');
                                __overlay.setAttribute('aria-hidden', 'true');
                                __overlay.hidden = true;
                                document.body.style.overflow = '';
                            } catch { /* no-op */ }
                        };
                        __addBtn.addEventListener('click', __open);
                        __overlay.addEventListener('click', (e) => { if (e.target === __overlay) { __close(); } });
                        __popover.querySelector('[data-add-wine-close]')?.addEventListener('click', __close);
                        __popover.querySelector('.inventory-add-cancel')?.addEventListener('click', __close);
                    }
                }
            }

            // Relaxed guard: allow initialization even if detailAddRow is missing; most features check for elements when needed.
            if (!inventoryTable || !detailsTable || !detailsBody || !emptyRow || !detailsTitle || !detailsSubtitle || !messageBanner) {
                return;
            }

            let activeDetailActionsMenu = null;

            document.addEventListener('click', (event) => {
                if (!activeDetailActionsMenu) {
                    return;
                }

                const menu = activeDetailActionsMenu;

                if (!menu || !document.body.contains(menu)) {
                    activeDetailActionsMenu = null;
                    return;
                }

                if (menu.contains(event.target)) {
                    return;
                }

                closeActiveDetailActionsMenu();
            });

            document.addEventListener('keydown', (event) => {
                if (event.key === 'Escape') {
                    closeActiveDetailActionsMenu({ focusTrigger: true });
                }
            });

            const addWineButton = document.querySelector('.inventory-add-trigger');
            const addWineOverlay = document.getElementById('inventory-add-overlay');
            const addWinePopover = document.getElementById('inventory-add-popover');
            const addWineForm = addWinePopover?.querySelector('.inventory-add-form');
            const addWineSearch = addWinePopover?.querySelector('.inventory-add-wine-search');
            const addWineHiddenInput = addWinePopover?.querySelector('.inventory-add-wine-id');
            const addWineResults = addWinePopover?.querySelector('.inventory-add-wine-results');
            const addWineCombobox = addWinePopover?.querySelector('.inventory-add-combobox');
            const addWineVintage = addWinePopover?.querySelector('.inventory-add-vintage');
            const addWineLocation = addWinePopover?.querySelector('.inventory-add-location');
            const addWineQuantity = addWinePopover?.querySelector('.inventory-add-quantity');
            const addWineSummary = addWinePopover?.querySelector('.inventory-add-summary');
            const addWineHint = addWinePopover?.querySelector('.inventory-add-vintage-hint');
            const addWineError = addWinePopover?.querySelector('.inventory-add-error');
            const addWineSubmit = addWinePopover?.querySelector('.inventory-add-submit');
            const addWineCancel = addWinePopover?.querySelector('.inventory-add-cancel');
            const addWineClose = addWinePopover?.querySelector('[data-add-wine-close]');
            const wineSurferOverlay = document.getElementById('inventory-wine-surfer-overlay');
            const wineSurferPopover = document.getElementById('inventory-wine-surfer-popover');
            const wineSurferClose = wineSurferPopover?.querySelector('[data-wine-surfer-close]');
            const wineSurferStatus = wineSurferPopover?.querySelector('.inventory-wine-surfer-status');
            const wineSurferList = wineSurferPopover?.querySelector('.inventory-wine-surfer-list');
            const wineSurferIntro = wineSurferPopover?.querySelector('.inventory-wine-surfer-intro');
            const wineSurferQueryLabel = wineSurferPopover?.querySelector('.inventory-wine-surfer-query');

            const drinkOverlay = document.getElementById('drink-bottle-overlay');
            const drinkPopover = document.getElementById('drink-bottle-popover');
            const drinkForm = drinkPopover?.querySelector('.drink-bottle-form');
            const drinkDateInput = drinkPopover?.querySelector('.drink-bottle-date');
            const drinkScoreInput = drinkPopover?.querySelector('.drink-bottle-score');
            const drinkScoreIsRange = (drinkScoreInput?.type ?? '').toLowerCase() === 'range';
            const drinkScoreDisplay = drinkPopover?.querySelector('.drink-bottle-score-display');
            const drinkScoreClearButton = drinkPopover?.querySelector('.drink-bottle-score-clear');
            const drinkScoreDefaultValue = drinkScoreInput?.dataset?.defaultValue
                ?? drinkScoreInput?.getAttribute('data-default-value')
                ?? drinkScoreInput?.getAttribute('min')
                ?? '5';
            const drinkNoteInput = drinkPopover?.querySelector('.drink-bottle-note');
            const drinkError = drinkPopover?.querySelector('.drink-bottle-error');
            const drinkCancelButton = drinkPopover?.querySelector('.drink-bottle-cancel');
            const drinkSubmitButton = drinkPopover?.querySelector('.drink-bottle-submit');
            const drinkTitle = drinkPopover?.querySelector('.drink-bottle-title');
            const drinkHeaderCloseButton = drinkPopover?.querySelector('[data-drink-bottle-close]');

            const detailAddLocation = detailAddRow?.querySelector('.detail-add-location');
            const detailAddPrice = detailAddRow?.querySelector('.detail-add-price');
            const detailAddQuantity = detailAddRow?.querySelector('.detail-add-quantity-select');
            const detailAddButton = detailAddRow?.querySelector('.detail-add-submit');
            const inventorySection = document.getElementById('inventory-view');
            const detailsSection = document.getElementById('details-view');
            const detailsCloseButton = document.getElementById('details-close-button');

            // Enforce initial state to avoid any flash of the details panel
            if (inventorySection) {
                inventorySection.hidden = false;
                inventorySection.setAttribute('aria-hidden', 'false');
            }
            if (detailsSection) {
                detailsSection.hidden = true;
                detailsSection.setAttribute('aria-hidden', 'true');
            }
            showInventoryView();

            let selectedGroupId = null;
            let selectedSummary = null;
            let selectedRow = null;
            let notesSelectedBottleId = null;
            let selectedDetailRowElement = null;
            let loading = false;
            let notesLoading = false;
            let modalLoading = false;
            let drinkModalLoading = false;
            let drinkTarget = null;
            const DETAIL_ROW_DATA_KEY = '__wineInventoryDetail';

            const referenceData = {
                subAppellations: [],
                bottleLocations: [],
                users: []
            };
            const referenceDataPromise = loadReferenceData();
            let wineOptions = [];
            let selectedWineOption = null;
            let wineSearchTimeoutId = null;
            let wineSearchController = null;
            let wineSearchLoading = false;
            let wineSearchError = '';
            let currentWineQuery = '';
            let activeWineOptionIndex = -1;
            let lastCompletedQuery = '';
            let wineSurferLoading = false;
            let wineSurferError = '';
            let wineSurferResults = [];
            let wineSurferActiveQuery = '';
            let wineSurferController = null;
            let wineSurferSelectionPending = false;
            let wineSurferStreamChunkCount = 0;

            resetDetailsView();

            initializeLocationSection();
            initializeSummaryRows();
            bindGroupingControl();
            refreshGrouping({ resetState: true });
            const sharedAddWineModalAvailable = typeof window.wineSurferFavorites?.openAddWineModal === 'function';
            // Avoid binding the legacy Add Wine popover logic if the shared controller or dedicated InventoryAddModal is present
            if (!sharedAddWineModalAvailable && !window.InventoryAddModal) {
                bindAddWinePopover();
            }
            bindDetailAddRow();
            bindDrinkBottleModal();
            initializeDrinkScoreControl();
            bindDetailsCloseButton();
            if (notesEnabled) {
                bindNotesPanel();
            }

            function initializeSummaryRows() {
                const rows = Array.from(inventoryTable.querySelectorAll('tbody tr.group-row'));
                rows.forEach((row, index) => {
                    ensureRowInitialIndex(row, index);
                    attachSummaryRowHandlers(row);
                });

                if (rows.length > nextSummaryRowIndex) {
                    nextSummaryRowIndex = rows.length;
                }
            }

            function ensureRowInitialIndex(row, explicitIndex) {
                if (!row) {
                    return;
                }

                if (explicitIndex != null && !Number.isNaN(Number(explicitIndex))) {
                    row.dataset.initialIndex = String(explicitIndex);
                    nextSummaryRowIndex = Math.max(nextSummaryRowIndex, Number(explicitIndex) + 1);
                    return;
                }

                const existingValue = row.dataset.initialIndex;
                if (existingValue && existingValue !== '') {
                    const parsed = Number(existingValue);
                    if (!Number.isNaN(parsed)) {
                        nextSummaryRowIndex = Math.max(nextSummaryRowIndex, parsed + 1);
                        return;
                    }
                }

                row.dataset.initialIndex = String(nextSummaryRowIndex);
                nextSummaryRowIndex += 1;
            }

            function getInitialRowIndex(row) {
                if (!row) {
                    return Number.MAX_SAFE_INTEGER;
                }

                const value = Number(row.dataset?.initialIndex ?? row.getAttribute?.('data-initial-index'));
                return Number.isFinite(value) ? value : Number.MAX_SAFE_INTEGER;
            }

            function compareByInitialIndex(a, b) {
                return getInitialRowIndex(a) - getInitialRowIndex(b);
            }

            function sortRowsByInitialIndex(rows) {
                return (rows ?? []).slice().sort(compareByInitialIndex);
            }

            const GROUPING_CONFIG = {
                wine: {
                    key: 'wine',
                    label: 'Wine',
                    emptyLabel: 'Wine not set',
                    getValue: (row) => normalizeGroupingValue(getRowGroupingValue(row, 'wine')),
                    getDisplay(value) {
                        return formatGroupingDisplay(value, this.emptyLabel);
                    },
                    buildLabel(display) {
                        return buildGroupingLabel(this.label, display);
                    }
                },
                status: {
                    key: 'status',
                    label: 'Status',
                    emptyLabel: 'Status not set',
                    getValue: (row) => normalizeGroupingValue(getRowGroupingValue(row, 'status')),
                    getDisplay(value) {
                        return formatGroupingDisplay(value, this.emptyLabel);
                    },
                    buildLabel(display) {
                        return buildGroupingLabel(this.label, display);
                    }
                },
                color: {
                    key: 'color',
                    label: 'Color',
                    emptyLabel: 'Color not set',
                    getValue: (row) => normalizeGroupingValue(getRowGroupingValue(row, 'color')),
                    getDisplay(value) {
                        return formatGroupingDisplay(value, this.emptyLabel);
                    },
                    buildLabel(display) {
                        return buildGroupingLabel(this.label, display);
                    }
                },
                appellation: {
                    key: 'appellation',
                    label: 'Appellation',
                    emptyLabel: 'Appellation not set',
                    getValue: (row) => normalizeGroupingValue(getRowGroupingValue(row, 'appellation')),
                    getDisplay(value) {
                        return formatGroupingDisplay(value, this.emptyLabel);
                    },
                    buildLabel(display) {
                        return buildGroupingLabel(this.label, display);
                    }
                },
                vintage: {
                    key: 'vintage',
                    label: 'Vintage',
                    emptyLabel: 'Vintage not set',
                    getValue: (row) => normalizeGroupingValue(getRowGroupingValue(row, 'vintage')),
                    getDisplay(value) {
                        return formatGroupingDisplay(value, this.emptyLabel);
                    },
                    buildLabel(display) {
                        return buildGroupingLabel(this.label, display);
                    }
                }
            };

            if (activeGroupingKey !== 'none' && !Object.prototype.hasOwnProperty.call(GROUPING_CONFIG, activeGroupingKey)) {
                activeGroupingKey = 'none';
            }

            function bindGroupingControl() {
                if (!groupingSelect) {
                    return;
                }

                groupingSelect.addEventListener('change', () => {
                    const selectedValue = groupingSelect.value ?? 'none';
                    const normalizedValue = Object.prototype.hasOwnProperty.call(GROUPING_CONFIG, selectedValue)
                        ? selectedValue
                        : 'none';
                    const hasChanged = normalizedValue !== activeGroupingKey;
                    activeGroupingKey = normalizedValue;
                    if (hasChanged) {
                        groupExpansionState = {};
                    }
                    refreshGrouping({ expandForRow: selectedRow });
                });
            }

            function refreshGrouping(options = {}) {
                if (options.resetState) {
                    groupExpansionState = {};
                }

                applyGrouping({ expandForRow: options.expandForRow ?? selectedRow ?? null });
            }

            function applyGrouping(options = {}) {
                const tbody = inventoryTable.querySelector('tbody');
                if (!tbody) {
                    return;
                }

                const rows = Array.from(tbody.querySelectorAll('tr.group-row'));
                const headerRows = Array.from(tbody.querySelectorAll('tr.summary-group-row'));
                headerRows.forEach(row => row.remove());

                rows.forEach(row => {
                    row.hidden = false;
                    row.classList.remove('group-row--child');
                    row.removeAttribute('data-group-parent');
                });

                const config = GROUPING_CONFIG[activeGroupingKey];
                if (!config) {
                    inventoryTable.classList.remove('inventory-table--grouped');
                    if (rows.length > 0) {
                        const fragment = document.createDocumentFragment();
                        const orderedRows = sortRowsByInitialIndex(rows);
                        orderedRows.forEach(row => fragment.appendChild(row));
                        tbody.appendChild(fragment);
                    }
                    return;
                }

                if (rows.length === 0) {
                    inventoryTable.classList.remove('inventory-table--grouped');
                    return;
                }

                inventoryTable.classList.add('inventory-table--grouped');

                const groups = [];
                const groupMap = new Map();

                rows.forEach(row => {
                    const value = config.getValue(row);
                    const key = buildGroupKey(value, config.key);
                    let group = groupMap.get(key);
                    if (!group) {
                        group = {
                            key,
                            value,
                            display: config.getDisplay(value),
                            rows: []
                        };
                        groupMap.set(key, group);
                        groups.push(group);
                    }

                    group.rows.push(row);
                });

                const expandForRow = options.expandForRow ?? null;
                const validKeys = new Set(groups.map(group => group.key));

                Object.keys(groupExpansionState).forEach((key) => {
                    if (!validKeys.has(key)) {
                        delete groupExpansionState[key];
                    }
                });

                const fragment = document.createDocumentFragment();
                groups.forEach(group => {
                    const groupRows = sortRowsByInitialIndex(group.rows);
                    group.rows = groupRows;
                    const containsSelected = Boolean(selectedRow && groupRows.includes(selectedRow));
                    const containsExpandTarget = Boolean(expandForRow && groupRows.includes(expandForRow));
                    let expanded;
                    if (groupExpansionState[group.key] !== undefined) {
                        expanded = groupExpansionState[group.key];
                    } else {
                        expanded = containsSelected || containsExpandTarget || false;
                        groupExpansionState[group.key] = expanded;
                    }

                    const headerRow = buildGroupingHeaderRow(group, config, expanded);
                    fragment.appendChild(headerRow);

                    groupRows.forEach(row => {
                        row.classList.add('group-row--child');
                        row.dataset.groupParent = group.key;
                        row.hidden = !expanded;
                        fragment.appendChild(row);
                    });
                });

                tbody.appendChild(fragment);
            }

            function buildGroupingHeaderRow(group, config, expanded) {
                const row = document.createElement('tr');
                row.className = 'summary-group-row';
                if (expanded) {
                    row.classList.add('summary-group-row--expanded');
                }
                row.dataset.groupKey = group.key;
                // For wine grouping, capture representative wineId from first child row
                if (config?.key === 'wine' && group?.rows?.length) {
                    const firstChild = group.rows[0];
                    const wineId = firstChild?.dataset?.wineId ?? firstChild?.getAttribute?.('data-wine-id');
                    if (wineId) {
                        row.dataset.wineId = wineId;
                    }
                }
                row.setAttribute('tabindex', '0');
                row.setAttribute('role', 'button');
                row.setAttribute('aria-expanded', expanded ? 'true' : 'false');

                const cell = document.createElement('td');
                cell.colSpan = summaryColumnCount > 0 ? summaryColumnCount : 1;

                const content = document.createElement('div');
                content.className = 'summary-group-row__content';

                const indicator = document.createElement('span');
                indicator.className = 'summary-group-row__chevron';
                content.appendChild(indicator);

                const label = document.createElement('span');
                label.className = 'summary-group-row__label';
                const displayLabel = typeof config.buildLabel === 'function'
                    ? config.buildLabel(group.display)
                    : buildGroupingLabel(config.label, group.display);
                label.textContent = displayLabel;
                content.appendChild(label);

                const count = document.createElement('span');
                count.className = 'summary-group-row__count';
                count.textContent = formatGroupCount(group.rows.length);
                content.appendChild(count);

                cell.appendChild(content);
                row.appendChild(cell);

                row.addEventListener('click', async () => {
                    const wasExpanded = row.classList.contains('summary-group-row--expanded');
                    toggleGroupExpansion(group.key);
                    // After toggling, determine new state
                    const isExpanded = !wasExpanded;
                    if (config?.key === 'wine') {
                        await ensureWineSubtable(row, isExpanded);
                    }
                });
                row.addEventListener('keydown', async (event) => {
                    if (event.key === 'Enter' || event.key === ' ') {
                        event.preventDefault();
                        const wasExpanded = row.classList.contains('summary-group-row--expanded');
                        toggleGroupExpansion(group.key);
                        const isExpanded = !wasExpanded;
                        if (config?.key === 'wine') {
                            await ensureWineSubtable(row, isExpanded);
                        }
                    }
                });

                return row;
            }

            function toggleGroupExpansion(groupKey, desiredState) {
                const key = String(groupKey);
                const nextState = desiredState != null
                    ? Boolean(desiredState)
                    : !(groupExpansionState[key] ?? false);
                groupExpansionState[key] = nextState;

                const header = inventoryTable.querySelector(`.summary-group-row[data-group-key="${escapeSelectorValue(key)}"]`);
                if (header) {
                    header.setAttribute('aria-expanded', nextState ? 'true' : 'false');
                    header.classList.toggle('summary-group-row--expanded', nextState);
                }

                const childRows = Array.from(inventoryTable.querySelectorAll(`.group-row[data-group-parent="${escapeSelectorValue(key)}"]`));
                childRows.forEach(row => {
                    row.hidden = !nextState;
                });

                // Manage inline subtable visibility for wine grouping
                if (header && header.dataset && header.dataset.wineId) {
                    const existingSubtable = header.nextElementSibling;
                    const isSubtableRow = existingSubtable && existingSubtable.classList.contains('summary-subtable-row');
                    if (!nextState && isSubtableRow) {
                        existingSubtable.remove();
                    }
                }

                if (!nextState && selectedRow && childRows.includes(selectedRow)) {
                    showInventoryView();
                    resetDetailsView();
                }
            }

            async function ensureWineSubtable(headerRow, expanded) {
                return ensureWineSubtableCore(headerRow, expanded);
            }

            async function toggleWineRowSubtable(row) {
                if (!row) return;
                const wineId = row.dataset?.wineId ?? row.getAttribute?.('data-wine-id');
                if (!wineId) {
                    return;
                }
                // Toggle: if a subtable immediately follows this row, remove it; otherwise insert and load
                const next = row.nextElementSibling;
                const isSubtableRow = next && next.classList.contains('summary-subtable-row');
                if (isSubtableRow) {
                    next.remove();
                    return;
                }
                await ensureWineSubtableCore(row, true);
            }

            async function ensureWineSubtableCore(anchorRow, expanded) {
                try {
                    if (!anchorRow || !(anchorRow instanceof HTMLElement)) {
                        return;
                    }
                    const wineId = anchorRow.dataset?.wineId ?? anchorRow.getAttribute?.('data-wine-id');
                    if (!wineId) {
                        return;
                    }

                    // If collapsing, remove the subtable row if present
                    if (!expanded) {
                        const sibling = anchorRow.nextElementSibling;
                        if (sibling && sibling.classList.contains('summary-subtable-row')) {
                            sibling.remove();
                        }
                        return;
                    }

                    // If already present, do nothing
                    const next = anchorRow.nextElementSibling;
                    if (next && next.classList.contains('summary-subtable-row')) {
                        return;
                    }

                    // Insert placeholder row with spinner
                    const placeholder = document.createElement('tr');
                    placeholder.className = 'summary-subtable-row';
                    const cell = document.createElement('td');
                    cell.colSpan = summaryColumnCount > 0 ? summaryColumnCount : 1;
                    cell.innerHTML = '<div class="summary-subtable summary-subtable--loading">Loading vintages…</div>';
                    placeholder.appendChild(cell);
                    const parent = anchorRow.parentNode;
                    if (parent && typeof parent.insertBefore === 'function') {
                        parent.insertBefore(placeholder, anchorRow.nextSibling);
                    } else {
                        anchorRow.insertAdjacentElement('afterend', placeholder);
                    }

                    // Fetch details for the wine (all vintages in inventory)
                    const response = await fetch(`/wine-manager/wine/${encodeURIComponent(wineId)}/details`, { headers: { 'Accept': 'application/json' } });
                    if (!response.ok) {
                        throw new Error('Failed to load vintages');
                    }
                    const data = await response.json();
                    const subtable = buildWineVintagesSubtable(data);
                    cell.innerHTML = '';
                    cell.appendChild(subtable);
                } catch (error) {
                    const msg = error?.message ?? 'Failed to load vintages';
                    if (anchorRow && anchorRow.nextElementSibling && anchorRow.nextElementSibling.classList.contains('summary-subtable-row')) {
                        const cell = anchorRow.nextElementSibling.querySelector('td');
                        if (cell) {
                            cell.innerHTML = `<div class=\"summary-subtable summary-subtable--error\">${escapeHtml(msg)}</div>`;
                        }
                    }
                }
            }

            function buildWineVintagesSubtable(payload) {
                const details = Array.isArray(payload?.details ?? payload?.Details) ? (payload.details ?? payload.Details) : [];
                const grouped = new Map();
                details.forEach(item => {
                    const vintage = Number(item?.vintage ?? item?.Vintage ?? 0) || 0;
                    const vintageId = (item?.wineVintageId ?? item?.WineVintageId) ? String(item?.wineVintageId ?? item?.WineVintageId) : null;
                    const key = String(vintage);
                    let group = grouped.get(key);
                    if (!group) {
                        group = { vintage, count: 0, wineVintageId: vintageId };
                        grouped.set(key, group);
                    }
                    group.count += 1;
                    // Prefer a non-empty wineVintageId if present
                    if (!group.wineVintageId && vintageId) {
                        group.wineVintageId = vintageId;
                    }
                });
                const vintages = Array.from(grouped.values()).sort((a, b) => a.vintage - b.vintage);

                const container = document.createElement('div');
                container.className = 'summary-subtable';

                const table = document.createElement('table');
                table.className = 'crud-table__table summary-subtable__table';

                const thead = document.createElement('thead');
                const headerRow = document.createElement('tr');
                headerRow.className = 'crud-table__header-row';
                const hVintage = document.createElement('th');
                hVintage.textContent = 'Vintage';
                const hCount = document.createElement('th');
                hCount.textContent = 'Bottles';
                headerRow.appendChild(hVintage);
                headerRow.appendChild(hCount);
                thead.appendChild(headerRow);

                const tbody = document.createElement('tbody');
                if (vintages.length === 0) {
                    const empty = document.createElement('tr');
                    empty.className = 'crud-table__empty-row';
                    const cell = document.createElement('td');
                    cell.colSpan = 2;
                    cell.textContent = 'No vintages in this group.';
                    empty.appendChild(cell);
                    tbody.appendChild(empty);
                } else {
                    vintages.forEach(v => {
                        const row = document.createElement('tr');
                        row.setAttribute('tabindex', '0');
                        row.setAttribute('role', 'button');
                        if (v.wineVintageId) {
                            row.dataset.groupId = v.wineVintageId;
                        }
                        const cVintage = document.createElement('td');
                        cVintage.textContent = v.vintage ? String(v.vintage) : '—';
                        const cCount = document.createElement('td');
                        cCount.textContent = String(v.count);
                        row.appendChild(cVintage);
                        row.appendChild(cCount);

                        // Click/keyboard to load details for this vintage group
                        const activate = async () => {
                            if (!v.wineVintageId) return;
                            try {
                                showDetailsView();
                                await loadDetails({ groupId: v.wineVintageId }, false);
                            } catch (error) {
                                showMessage(error?.message ?? String(error), 'error');
                            }
                        };
                        row.addEventListener('click', (e) => {
                            e.preventDefault();
                            activate();
                        });
                        row.addEventListener('keydown', (e) => {
                            if (e.key === 'Enter' || e.key === ' ') {
                                e.preventDefault();
                                activate();
                            }
                        });

                        tbody.appendChild(row);
                    });
                }

                table.appendChild(thead);
                table.appendChild(tbody);
                container.appendChild(table);
                return container;
            }

            function escapeHtml(value) {
                const div = document.createElement('div');
                div.textContent = String(value ?? '');
                return div.innerHTML;
            }

            function formatGroupCount(count) {
                const value = Number(count) || 0;
                return value === 1 ? '1 wine' : `${value} wines`;
            }

            function escapeSelectorValue(value) {
                const raw = String(value);
                if (window.CSS?.escape) {
                    return window.CSS.escape(raw);
                }

                return raw.replace(/[^a-zA-Z0-9_-]/g, (character) => `\\${character}`);
            }

            function buildGroupKey(value, groupingKey) {
                const normalized = normalizeGroupingValue(value);
                const prefix = groupingKey ?? activeGroupingKey ?? 'group';
                const encoded = normalized ? encodeURIComponent(normalized.toLowerCase()) : 'empty';
                return `${prefix}|${encoded}`;
            }

            function getRowGroupingValue(row, key) {
                switch (key) {
                    case 'status':
                        return row.dataset.summaryStatus ?? '';
                    case 'color':
                        return row.dataset.summaryColor ?? '';
                    case 'appellation':
                        return row.dataset.summaryAppellation ?? '';
                    case 'vintage':
                        return row.dataset.summaryVintage ?? '';
                    case 'wine':
                        return row.dataset.summaryWine ?? '';
                    default:
                        return '';
                }
            }

            function normalizeGroupingValue(value) {
                if (value == null) {
                    return '';
                }

                const text = String(value).trim();
                if (!text || text === '—') {
                    return '';
                }

                return text;
            }

            function formatGroupingDisplay(value, emptyLabel) {
                const normalized = normalizeGroupingValue(value);
                return normalized || emptyLabel;
            }

            function buildGroupingLabel(label, display) {
                if (!label) {
                    return display;
                }

                return `${label}: ${display}`;
            }

            function attachSummaryRowHandlers(row) {
                row.addEventListener('click', (event) => {
                                    if (typeof isPointerSequenceSuppressed === 'function' && isPointerSequenceSuppressed()) {
                                        event.preventDefault();
                                        event.stopPropagation();
                                        return;
                                    }
                    const target = event.target;

                    if (target.closest('.save-group')) {
                        event.stopPropagation();
                        saveSummaryEdit(row);
                        return;
                    }

                    if (target.closest('.cancel-group')) {
                        event.stopPropagation();
                        cancelSummaryEdit(row);
                        return;
                    }

                    if (target.closest('.edit-group')) {
                        event.stopPropagation();
                        referenceDataPromise
                            .then(() => enterEditMode(row))
                            .catch(error => showMessage(error?.message ?? String(error), 'error'));
                        return;
                    }

                    if (target.closest('.delete-group')) {
                        event.stopPropagation();
                        handleSummaryDelete(row);
                        return;
                    }

                    if (row.classList.contains('editing')) {
                        return;
                    }

                    // If this is a wine-level group row (no single vintage), toggle the inline vintages subtable
                    const rowVintage = (row.dataset?.summaryVintage ?? row.getAttribute?.('data-summary-vintage') ?? '').trim();
                    const wineId = row.dataset?.wineId ?? row.getAttribute?.('data-wine-id') ?? '';
                    const isWineLevel = (!rowVintage || rowVintage === '—') && !!wineId;
                    if (isWineLevel) {
                        toggleWineRowSubtable(row).catch(error => showMessage(error?.message ?? String(error), 'error'));
                        return;
                    }

                    handleRowSelection(row).catch(error => showMessage(error?.message ?? String(error), 'error'));
                });

                row.addEventListener('keydown', (event) => {
                    if (row.classList.contains('editing')) {
                        return;
                    }

                    if (event.key === 'Enter' || event.key === ' ') {
                        event.preventDefault();

                        const rowVintage = (row.dataset?.summaryVintage ?? row.getAttribute?.('data-summary-vintage') ?? '').trim();
                        const wineId = row.dataset?.wineId ?? row.getAttribute?.('data-wine-id') ?? '';
                        const isWineLevel = (!rowVintage || rowVintage === '—') && !!wineId;
                        if (isWineLevel) {
                            toggleWineRowSubtable(row).catch(error => showMessage(error?.message ?? String(error), 'error'));
                            return;
                        }

                        handleRowSelection(row).catch(error => showMessage(error?.message ?? String(error), 'error'));
                    }
                });
            }

            function bindDetailsCloseButton() {
                const handleClose = () => {
                    const rowToFocus = selectedRow;
                    showInventoryView();
                    resetDetailsView();
                    if (rowToFocus) {
                        rowToFocus.focus();
                    }
                };

                const intercept = (event) => {
                    if (!event) return;
                    if (typeof event.preventDefault === 'function') event.preventDefault();
                    if (typeof event.stopImmediatePropagation === 'function') event.stopImmediatePropagation();
                    if (typeof event.stopPropagation === 'function') event.stopPropagation();
                };

                const isCloseTarget = (evt) => {
                    const el = evt?.target;
                    return el instanceof Element ? !!el.closest('[data-details-close]') : false;
                };

                // Direct binding to the explicit close button if present
                if (detailsCloseButton) {
                    // Intercept as early as possible so underlying elements don’t react
                    detailsCloseButton.addEventListener('pointerdown', (event) => {
                        intercept(event);
                        if (typeof suppressNextPointerSequence === 'function') suppressNextPointerSequence(350);
                        handleClose();
                    }, { capture: true });

                    detailsCloseButton.addEventListener('mousedown', (event) => {
                        intercept(event);
                    }, { capture: true });

                    detailsCloseButton.addEventListener('touchstart', (event) => {
                        intercept(event);
                    }, { capture: true, passive: false });

                    detailsCloseButton.addEventListener('click', (event) => {
                        intercept(event);
                        handleClose();
                    });
                }

                // Delegate inside the details section (covers clicks on child elements like the inner span)
                if (detailsSection) {
                    detailsSection.addEventListener('pointerdown', (event) => {
                        if (!isCloseTarget(event)) return;
                        intercept(event);
                    }, { capture: true });

                    detailsSection.addEventListener('mousedown', (event) => {
                        if (!isCloseTarget(event)) return;
                        intercept(event);
                    }, { capture: true });

                    detailsSection.addEventListener('touchstart', (event) => {
                        if (!isCloseTarget(event)) return;
                        intercept(event);
                    }, { capture: true, passive: false });

                    detailsSection.addEventListener('click', (event) => {
                        if (!isCloseTarget(event)) return;
                        intercept(event);
                        handleClose();
                    });
                }

                // Global defensive delegate to ensure the close works even if the section reference is missing
                const docEarlyIntercept = (event) => {
                    if (!isCloseTarget(event)) return;
                    intercept(event);
                };
                document.addEventListener('pointerdown', docEarlyIntercept, { capture: true });
                document.addEventListener('mousedown', docEarlyIntercept, { capture: true });
                document.addEventListener('touchstart', docEarlyIntercept, { capture: true, passive: false });

                document.addEventListener('click', (event) => {
                    if (!isCloseTarget(event)) return;
                    intercept(event);
                    handleClose();
                });

                // Allow Esc key to close the details panel for accessibility
                document.addEventListener('keydown', (event) => {
                    if (event.key === 'Escape') {
                        const isDetailsVisible = detailsSection && detailsSection.hidden === false;
                        if (isDetailsVisible) {
                            event.preventDefault();
                            handleClose();
                        }
                    }
                });
            }

            function bindAddWinePopover() {
                if (!addWineButton || !addWineOverlay || !addWinePopover) {
                    return;
                }

                closeAddWinePopover();

                addWineButton.addEventListener('click', () => {
                    openAddWinePopover().catch(error => showMessage(error?.message ?? String(error), 'error'));
                });

                const bindAddWineClose = (element) => {
                    if (!element) {
                        return;
                    }

                    element.addEventListener('click', () => {
                        closeAddWinePopover();
                    });
                };

                bindAddWineClose(addWineCancel);
                bindAddWineClose(addWineClose);

                const bindWineSurferClose = (element) => {
                    if (!element) {
                        return;
                    }

                    element.addEventListener('click', () => {
                        closeWineSurferPopover();
                    });
                };

                bindWineSurferClose(wineSurferClose);

                addWineOverlay.addEventListener('click', (event) => {
                    if (event.target === addWineOverlay) {
                        closeAddWinePopover();
                    }
                });

                wineSurferOverlay?.addEventListener('click', (event) => {
                    if (event.target === wineSurferOverlay) {
                        closeWineSurferPopover();
                    }
                });

                addWineForm?.addEventListener('submit', handleAddWineSubmit);

                addWineSearch?.addEventListener('input', handleWineSearchInput);
                addWineSearch?.addEventListener('keydown', handleWineSearchKeyDown);
                addWineSearch?.addEventListener('focus', handleWineSearchFocus);

                document.addEventListener('pointerdown', handleWinePointerDown);

                addWineLocation?.addEventListener('change', () => {
                    showAddWineError('');
                });

                document.addEventListener('keydown', (event) => {
                    if (event.key !== 'Escape') {
                        return;
                    }

                    if (wineSurferOverlay && !wineSurferOverlay.hidden) {
                        event.preventDefault();
                        closeWineSurferPopover();
                        return;
                    }

                    if (addWineOverlay && !addWineOverlay.hidden) {
                        event.preventDefault();
                        closeAddWinePopover();
                    }
                });
            }

            async function openAddWinePopover() {
                if (!addWineOverlay || !addWinePopover) {
                    return;
                }

                closeDrinkBottleModal();
                addWineOverlay.hidden = false;
                addWineOverlay.setAttribute('aria-hidden', 'false');
                addWineOverlay.classList.add('is-open');
                document.body.style.overflow = 'hidden';
                showAddWineError('');
                resetWineSelection();
                closeWineSurferPopover({ restoreFocus: false });

                setModalLoading(true);

                try {
                    await referenceDataPromise;
                    populateLocationSelect(addWineLocation, addWineLocation?.value ?? '');
                    if (addWineVintage) {
                        addWineVintage.value = '';
                    }
                    if (addWineLocation) {
                        addWineLocation.value = '';
                    }
                    if (addWineQuantity) {
                        addWineQuantity.value = '1';
                    }
                } catch (error) {
                    showAddWineError(error?.message ?? String(error));
                } finally {
                    setModalLoading(false);
                }

                addWineSearch?.focus();
            }

            function closeAddWinePopover() {
                if (!addWineOverlay) {
                    return;
                }

                closeDrinkBottleModal();
                closeWineSurferPopover({ restoreFocus: false });
                setModalLoading(false);
                addWineOverlay.classList.remove('is-open');
                addWineOverlay.setAttribute('aria-hidden', 'true');
                addWineOverlay.hidden = true;
                document.body.style.overflow = '';
                showAddWineError('');
                resetWineSelection();
                if (addWineVintage) {
                    addWineVintage.value = '';
                }
                if (addWineLocation) {
                    addWineLocation.value = '';
                }
                if (addWineQuantity) {
                    addWineQuantity.value = '1';
                }
            }

            async function handleAddWineSubmit(event) {
                event.preventDefault();

                if (loading || modalLoading) {
                    return;
                }

                const wineId = addWineHiddenInput?.value ?? '';
                const vintageValue = Number(addWineVintage?.value ?? '');
                const quantityValue = Number(addWineQuantity?.value ?? '1');
                const locationValue = addWineLocation?.value ?? '';

                if (!wineId) {
                    showAddWineError('Select a wine to add to your inventory.');
                    addWineSearch?.focus();
                    return;
                }

                if (!Number.isInteger(vintageValue)) {
                    showAddWineError('Enter a valid vintage year.');
                    addWineVintage?.focus();
                    return;
                }

                if (!Number.isInteger(quantityValue) || quantityValue < 1 || quantityValue > 12) {
                    showAddWineError('Select how many bottles to add.');
                    addWineQuantity?.focus();
                    return;
                }

                showAddWineError('');

                const payload = {
                    wineId,
                    vintage: vintageValue,
                    quantity: quantityValue,
                    bottleLocationId: locationValue || null
                };

                try {
                    setModalLoading(true);
                    setLoading(true);
                    const response = await sendJson('/wine-manager/inventory', {
                        method: 'POST',
                        body: JSON.stringify(payload)
                    });

                    await handleInventoryAddition(response, quantityValue);
                    closeAddWinePopover();
                } catch (error) {
                    showAddWineError(error?.message ?? 'Unable to add wine to your inventory.');
                } finally {
                    setModalLoading(false);
                    setLoading(false);
                }
            }

            function cancelWineSurferRequest() {
                if (wineSurferController) {
                    wineSurferController.abort();
                    wineSurferController = null;
                }
            }

            function renderWineSurferResults() {
                const hasQuery = Boolean(wineSurferActiveQuery);

                if (wineSurferIntro) {
                    wineSurferIntro.hidden = !hasQuery;
                    wineSurferIntro.setAttribute('aria-hidden', hasQuery ? 'false' : 'true');
                }

                if (wineSurferQueryLabel) {
                    wineSurferQueryLabel.textContent = hasQuery
                        ? `“${wineSurferActiveQuery}”`
                        : '';
                }

                if (wineSurferStatus) {
                    wineSurferStatus.textContent = '';
                    wineSurferStatus.classList.remove('is-error');

                    if (hasQuery) {
                        if (wineSurferSelectionPending) {
                            wineSurferStatus.textContent = 'Adding wine to your cellar…';
                        } else if (wineSurferLoading) {
                            if (wineSurferStreamChunkCount > 0) {
                                wineSurferStatus.textContent = wineSurferStreamChunkCount === 1
                                    ? 'Wine Surfer is gathering matches… (1 update received)'
                                    : `Wine Surfer is gathering matches… (${wineSurferStreamChunkCount} updates received)`;
                            } else {
                                wineSurferStatus.textContent = 'Wine Surfer is searching…';
                            }
                        } else if (wineSurferError) {
                            wineSurferStatus.textContent = wineSurferError;
                            wineSurferStatus.classList.add('is-error');
                        } else if (wineSurferResults.length === 0) {
                            wineSurferStatus.textContent = 'Wine Surfer could not find any matches.';
                        } else {
                            const count = wineSurferResults.length;
                            wineSurferStatus.textContent = count === 1
                                ? 'Wine Surfer found 1 match.'
                                : `Wine Surfer found ${count} matches.`;
                        }
                    }
                }

                if (wineSurferList) {
                    wineSurferList.innerHTML = '';

                    if (!hasQuery || wineSurferLoading || wineSurferResults.length === 0) {
                        return;
                    }

                    wineSurferResults.forEach((result) => {
                        const item = document.createElement('li');

                        const button = document.createElement('button');
                        button.type = 'button';
                        button.className = 'inventory-wine-surfer-item';
                        button.disabled = wineSurferSelectionPending;
                        if (wineSurferSelectionPending) {
                            button.setAttribute('aria-disabled', 'true');
                        } else {
                            button.removeAttribute('aria-disabled');
                        }
                        button.addEventListener('click', () => {
                            handleWineSurferSelection(result);
                        });

                        const name = document.createElement('span');
                        name.className = 'inventory-wine-surfer-item__name';
                        name.textContent = result.name;
                        button.appendChild(name);

                        const metaParts = [];
                        if (result.color) {
                            metaParts.push(`Color: ${result.color}`);
                        }

                        const locationParts = [];
                        if (result.subAppellation) {
                            locationParts.push(result.subAppellation);
                        }
                        if (result.appellation && !locationParts.includes(result.appellation)) {
                            locationParts.push(result.appellation);
                        }
                        if (result.region) {
                            locationParts.push(result.region);
                        }
                        if (result.country) {
                            locationParts.push(result.country);
                        }

                        if (locationParts.length > 0) {
                            metaParts.push(`Location: ${locationParts.join(' • ')}`);
                        }

                        if (metaParts.length > 0) {
                            const meta = document.createElement('span');
                            meta.className = 'inventory-wine-surfer-item__meta';
                            meta.textContent = metaParts.join(' • ');
                            button.appendChild(meta);
                        }

                        item.appendChild(button);
                        wineSurferList.appendChild(item);
                    });
                }
            }

            function closeWineSurferPopover({ restoreFocus = true } = {}) {
                cancelWineSurferRequest();
                wineSurferLoading = false;
                wineSurferError = '';
                wineSurferResults = [];
                wineSurferActiveQuery = '';
                wineSurferSelectionPending = false;
                wineSurferStreamChunkCount = 0;

                if (wineSurferOverlay) {
                    wineSurferOverlay.classList.remove('is-open');
                    wineSurferOverlay.setAttribute('aria-hidden', 'true');
                    wineSurferOverlay.hidden = true;
                }

                renderWineSurferResults();

                if (restoreFocus && addWineSearch && (!wineSurferOverlay || wineSurferOverlay.hidden)) {
                    addWineSearch.focus();
                }
            }

            async function openWineSurferPopover(query) {
                if (!wineSurferOverlay || !wineSurferPopover) {
                    return;
                }

                const trimmedQuery = (query ?? '').trim();
                if (!trimmedQuery) {
                    showAddWineError('Enter a wine name before using Wine Surfer.');
                    return;
                }

                wineSurferOverlay.hidden = false;
                wineSurferOverlay.setAttribute('aria-hidden', 'false');
                wineSurferOverlay.classList.add('is-open');

                cancelWineSurferRequest();
                wineSurferLoading = true;
                wineSurferError = '';
                wineSurferResults = [];
                wineSurferActiveQuery = trimmedQuery;
                wineSurferStreamChunkCount = 0;
                renderWineSurferResults();

                const controller = new AbortController();
                wineSurferController = controller;

                try {
                    const streamSupported = supportsReadableStream();
                    if (streamSupported) {
                        await streamWineSurferMatches(trimmedQuery, controller);
                    } else {
                        await fetchWineSurferJson(trimmedQuery, controller);
                    }

                    if (wineSurferController !== controller) {
                        return;
                    }
                } catch (error) {
                    if (controller.signal.aborted) {
                        return;
                    }

                    wineSurferResults = [];
                    wineSurferError = error?.message ?? 'Wine Surfer could not search right now.';
                } finally {
                    if (wineSurferController === controller) {
                        wineSurferController = null;
                    }

                    wineSurferLoading = false;
                    renderWineSurferResults();

                    if (wineSurferClose) {
                        wineSurferClose.focus();
                    }
                }
            }

            function openCreateWinePopover(query) {
                const module = window.WineCreatePopover;
                if (!module || typeof module.open !== 'function') {
                    showAddWineError('Unable to open the create wine dialog right now.');
                    return;
                }

                const initialName = (query ?? '').trim()
                    || currentWineQuery
                    || addWineSearch?.value
                    || '';

                module.open({
                    initialName,
                    parentDialog: addWinePopover ?? null,
                    triggerElement: addWineSearch ?? null,
                    onSuccess: (response) => {
                        const option = normalizeWineOption(response);
                        if (!option) {
                            showAddWineError('Wine was created, but it could not be selected automatically.');
                            return;
                        }

                        wineOptions = appendActionOptions([option], option.name, { includeCreate: false });
                        lastCompletedQuery = option.name;
                        setSelectedWine(option);
                        showAddWineError('');
                        closeWineResults();
                        if (addWineSearch) {
                            addWineSearch.focus();
                        }
                    },
                    onCancel: () => {
                        if (addWineSearch) {
                            addWineSearch.focus();
                        }
                    }
                });
            }

            async function fetchWineSurferJson(query, controller) {
                const response = await sendJson(`/wine-manager/wine-surfer?query=${encodeURIComponent(query)}`, {
                    method: 'GET',
                    signal: controller.signal
                });

                if (wineSurferController !== controller) {
                    return;
                }

                const items = Array.isArray(response?.wines)
                    ? response.wines
                    : [];
                wineSurferResults = items
                    .map(normalizeWineSurferResult)
                    .filter(Boolean);
                wineSurferError = '';
            }

            async function streamWineSurferMatches(query, controller) {
                const response = await fetch(`/wine-manager/wine-surfer?query=${encodeURIComponent(query)}`, {
                    method: 'GET',
                    signal: controller.signal,
                    credentials: 'same-origin',
                    headers: {
                        Accept: 'application/x-ndjson'
                    }
                });

                if (!response.ok) {
                    const message = await readWineSurferResponseError(response);
                    throw new Error(message);
                }

                if (!response.body || typeof response.body.getReader !== 'function') {
                    const payload = await response.json();
                    if (wineSurferController !== controller) {
                        return;
                    }

                    const items = Array.isArray(payload?.wines)
                        ? payload.wines
                        : [];
                    wineSurferResults = items
                        .map(normalizeWineSurferResult)
                        .filter(Boolean);
                    wineSurferError = '';
                    return;
                }

                const reader = response.body.getReader();
                const decoder = new TextDecoder();
                let buffer = '';
                let aggregated = '';
                let encounteredError = '';
                let hasReceivedMatches = false;

                try {
                    while (true) {
                        const { value, done } = await reader.read();
                        if (done) {
                            break;
                        }

                        if (!value) {
                            continue;
                        }

                        buffer += decoder.decode(value, { stream: true });

                        let newlineIndex = buffer.indexOf('\n');
                        while (newlineIndex >= 0) {
                            const line = buffer.slice(0, newlineIndex).trim();
                            buffer = buffer.slice(newlineIndex + 1);

                            if (line) {
                                let eventPayload = null;
                                try {
                                    eventPayload = JSON.parse(line);
                                } catch {
                                    eventPayload = null;
                                }

                                if (eventPayload) {
                                    const eventType = typeof eventPayload.type === 'string'
                                        ? eventPayload.type
                                        : '';

                                    if (eventType === 'delta') {
                                        const deltaContent = typeof eventPayload.content === 'string'
                                            ? eventPayload.content
                                            : '';
                                        if (deltaContent) {
                                            aggregated += deltaContent;
                                            wineSurferStreamChunkCount += 1;
                                        }
                                    } else if (eventType === 'matches') {
                                        const wines = Array.isArray(eventPayload.wines)
                                            ? eventPayload.wines
                                            : [];
                                        wineSurferResults = wines
                                            .map(normalizeWineSurferResult)
                                            .filter(Boolean);
                                        wineSurferError = '';
                                        hasReceivedMatches = true;
                                    } else if (eventType === 'error') {
                                        encounteredError = typeof eventPayload.message === 'string'
                                            ? eventPayload.message
                                            : 'Wine Surfer could not search right now.';
                                    } else if (eventType === 'complete') {
                                        const wines = Array.isArray(eventPayload.wines)
                                            ? eventPayload.wines
                                            : null;
                                        if (wines) {
                                            wineSurferResults = wines
                                                .map(normalizeWineSurferResult)
                                                .filter(Boolean);
                                            wineSurferError = '';
                                            hasReceivedMatches = true;
                                        }
                                    }
                                }
                            }

                            renderWineSurferResults();

                            if (encounteredError || controller.signal.aborted) {
                                break;
                            }

                            newlineIndex = buffer.indexOf('\n');
                        }

                        if (encounteredError || controller.signal.aborted) {
                            break;
                        }
                    }

                    aggregated += decoder.decode();
                } catch (error) {
                    if (!controller.signal.aborted) {
                        throw error;
                    }
                } finally {
                    try {
                        reader.releaseLock();
                    } catch {
                        /* ignore */
                    }
                }

                if (controller.signal.aborted) {
                    return;
                }

                if (encounteredError) {
                    wineSurferResults = [];
                    wineSurferError = encounteredError;
                    renderWineSurferResults();
                    return;
                }

                if (!hasReceivedMatches && aggregated) {
                    try {
                        const parsed = JSON.parse(aggregated);
                        const wines = Array.isArray(parsed?.wines)
                            ? parsed.wines
                            : [];
                        wineSurferResults = wines
                            .map(normalizeWineSurferResult)
                            .filter(Boolean);
                        wineSurferError = '';
                        hasReceivedMatches = true;
                    } catch {
                        /* ignore parse errors for partial content */
                    }
                }

                if (!hasReceivedMatches && !wineSurferError) {
                    wineSurferResults = [];
                }

                renderWineSurferResults();
            }

            function supportsReadableStream() {
                return typeof ReadableStream !== 'undefined'
                    && typeof Response !== 'undefined'
                    && Response.prototype
                    && 'body' in Response.prototype;
            }

            async function readWineSurferResponseError(response) {
                let message = `${response.status} ${response.statusText}`;
                try {
                    const problem = await response.json();
                    if (typeof problem === 'string') {
                        message = problem;
                    } else if (problem?.title) {
                        message = problem.title;
                    } else if (problem?.message) {
                        message = problem.message;
                    }
                } catch {
                    try {
                        const text = await response.text();
                        if (text) {
                            message = text;
                        }
                    } catch {
                        /* ignore */
                    }
                }

                return message;
            }

            function cancelWineSearchTimeout() {
                if (wineSearchTimeoutId !== null) {
                    window.clearTimeout(wineSearchTimeoutId);
                    wineSearchTimeoutId = null;
                }
            }

            function abortWineSearchRequest() {
                if (wineSearchController) {
                    wineSearchController.abort();
                    wineSearchController = null;
                }
            }

            function resetWineSelection() {
                cancelWineSearchTimeout();
                abortWineSearchRequest();
                wineOptions = [];
                selectedWineOption = null;
                wineSearchLoading = false;
                wineSearchError = '';
                currentWineQuery = '';
                activeWineOptionIndex = -1;
                lastCompletedQuery = '';
                if (addWineHiddenInput) {
                    addWineHiddenInput.value = '';
                }
                if (addWineSearch) {
                    addWineSearch.value = '';
                    addWineSearch.setAttribute('aria-expanded', 'false');
                    addWineSearch.removeAttribute('aria-activedescendant');
                }
                if (addWineResults) {
                    addWineResults.innerHTML = '';
                    addWineResults.dataset.visible = 'false';
                    addWineResults.setAttribute('hidden', '');
                }
                updateSelectedWineSummary(null);
                updateVintageHint(null);
            }

            function scheduleWineSearch(query) {
                cancelWineSearchTimeout();
                wineSearchTimeoutId = window.setTimeout(() => {
                    performWineSearch(query).catch(() => {
                        /* handled via state updates */
                    });
                }, 500);
            }

            async function performWineSearch(query) {
                cancelWineSearchTimeout();
                if (!query || query.length < 3) {
                    return;
                }

                abortWineSearchRequest();
                const controller = new AbortController();
                wineSearchController = controller;
                wineSearchLoading = true;
                wineSearchError = '';
                renderWineSearchResults();

                try {
                    const response = await sendJson(`/wine-manager/wine-surfer?query=${encodeURIComponent(query)}`, {
                        method: 'GET',
                        signal: controller.signal
                    });
                    if (wineSearchController !== controller) {
                        return;
                    }

                    const wines = Array.isArray(response?.wines) ? response.wines : [];
                    const normalized = wines
                        .map(normalizeWineOption)
                        .filter(Boolean);
                    wineOptions = appendActionOptions(normalized, query);
                    lastCompletedQuery = query;
                    wineSearchLoading = false;
                    wineSearchError = '';
                    renderWineSearchResults();
                } catch (error) {
                    if (controller.signal.aborted) {
                        return;
                    }

                    wineOptions = appendActionOptions([], query);
                    wineSearchLoading = false;
                    wineSearchError = error?.message ?? 'Unable to search for wines.';
                    lastCompletedQuery = query;
                    renderWineSearchResults();
                } finally {
                    if (wineSearchController === controller) {
                        wineSearchController = null;
                    }
                }
            }

            function renderWineSearchResults() {
                if (!addWineResults) {
                    return;
                }

                const trimmedQuery = currentWineQuery.trim();
                const shouldDisplay = trimmedQuery.length >= 3
                    || wineSearchLoading
                    || wineSearchError
                    || wineSearchTimeoutId !== null;

                if (!shouldDisplay) {
                    closeWineResults();
                    return;
                }

                setWineResultsVisibility(true);
                addWineResults.innerHTML = '';

                if (wineSearchTimeoutId !== null || wineSearchLoading) {
                    addWineResults.appendChild(buildWineStatusElement('Searching…'));
                    return;
                }

                const hasCreateOption = wineOptions.some(option => option?.isCreateWine);
                const cellarOptions = wineOptions.filter(option => option && !option.isWineSurfer && !option.isCreateWine);
                const hasCellarOptions = cellarOptions.length > 0;

                if (wineSearchError) {
                    const errorStatus = buildWineStatusElement(wineSearchError);
                    errorStatus.classList.add('inventory-add-wine-result-status--error');
                    addWineResults.appendChild(errorStatus);
                } else if (!hasCellarOptions) {
                    if (trimmedQuery === lastCompletedQuery) {
                        const createMessage = hasCreateOption && trimmedQuery.length > 0
                            ? `No wines found in your cellar. Create “${trimmedQuery}”.`
                            : 'No wines found in your cellar. Create a new wine.';
                        const message = hasCreateOption ? createMessage : 'No wines found.';
                        addWineResults.appendChild(buildWineStatusElement(message));
                    } else {
                        addWineResults.appendChild(buildWineStatusElement('Keep typing to search the cellar.'));
                    }
                } else if (hasCreateOption && trimmedQuery === lastCompletedQuery) {
                    const message = trimmedQuery.length > 0
                        ? `Select a wine below or create “${trimmedQuery}” if it's missing from your cellar.`
                        : 'Select a wine below or create a new entry if it is missing from your cellar.';
                    addWineResults.appendChild(buildWineStatusElement(message));
                }

                if (wineOptions.length === 0) {
                    return;
                }

                const list = document.createElement('div');
                list.className = 'inventory-add-wine-options';

                wineOptions.forEach((option, index) => {
                    if (!option) {
                        return;
                    }

                    const element = document.createElement('button');
                    element.type = 'button';
                    element.className = 'inventory-add-wine-option';
                    element.setAttribute('role', 'option');
                    element.dataset.index = String(index);

                    if (!option.isWineSurfer && !option.isCreateWine && option.id) {
                        element.dataset.wineId = option.id;
                    }

                    if (option.isWineSurfer || option.isCreateWine) {
                        element.classList.add('inventory-add-wine-option--action');
                        if (option.isCreateWine) {
                            element.classList.add('create-wine-option--action');
                        }
                    }

                    const optionId = option.isWineSurfer
                        ? 'inventory-add-wine-option-wine-surfer'
                        : option.isCreateWine
                            ? 'inventory-add-wine-option-create'
                            : `inventory-add-wine-option-${option.id}`;
                    element.id = optionId;

                    const nameSpan = document.createElement('span');
                    nameSpan.className = 'inventory-add-wine-option__name';
                    nameSpan.textContent = option.label
                        ?? option.name
                        ?? option.id
                        ?? 'Wine option';
                    element.appendChild(nameSpan);

                    if (option.isCreateWine) {
                        const actionMeta = document.createElement('span');
                        actionMeta.className = 'inventory-add-wine-option__meta';
                        actionMeta.textContent = 'Create a new wine with custom details.';
                        element.appendChild(actionMeta);
                    } else {
                        const metaParts = [];
                        if (option.color) {
                            metaParts.push(option.color);
                        }

                        const regionParts = [];
                        if (option.subAppellation) {
                            regionParts.push(option.subAppellation);
                        }
                        if (option.appellation && !regionParts.includes(option.appellation)) {
                            regionParts.push(option.appellation);
                        }
                        if (option.region) {
                            regionParts.push(option.region);
                        }
                        if (option.country) {
                            regionParts.push(option.country);
                        }

                        if (regionParts.length > 0) {
                            metaParts.push(regionParts.join(' • '));
                        }

                        if (Array.isArray(option.vintages) && option.vintages.length > 0) {
                            const vintages = option.vintages.slice(0, 3);
                            const suffix = option.vintages.length > vintages.length ? '…' : '';
                            metaParts.push(`Vintages: ${vintages.join(', ')}${suffix}`);
                        }

                        if (metaParts.length > 0) {
                            const metaSpan = document.createElement('span');
                            metaSpan.className = 'inventory-add-wine-option__meta';
                            metaSpan.textContent = metaParts.join(' · ');
                            element.appendChild(metaSpan);
                        }
                    }

                    element.addEventListener('mousedown', (event) => {
                        event.preventDefault();
                    });

                    element.addEventListener('click', () => {
                        selectWineOption(option);
                    });

                    list.appendChild(element);
                });

                addWineResults.appendChild(list);
                highlightActiveWineOption();
            }

            async function handleWineSurferSelection(result) {
                if (!result || wineSurferSelectionPending) {
                    return;
                }

                const payload = {
                    name: result.name,
                    country: result.country ?? null,
                    region: result.region ?? null,
                    appellation: result.appellation ?? null,
                    subAppellation: result.subAppellation ?? null,
                    color: result.color ?? null
                };

                // Validate required fields for unified endpoint
                if (!payload.color || !payload.region || !payload.appellation) {
                    wineSurferError = 'Wine requires color, region, and appellation. Please refine your selection.';
                    renderWineSurferResults();
                    return;
                }

                wineSurferSelectionPending = true;
                wineSurferError = '';
                renderWineSurferResults();

                try {
                    const response = await sendJson('/wine-manager/wine-surfer/wines', {
                        method: 'POST',
                        body: JSON.stringify(payload)
                    });

                    const option = normalizeWineOption(response);
                    if (!option) {
                        throw new Error('Wine Surfer returned an unexpected response.');
                    }

                    wineOptions = appendActionOptions([option], option.name);
                    setSelectedWine(option);
                    showAddWineError('');
                    closeWineSurferPopover({ restoreFocus: false });
                    if (addWineSearch) {
                        addWineSearch.focus();
                    }
                } catch (error) {
                    wineSurferError = error?.message ?? 'Wine Surfer could not add that wine right now.';
                } finally {
                    wineSurferSelectionPending = false;
                    renderWineSurferResults();
                }
            }

            function buildWineStatusElement(text) {
                const status = document.createElement('div');
                status.className = 'inventory-add-wine-result-status';
                status.textContent = text;
                status.setAttribute('role', 'status');
                return status;
            }

            function setWineResultsVisibility(visible) {
                if (!addWineResults || !addWineSearch) {
                    return;
                }

                if (visible) {
                    addWineResults.dataset.visible = 'true';
                    addWineResults.removeAttribute('hidden');
                    addWineSearch.setAttribute('aria-expanded', 'true');
                } else {
                    addWineResults.dataset.visible = 'false';
                    addWineResults.setAttribute('hidden', '');
                    addWineSearch.setAttribute('aria-expanded', 'false');
                    addWineSearch.removeAttribute('aria-activedescendant');
                }
            }

            function closeWineResults() {
                activeWineOptionIndex = -1;
                setWineResultsVisibility(false);
                if (addWineResults) {
                    addWineResults.innerHTML = '';
                }
            }

            function highlightActiveWineOption() {
                if (!addWineResults || !addWineSearch) {
                    return;
                }

                const options = Array.from(addWineResults.querySelectorAll('.inventory-add-wine-option'));
                options.forEach((element, index) => {
                    const isActive = index === activeWineOptionIndex;
                    element.classList.toggle('is-active', isActive);
                    element.setAttribute('aria-selected', isActive ? 'true' : 'false');
                });

                const activeElement = options[activeWineOptionIndex];
                if (activeElement) {
                    addWineSearch.setAttribute('aria-activedescendant', activeElement.id);
                    activeElement.scrollIntoView({ block: 'nearest' });
                } else {
                    addWineSearch.removeAttribute('aria-activedescendant');
                }
            }

            function moveWineResultFocus(step) {
                if (!Array.isArray(wineOptions) || wineOptions.length === 0) {
                    return;
                }

                const maxIndex = wineOptions.length - 1;
                if (maxIndex < 0) {
                    return;
                }

                if (addWineResults?.dataset?.visible !== 'true') {
                    renderWineSearchResults();
                }

                if (activeWineOptionIndex < 0) {
                    activeWineOptionIndex = step > 0 ? 0 : maxIndex;
                } else {
                    activeWineOptionIndex += step;
                    if (activeWineOptionIndex > maxIndex) {
                        activeWineOptionIndex = 0;
                    } else if (activeWineOptionIndex < 0) {
                        activeWineOptionIndex = maxIndex;
                    }
                }

                highlightActiveWineOption();
            }

            function selectWineOption(option) {
                if (!option) {
                    return;
                }

                if (option.isWineSurfer) {
                    closeWineResults();
                    const queryValue = (option.query ?? currentWineQuery ?? addWineSearch?.value ?? '').trim();
                    openWineSurferPopover(queryValue);
                    return;
                }

                if (option.isCreateWine) {
                    closeWineResults();
                    const queryValue = (option.query ?? currentWineQuery ?? addWineSearch?.value ?? '').trim();
                    openCreateWinePopover(queryValue);
                    return;
                }

                setSelectedWine(option);
                showAddWineError('');
                closeWineResults();
            }

            function setSelectedWine(option, { preserveSearchValue = false } = {}) {
                const isActionOption = option?.isWineSurfer === true || option?.isCreateWine === true;
                selectedWineOption = isActionOption ? null : option ?? null;
                if (addWineHiddenInput) {
                    addWineHiddenInput.value = selectedWineOption?.id ?? '';
                }
                if (addWineSearch) {
                    if (selectedWineOption) {
                        const label = selectedWineOption?.label ?? selectedWineOption?.name ?? '';
                        addWineSearch.value = label;
                        currentWineQuery = label.trim();
                    } else if (preserveSearchValue) {
                        currentWineQuery = addWineSearch.value.trim();
                    } else {
                        addWineSearch.value = '';
                        currentWineQuery = '';
                    }
                }
                updateSelectedWineSummary(selectedWineOption);
                updateVintageHint(selectedWineOption);
            }

            function valueMatchesSelected(value) {
                if (!selectedWineOption) {
                    return false;
                }

                const selectedLabel = selectedWineOption.label ?? selectedWineOption.name ?? '';
                return value.trim().toLowerCase() === selectedLabel.trim().toLowerCase();
            }

            function handleWineSearchInput(event) {
                const value = event?.target?.value ?? '';
                currentWineQuery = value.trim();

                if (!valueMatchesSelected(value)) {
                    setSelectedWine(null, { preserveSearchValue: true });
                }

                wineSearchError = '';

                if (currentWineQuery.length < 3) {
                    wineOptions = [];
                    wineSearchLoading = false;
                    lastCompletedQuery = '';
                    cancelWineSearchTimeout();
                    abortWineSearchRequest();
                    renderWineSearchResults();
                    return;
                }

                scheduleWineSearch(currentWineQuery);
                renderWineSearchResults();
            }

            function handleWineSearchKeyDown(event) {
                if (event.key === 'ArrowDown') {
                    event.preventDefault();
                    moveWineResultFocus(1);
                    return;
                }

                if (event.key === 'ArrowUp') {
                    event.preventDefault();
                    moveWineResultFocus(-1);
                    return;
                }

                if (event.key === 'Enter') {
                    if (activeWineOptionIndex >= 0 && wineOptions[activeWineOptionIndex]) {
                        event.preventDefault();
                        selectWineOption(wineOptions[activeWineOptionIndex]);
                    }
                    return;
                }

                if (event.key === 'Escape') {
                    if (addWineResults?.dataset?.visible === 'true') {
                        event.preventDefault();
                        closeWineResults();
                        return;
                    }

                    if (addWineSearch?.value) {
                        event.preventDefault();
                        addWineSearch.value = '';
                        handleWineSearchInput({ target: addWineSearch });
                    }
                }
            }

            function handleWineSearchFocus() {
                if (!addWineResults) {
                    return;
                }

                if (wineOptions.length > 0 || wineSearchError || wineSearchLoading || wineSearchTimeoutId !== null) {
                    renderWineSearchResults();
                }
            }

            function handleWinePointerDown(event) {
                if (!addWineOverlay || addWineOverlay.hidden) {
                    return;
                }

                if (!addWineCombobox) {
                    return;
                }

                if (!addWineCombobox.contains(event.target)) {
                    closeWineResults();
                }
            }

            function updateSelectedWineSummary(option = selectedWineOption) {
                if (!addWineSummary) {
                    return;
                }

                const target = option ?? null;
                if (!target) {
                    addWineSummary.textContent = 'Search for a wine to see its appellation and color.';
                    return;
                }

                const parts = [];
                if (target.color) {
                    parts.push(target.color);
                }

                const regionParts = [];
                if (target.subAppellation) {
                    regionParts.push(target.subAppellation);
                }
                if (target.appellation && !regionParts.includes(target.appellation)) {
                    regionParts.push(target.appellation);
                }
                if (target.region) {
                    regionParts.push(target.region);
                }
                if (target.country) {
                    regionParts.push(target.country);
                }

                if (regionParts.length > 0) {
                    parts.push(regionParts.join(' • '));
                }

                addWineSummary.textContent = parts.length > 0
                    ? parts.join(' · ')
                    : 'No additional details available.';
            }

            function updateVintageHint(option = selectedWineOption) {
                if (!addWineHint) {
                    return;
                }

                const target = option ?? null;
                if (!target) {
                    addWineHint.textContent = 'Search for a wine to view existing vintages.';
                    return;
                }

                if (!Array.isArray(target.vintages) || target.vintages.length === 0) {
                    addWineHint.textContent = 'No bottles recorded yet for this wine. Enter any vintage to begin.';
                    return;
                }

                const vintages = target.vintages.slice(0, 6);
                const suffix = target.vintages.length > vintages.length ? '…' : '';
                addWineHint.textContent = `Existing vintages: ${vintages.join(', ')}${suffix}`;
            }

            async function handleInventoryAddition(response, quantity = 1) {
                const summary = normalizeSummary(response?.group ?? response?.Group);
                if (!summary) {
                    showMessage('Bottle added, but the inventory view could not be refreshed.', 'warning');
                    return;
                }

                const tbody = inventoryTable.querySelector('tbody');
                if (!tbody) {
                    return;
                }

                const wineId = summary.wineId;
                if (!wineId) {
                    showMessage('Bottle added, but the wine identifier was missing.', 'warning');
                    return;
                }

                let row = inventoryTable.querySelector(`tr[data-wine-id="${wineId}"]`);
                if (!row) {
                    // Create a placeholder wine-level row; it will be refreshed after loading wine details
                    row = document.createElement('tr');
                    row.className = 'group-row';
                    row.setAttribute('tabindex', '0');
                    row.setAttribute('role', 'button');
                    row.setAttribute('aria-controls', 'details-table');
                    ensureSummaryRowStructure(row);
                    row.dataset.wineId = wineId;
                    row.dataset.subAppellationId = summary?.subAppellationId ?? '';
                    row.dataset.appellationId = summary?.appellationId ?? '';
                    row.dataset.summaryWine = summary?.wineName ?? '';
                    row.dataset.summaryAppellation = buildAppellationDisplay(summary);
                    row.dataset.summaryVintage = '';
                    row.dataset.summaryColor = summary?.color ?? '';
                    row.dataset.summaryStatus = summary?.statusLabel ?? '';

                    const wineCell = row.querySelector('.summary-wine');
                    const appCell = row.querySelector('.summary-appellation');
                    const vintageCell = row.querySelector('.summary-vintage');
                    const bottlesCell = row.querySelector('.summary-bottles');
                    const colorCell = row.querySelector('.summary-color');
                    const statusSpan = row.querySelector('[data-field="status"]');
                    const scoreCell = row.querySelector('[data-field="score"]');

                    if (wineCell) wineCell.textContent = summary?.wineName ?? '';
                    if (appCell) appCell.textContent = buildAppellationDisplay(summary);
                    if (vintageCell) vintageCell.textContent = '—';
                    if (bottlesCell) bottlesCell.textContent = '0';
                    if (colorCell) colorCell.textContent = summary?.color ?? '';
                    if (statusSpan) {
                        statusSpan.textContent = summary?.statusLabel ?? '';
                        const cssClass = summary?.statusCssClass ?? '';
                        statusSpan.className = `status-pill ${cssClass}`;
                    }
                    if (scoreCell) scoreCell.textContent = '—';

                    attachSummaryRowHandlers(row);
                    tbody.appendChild(row);
                }

                ensureRowInitialIndex(row);
                refreshGrouping({ expandForRow: row });

                const addedCount = Number.isInteger(quantity) && quantity > 0 ? quantity : 1;
                const message = addedCount === 1
                    ? 'Bottle added to your inventory.'
                    : `${addedCount} bottles added to your inventory.`;
                showMessage(message, 'success');

                // Select the wine row and load wine-level details, which will also refresh the row summary
                await handleRowSelection(row, { force: true });
                row.focus();
            }

            function showAddWineError(message) {
                if (!addWineError) {
                    return;
                }

                const text = message ?? '';
                addWineError.textContent = text;
                addWineError.setAttribute('aria-hidden', text ? 'false' : 'true');
            }

            function setModalLoading(state) {
                modalLoading = state;
                if (addWineSubmit) {
                    addWineSubmit.disabled = state;
                }
                if (addWineSearch) {
                    addWineSearch.disabled = state;
                    if (state) {
                        addWineSearch.setAttribute('aria-busy', 'true');
                        cancelWineSearchTimeout();
                        abortWineSearchRequest();
                        closeWineResults();
                    } else {
                        addWineSearch.removeAttribute('aria-busy');
                    }
                }
                if (addWineVintage) {
                    addWineVintage.disabled = state;
                }
                if (addWineQuantity) {
                    addWineQuantity.disabled = state;
                }
                if (addWineLocation) {
                    addWineLocation.disabled = state;
                }
            }

            function bindDetailAddRow() {
                if (!detailAddRow || !detailAddButton) {
                    return;
                }

                detailAddButton.addEventListener('click', handleAddBottle);
            }

            function initializeDrinkScoreControl() {
                if (!drinkScoreInput || !drinkScoreIsRange) {
                    if (drinkScoreClearButton) {
                        drinkScoreClearButton.disabled = true;
                    }
                    return;
                }

                setDrinkScoreValue('');

                drinkScoreInput.addEventListener('input', () => {
                    drinkScoreInput.dataset.hasValue = 'true';
                    updateDrinkScoreDisplay();
                });

                drinkScoreClearButton?.addEventListener('click', (event) => {
                    event.preventDefault();
                    setDrinkScoreValue('');
                    drinkScoreInput.focus();
                });

                updateDrinkScoreDisplay();
            }

            function setDrinkScoreValue(value) {
                if (!drinkScoreInput) {
                    return;
                }

                if (!drinkScoreIsRange) {
                    drinkScoreInput.value = value != null && value !== '' ? String(value) : '';
                    return;
                }

                const hasValue = value != null && value !== '';
                drinkScoreInput.dataset.hasValue = hasValue ? 'true' : 'false';

                const fallback = drinkScoreDefaultValue ?? drinkScoreInput.min ?? '0';
                drinkScoreInput.value = hasValue ? String(value) : fallback;

                updateDrinkScoreDisplay();
            }

            function getDrinkScoreRawValue() {
                if (!drinkScoreInput) {
                    return '';
                }

                if (!drinkScoreIsRange) {
                    return drinkScoreInput.value ?? '';
                }

                return drinkScoreInput.dataset.hasValue === 'true'
                    ? (drinkScoreInput.value ?? '')
                    : '';
            }

            function updateDrinkScoreDisplay() {
                if (!drinkScoreDisplay) {
                    return;
                }

                if (!drinkScoreInput || drinkScoreInput.dataset.hasValue !== 'true') {
                    drinkScoreDisplay.textContent = 'Not rated';
                    drinkScoreInput?.setAttribute('aria-valuetext', 'Not rated');
                    return;
                }

                const numeric = Number.parseFloat(drinkScoreInput.value ?? '');
                if (Number.isFinite(numeric)) {
                    const formatted = numeric.toFixed(1);
                    drinkScoreDisplay.textContent = `${formatted} / 10`;
                    drinkScoreInput.setAttribute('aria-valuetext', `${formatted} out of 10`);
                } else {
                    drinkScoreDisplay.textContent = 'Not rated';
                    drinkScoreInput.setAttribute('aria-valuetext', 'Not rated');
                }
            }

            function bindDrinkBottleModal() {
                if (!drinkOverlay || !drinkPopover || !drinkForm) {
                    return;
                }

                closeDrinkBottleModal();

                drinkForm.addEventListener('submit', handleDrinkBottleSubmit);
                drinkCancelButton?.addEventListener('click', () => {
                    closeDrinkBottleModal();
                });

                drinkHeaderCloseButton?.addEventListener('click', () => {
                    closeDrinkBottleModal();
                });

                drinkOverlay.addEventListener('click', (event) => {
                    if (event.target === drinkOverlay) {
                        closeDrinkBottleModal();
                    }
                });

                document.addEventListener('keydown', (event) => {
                    if (event.key === 'Escape' && drinkOverlay && !drinkOverlay.hidden) {
                        closeDrinkBottleModal();
                    }
                });
            }

            function bindNotesPanel() {
                if (!notesEnabled) {
                    return;
                }

                initializeNotesPanel();
                notesCloseButton?.addEventListener('click', closeNotesPanel);
                notesAddButton?.addEventListener('click', handleAddNote);
            }

            function openDrinkBottleModal(detail, summary) {
                if (!drinkOverlay || !drinkPopover) {
                    return;
                }

                closeAddWinePopover();
                closeWineSurferPopover({ restoreFocus: false });

                const bottleId = detail?.bottleId ?? detail?.BottleId ?? '';
                if (!bottleId) {
                    showMessage('Unable to determine the selected bottle.', 'error');
                    return;
                }

                const wineName = summary?.wineName ?? summary?.WineName ?? selectedSummary?.wineName ?? '';
                const vintage = summary?.vintage ?? summary?.Vintage ?? selectedSummary?.vintage ?? '';
                const rawDrunkAt = detail?.drunkAt ?? detail?.DrunkAt ?? null;
                const normalizedDrunkAt = normalizeIsoDate(rawDrunkAt);
                const existingNoteIdValue = detail?.currentUserNoteId ?? detail?.CurrentUserNoteId ?? null;
                const existingNoteTextValue = detail?.currentUserNote ?? detail?.CurrentUserNote ?? '';
                const existingScoreValue = detail?.currentUserScore ?? detail?.CurrentUserScore ?? null;
                const normalizedNoteId = existingNoteIdValue ? String(existingNoteIdValue) : null;
                const normalizedNoteText = typeof existingNoteTextValue === 'string'
                    ? existingNoteTextValue
                    : existingNoteTextValue != null
                        ? String(existingNoteTextValue)
                        : '';
                const normalizedScore = (() => {
                    if (existingScoreValue == null || existingScoreValue === '') {
                        return null;
                    }

                    const parsed = Number(existingScoreValue);
                    return Number.isFinite(parsed) ? parsed : null;
                })();

                drinkTarget = {
                    bottleId: String(bottleId),
                    userId: detail?.userId ? String(detail.userId) : detail?.UserId ? String(detail.UserId) : null,
                    drunkAt: normalizedDrunkAt || null,
                    isDrunk: Boolean(detail?.isDrunk ?? detail?.IsDrunk),
                    noteId: normalizedNoteId
                };

                if (drinkTitle) {
                    if (wineName) {
                        drinkTitle.textContent = vintage ? `Drink ${wineName} • ${vintage}` : `Drink ${wineName}`;
                    } else {
                        drinkTitle.textContent = 'Drink Bottle';
                    }
                }

                const defaultDate = normalizedDrunkAt
                    ? formatDateInputValue(normalizedDrunkAt)
                    : formatDateInputValue(new Date());

                if (drinkDateInput) {
                    drinkDateInput.value = defaultDate;
                }
                setDrinkScoreValue(normalizedScore != null ? normalizedScore : '');
                if (drinkNoteInput) {
                    drinkNoteInput.value = normalizedNoteText;
                }

                showDrinkError('');
                setDrinkModalLoading(false);

                drinkOverlay.hidden = false;
                drinkOverlay.setAttribute('aria-hidden', 'false');
                drinkOverlay.classList.add('is-open');
                document.body.style.overflow = 'hidden';

                (drinkDateInput ?? drinkNoteInput)?.focus();
            }

            function closeDrinkBottleModal() {
                if (!drinkOverlay) {
                    return;
                }

                drinkTarget = null;
                setDrinkModalLoading(false);
                drinkOverlay.classList.remove('is-open');
                drinkOverlay.setAttribute('aria-hidden', 'true');
                drinkOverlay.hidden = true;
                document.body.style.overflow = '';

                showDrinkError('');

                if (drinkDateInput) {
                    drinkDateInput.value = '';
                }
                setDrinkScoreValue('');
                if (drinkNoteInput) {
                    drinkNoteInput.value = '';
                }
                if (drinkCancelButton) {
                    drinkCancelButton.disabled = false;
                }
                if (drinkHeaderCloseButton) {
                    drinkHeaderCloseButton.disabled = false;
                }
            }

            async function handleDrinkBottleSubmit(event) {
                event.preventDefault();

                if (loading || drinkModalLoading) {
                    return;
                }

                if (!drinkTarget) {
                    showDrinkError('Select a bottle to drink.');
                    return;
                }

                if (!selectedSummary) {
                    showDrinkError('Select a wine group to drink from.');
                    return;
                }

                const bottleId = drinkTarget.bottleId;
                if (!bottleId) {
                    showDrinkError('Unable to determine the selected bottle.');
                    return;
                }

                const dateValue = drinkDateInput?.value ?? '';
                if (!dateValue) {
                    showDrinkError('Choose when you drank this bottle.');
                    drinkDateInput?.focus();
                    return;
                }

                const noteValue = drinkNoteInput?.value?.trim() ?? '';
                const scoreRawValue = getDrinkScoreRawValue();
                const parsedScore = parseScore(scoreRawValue);
                if (parsedScore === undefined) {
                    showDrinkError('Score must be between 0 and 10.');
                    drinkScoreInput?.focus();
                    return;
                }

                if (!noteValue && parsedScore == null) {
                    showDrinkError('Add a tasting note or score.');
                    (drinkNoteInput ?? drinkScoreInput)?.focus();
                    return;
                }

                const drunkAt = parseDateOnly(dateValue);
                if (!drunkAt) {
                    showDrinkError('Choose a valid drinking date.');
                    drinkDateInput?.focus();
                    return;
                }

                const row = detailsBody.querySelector(`.detail-row[data-bottle-id="${bottleId}"]`);
                const priceValue = row?.querySelector('.detail-price')?.value ?? '';
                const locationValue = row?.querySelector('.detail-location')?.value ?? '';
                const rowUserId = row?.dataset?.userId ? row.dataset.userId : '';
                const payloadUserId = rowUserId
                    || (drinkTarget.userId ? String(drinkTarget.userId) : null);

                const payload = {
                    wineVintageId: selectedSummary.wineVintageId,
                    price: parsePrice(priceValue),
                    isDrunk: true,
                    drunkAt,
                    bottleLocationId: locationValue || null,
                    userId: payloadUserId
                };

                try {
                    setDrinkModalLoading(true);
                    setLoading(true);

                    const response = await sendJson(`/wine-manager/bottles/${bottleId}`, {
                        method: 'PUT',
                        body: JSON.stringify(payload)
                    });

                    await renderDetails(response, true);
                    showMessage('Bottle marked as drunk.', 'success');

                    const notePayload = {
                        note: noteValue,
                        score: parsedScore
                    };
                    let noteUrl = '/wine-manager/notes';
                    let noteMethod = 'POST';

                    if (drinkTarget.noteId) {
                        noteUrl = `/wine-manager/notes/${drinkTarget.noteId}`;
                        noteMethod = 'PUT';
                    } else {
                        notePayload.bottleId = bottleId;
                    }

                    try {
                        const notesResponse = await sendJson(noteUrl, {
                            method: noteMethod,
                            body: JSON.stringify(notePayload)
                        });

                        const summary = normalizeBottleNoteSummary(notesResponse?.bottle ?? notesResponse?.Bottle);
                        const ownerUserId = drinkTarget.userId
                            || summary?.userId
                            || summary?.UserId
                            || '';
                        const ownerNote = extractUserNoteFromNotesResponse(notesResponse, ownerUserId ? String(ownerUserId) : '');
                        const noteIdFromResponse = ownerNote?.id ?? ownerNote?.Id ?? drinkTarget.noteId ?? null;
                        const noteTextFromResponse = ownerNote?.note ?? ownerNote?.Note ?? noteValue;
                        const noteScoreFromResponse = (() => {
                            const value = ownerNote?.score ?? ownerNote?.Score;
                            if (value == null || value === '') {
                                return parsedScore;
                            }

                            const parsedOwnerScore = Number(value);
                            return Number.isFinite(parsedOwnerScore) ? parsedOwnerScore : parsedScore;
                        })();

                        updateDetailRowNote(bottleId, noteIdFromResponse, noteTextFromResponse, noteScoreFromResponse);

                        if (noteIdFromResponse) {
                            drinkTarget.noteId = String(noteIdFromResponse);
                        }

                        const currentBottleId = notesSelectedBottleId ?? '';
                        if (currentBottleId && currentBottleId === bottleId) {
                            renderNotes(notesResponse);
                            showNotesMessage('Note saved.', 'success');
                        } else if (summary) {
                            updateScoresFromNotesSummary(summary);
                        }

                        showMessage('Bottle marked as drunk and tasting note saved.', 'success');
                    } catch (noteError) {
                        showDrinkError(noteError?.message ?? String(noteError));
                        return;
                    }

                    closeDrinkBottleModal();
                } catch (error) {
                    showDrinkError(error?.message ?? String(error));
                } finally {
                    setDrinkModalLoading(false);
                    setLoading(false);
                }
            }

            function showDrinkError(message) {
                if (!drinkError) {
                    return;
                }

                const text = message ?? '';
                drinkError.textContent = text;
                drinkError.setAttribute('aria-hidden', text ? 'false' : 'true');
            }

            function setDrinkModalLoading(state) {
                drinkModalLoading = state;
                if (drinkSubmitButton) {
                    drinkSubmitButton.disabled = state;
                }
                if (drinkDateInput) {
                    drinkDateInput.disabled = state;
                }
                if (drinkScoreInput) {
                    drinkScoreInput.disabled = state;
                }
                if (drinkScoreClearButton) {
                    drinkScoreClearButton.disabled = state;
                }
                if (drinkNoteInput) {
                    drinkNoteInput.disabled = state;
                }
                if (drinkCancelButton) {
                    drinkCancelButton.disabled = state;
                }
                if (drinkHeaderCloseButton) {
                    drinkHeaderCloseButton.disabled = state;
                }
            }

            function initializeNotesPanel() {
                if (notesAddUserDisplay) {
                    notesAddUserDisplay.textContent = '—';
                    notesAddUserDisplay.dataset.userId = '';
                }

                clearNotesAddInputs();
                disableNotesAddRow(true);
                setNotesHeader(null);
                showNotesMessage('', 'info');
            }

            function initializeLocationSection() {
                if (!locationSection || !locationList) {
                    return;
                }

                setLocationMessage('');

                const cards = Array.from(locationList.querySelectorAll('[data-location-card]'));
                cards.forEach(card => {
                    bindLocationCard(card);
                    updateLocationCardCounts(card);
                });

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

                closeLocationCreateForm();
                updateLocationEmptyState();
            }

            function openLocationCreateForm() {
                if (!locationCreateCard || !locationCreateForm) {
                    return;
                }

                locationCreateCard.removeAttribute('hidden');
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
                if (locationCreateInput) {
                    locationCreateInput.value = '';
                }
                if (locationCreateCapacity) {
                    locationCreateCapacity.value = '';
                }
                setLocationFormLoading(locationCreateForm, false);
                setLocationError(locationCreateForm, '');
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

                setLocationError(locationCreateForm, '');
                const { value: parsedCapacity, error: capacityError } = parseCapacityInputValue(capacityField?.value);
                if (capacityError) {
                    setLocationError(locationCreateForm, capacityError);
                    if (capacityField) {
                        capacityField.focus();
                        capacityField.select?.();
                    }
                    return;
                }

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
                if (!card) {
                    return;
                }

                const view = card.querySelector('[data-location-view]');
                const form = card.querySelector('[data-location-edit-form]');
                const input = form?.querySelector('[data-location-input]');
                const capacityInput = form?.querySelector('[data-location-capacity-input]');
                if (!form || !view) {
                    return;
                }

                view.hidden = true;
                form.hidden = false;
                setLocationError(form, '');
                if (input) {
                    input.value = card.dataset.locationName ?? '';
                    input.focus();
                    input.select();
                }
                if (capacityInput) {
                    const capacity = getLocationCapacity(card);
                    capacityInput.value = capacity != null ? String(capacity) : '';
                }
            }

            function closeLocationEdit(card) {
                if (!card) {
                    return;
                }

                const view = card.querySelector('[data-location-view]');
                const form = card.querySelector('[data-location-edit-form]');
                const input = form?.querySelector('[data-location-input]');
                const capacityInput = form?.querySelector('[data-location-capacity-input]');
                if (!form || !view) {
                    return;
                }

                view.hidden = false;
                form.hidden = true;
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
                    setLocationError(form, 'Location identifier is missing.');
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

                const currentName = (card.dataset.locationName ?? '').trim();
                setLocationError(form, '');
                const { value: parsedCapacity, error: capacityError } = parseCapacityInputValue(capacityInput?.value);
                if (capacityError) {
                    setLocationError(form, capacityError);
                    if (capacityInput) {
                        capacityInput.focus();
                        capacityInput.select?.();
                    }
                    return;
                }

                const currentCapacity = getLocationCapacity(card);
                const hasSameName = currentName === proposedName;
                const hasSameCapacity = (currentCapacity ?? null) === (parsedCapacity ?? null);
                if (hasSameName && hasSameCapacity) {
                    closeLocationEdit(card);
                    return;
                }

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
                    const normalized = normalizeLocation(response) ?? { id: locationId, name: proposedName, capacity: parsedCapacity };
                    const resolvedName = normalized.name ?? proposedName;
                    const resolvedCapacity = normalizeCapacityValue(normalized.capacity ?? parsedCapacity);

                    updateLocationCardName(card, resolvedName);
                    setLocationCapacity(card, resolvedCapacity);
                    updateLocationCardCounts(card);
                    if (resolvedCapacity == null) {
                        delete normalized.capacity;
                    } else {
                        normalized.capacity = resolvedCapacity;
                    }
                    closeLocationEdit(card);
                    reorderLocationCard(card);
                    updateReferenceLocation(normalized);
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

                const displayName = card.dataset.locationName || card.querySelector('[data-location-name]')?.textContent || 'this location';
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

                const newName = (card.dataset.locationName ?? '').toString().toLocaleLowerCase();
                const cards = Array.from(locationList.querySelectorAll('[data-location-card]')).filter(existing => existing !== card);
                const referenceNode = cards.find(existing => {
                    const existingName = (existing.dataset.locationName ?? '').toString().toLocaleLowerCase();
                    return newName.localeCompare(existingName, undefined, { sensitivity: 'base' }) < 0;
                });

                if (referenceNode) {
                    locationList.insertBefore(card, referenceNode);
                } else {
                    locationList.appendChild(card);
                }

                updateLocationCardCounts(card);
                updateLocationEmptyState();
            }

            function reorderLocationCard(card) {
                if (!locationList || !card) {
                    return;
                }

                const cards = Array.from(locationList.querySelectorAll('[data-location-card]')).filter(existing => existing !== card);
                const newName = (card.dataset.locationName ?? '').toString().toLocaleLowerCase();
                let inserted = false;
                for (const existing of cards) {
                    const existingName = (existing.dataset.locationName ?? '').toString().toLocaleLowerCase();
                    if (newName.localeCompare(existingName, undefined, { sensitivity: 'base' }) < 0) {
                        locationList.insertBefore(card, existing);
                        inserted = true;
                        break;
                    }
                }

                if (!inserted) {
                    locationList.appendChild(card);
                }
            }

            function updateLocationCardName(card, name) {
                if (!card) {
                    return;
                }

                const normalizedName = name != null ? String(name) : '';
                card.dataset.locationName = normalizedName;
                const title = card.querySelector('[data-location-name]');
                if (title) {
                    title.textContent = normalizedName;
                }
            }

            function setLocationDatasetCounts(card, counts) {
                if (!card) {
                    return;
                }

                const bottleCount = Number(counts?.bottleCount ?? counts?.BottleCount ?? card.dataset.bottleCount ?? 0) || 0;
                const uniqueCount = Number(counts?.uniqueCount ?? counts?.UniqueWineCount ?? card.dataset.uniqueCount ?? 0) || 0;
                const drunkCount = Number(counts?.drunkCount ?? counts?.DrunkBottleCount ?? card.dataset.drunkCount ?? 0) || 0;
                const cellaredSource = counts?.cellaredCount ?? counts?.CellaredBottleCount ?? card.dataset.cellaredCount;
                let cellaredCount = Number(cellaredSource ?? (bottleCount - drunkCount));
                if (!Number.isFinite(cellaredCount)) {
                    cellaredCount = bottleCount - drunkCount;
                }

                card.dataset.bottleCount = String(bottleCount);
                card.dataset.uniqueCount = String(uniqueCount);
                card.dataset.drunkCount = String(drunkCount);
                card.dataset.cellaredCount = String(cellaredCount);
            }

            function updateLocationCardCounts(card) {
                if (!card) {
                    return;
                }

                const bottleCount = Number(card.dataset.bottleCount ?? '0') || 0;
                const uniqueCount = Number(card.dataset.uniqueCount ?? '0') || 0;
                const drunkCount = Number(card.dataset.drunkCount ?? '0') || 0;
                const cellaredCount = Number(card.dataset.cellaredCount ?? String(bottleCount - drunkCount)) || 0;
                const capacity = getLocationCapacity(card);

                const bottleLabel = `${bottleCount} bottle${bottleCount === 1 ? '' : 's'}`;
                const uniqueLabel = uniqueCount > 0
                    ? `· ${uniqueCount} unique wine${uniqueCount === 1 ? '' : 's'}`
                    : '';
                const bottleTarget = card.querySelector('[data-location-bottle-count]');
                if (bottleTarget) {
                    bottleTarget.textContent = bottleLabel;
                }

                const uniqueTarget = card.querySelector('[data-location-wine-count]');
                if (uniqueTarget) {
                    uniqueTarget.textContent = uniqueLabel;
                }

                const fillIndicator = card.querySelector('[data-location-fill-indicator]');
                if (fillIndicator) {
                    const fillBar = fillIndicator.querySelector('[data-location-fill-bar]');
                    const percentTarget = fillIndicator.querySelector('[data-location-fill-percent]');
                    const remainingTarget = fillIndicator.querySelector('[data-location-fill-remaining]');
                    const hasCapacity = capacity != null;

                    fillIndicator.classList.toggle('location-fill-indicator--no-capacity', !hasCapacity);

                    if (hasCapacity) {
                        const numericCapacity = Number(capacity) || 0;
                        const baseRatio = numericCapacity <= 0
                            ? (bottleCount > 0 ? 1 : 0)
                            : bottleCount / numericCapacity;
                        const clampedRatio = Math.min(Math.max(baseRatio, 0), 1);
                        const percent = Math.round(clampedRatio * 100);

                        if (fillBar) {
                            fillBar.style.width = `${percent}%`;
                        }

                        if (percentTarget) {
                            percentTarget.textContent = `${percent}% full`;
                        }

                        const remaining = numericCapacity - bottleCount;
                        let fillSummary;
                        if (remaining > 0) {
                            fillSummary = `${remaining} open`;
                        } else if (remaining === 0) {
                            fillSummary = 'At capacity';
                        } else {
                            const over = Math.abs(remaining);
                            fillSummary = `Over by ${over}`;
                        }

                        if (remainingTarget) {
                            remainingTarget.textContent = fillSummary;
                        }

                        fillIndicator.classList.toggle('location-fill-indicator--over', remaining < 0);
                    } else {
                        if (fillBar) {
                            fillBar.style.width = bottleCount > 0 ? '100%' : '0%';
                        }

                        if (percentTarget) {
                            percentTarget.textContent = 'Capacity not set';
                        }

                        const fillSummary = bottleCount > 0
                            ? `${cellaredCount} cellared · ${drunkCount} enjoyed`
                            : 'Add a capacity to track fill';

                        if (remainingTarget) {
                            remainingTarget.textContent = fillSummary;
                        }

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
                            const over = Math.abs(remaining);
                            capacitySummary = `Over capacity by ${over} bottle${over === 1 ? '' : 's'}.`;
                        }
                        descriptionTarget.textContent = `${base} ${capacitySummary}`.trim();
                    } else {
                        descriptionTarget.textContent = 'No bottles stored here yet.';
                    }
                }
            }

            function updateLocationCardsFromResponse(data) {
                if (!locationList) {
                    return;
                }

                const rawLocations = Array.isArray(data?.locations)
                    ? data.locations
                    : Array.isArray(data?.Locations)
                        ? data.Locations
                        : null;

                if (!Array.isArray(rawLocations)) {
                    return;
                }

                const normalizedLocations = rawLocations
                    .map(normalizeLocationSummary)
                    .filter(location => location && location.id);

                if (normalizedLocations.length === 0) {
                    updateLocationEmptyState();
                    return;
                }

                const existingCards = new Map();
                const cards = Array.from(locationList.querySelectorAll('[data-location-card]'));
                cards.forEach(card => {
                    const identifier = card.dataset.locationId;
                    if (identifier) {
                        existingCards.set(identifier, card);
                    }
                });

                normalizedLocations.forEach(location => {
                    const counts = {
                        bottleCount: location.bottleCount,
                        uniqueCount: location.uniqueWineCount,
                        cellaredCount: location.cellaredBottleCount,
                        drunkCount: location.drunkBottleCount
                    };

                    const card = existingCards.get(location.id);
                    if (card) {
                        updateLocationCardName(card, location.name ?? '');
                        setLocationCapacity(card, location.capacity);
                        setLocationDatasetCounts(card, counts);
                        updateLocationCardCounts(card);
                    } else {
                        const newCard = createLocationCardElement(location, counts);
                        if (newCard) {
                            insertLocationCard(newCard);
                        }
                    }
                });

                updateLocationEmptyState();
            }

            function setLocationMessage(message, variant = 'info') {
                if (!locationMessage) {
                    return;
                }

                const text = message ? String(message).trim() : '';
                if (!text) {
                    locationMessage.textContent = '';
                    locationMessage.setAttribute('hidden', 'hidden');
                    locationMessage.removeAttribute('data-variant');
                    return;
                }

                locationMessage.textContent = text;
                locationMessage.dataset.variant = variant;
                locationMessage.removeAttribute('hidden');
            }

            function setLocationError(container, message) {
                if (!container) {
                    return;
                }

                const target = container.querySelector('[data-location-error]');
                if (!target) {
                    return;
                }

                const text = message ? String(message).trim() : '';
                target.textContent = text;
                if (text) {
                    target.removeAttribute('aria-hidden');
                } else {
                    target.setAttribute('aria-hidden', 'true');
                }
            }

            function toggleDisabledWithMemory(element, state) {
                if (!element) {
                    return;
                }

                if (state) {
                    element.dataset.prevDisabled = element.disabled ? 'true' : 'false';
                    element.disabled = true;
                } else {
                    const wasDisabled = element.dataset.prevDisabled === 'true';
                    element.disabled = wasDisabled;
                    delete element.dataset.prevDisabled;
                }
            }

            function setLocationFormLoading(form, state) {
                if (!form) {
                    return;
                }

                const elements = form.querySelectorAll('input, button, textarea, select');
                elements.forEach(element => toggleDisabledWithMemory(element, state));
            }

            function parseCapacityInputValue(value) {
                if (value == null) {
                    return { value: null, error: null };
                }

                const text = String(value).trim();
                if (text === '') {
                    return { value: null, error: null };
                }

                if (!/^-?\d+$/.test(text)) {
                    return { value: null, error: 'Capacity must be a whole number.' };
                }

                const parsed = Number(text);
                if (!Number.isFinite(parsed) || !Number.isInteger(parsed)) {
                    return { value: null, error: 'Capacity must be a whole number.' };
                }

                if (parsed < 0) {
                    return { value: null, error: 'Capacity must be zero or greater.' };
                }

                if (parsed > MAX_LOCATION_CAPACITY) {
                    return {
                        value: null,
                        error: `Capacity cannot exceed ${MAX_LOCATION_CAPACITY} bottles.`
                    };
                }

                return { value: parsed, error: null };
            }

            function normalizeCapacityValue(raw) {
                if (raw == null || raw === '') {
                    return null;
                }

                const numeric = Number(raw);
                if (!Number.isFinite(numeric)) {
                    return null;
                }

                const integer = Math.trunc(numeric);
                if (!Number.isFinite(integer) || integer < 0) {
                    return null;
                }

                return integer;
            }

            function setLocationCapacity(card, capacity) {
                if (!card) {
                    return;
                }

                const normalized = normalizeCapacityValue(capacity);
                if (normalized == null) {
                    delete card.dataset.locationCapacity;
                    return;
                }

                card.dataset.locationCapacity = String(normalized);
            }

            function getLocationCapacity(card) {
                if (!card) {
                    return null;
                }

                return normalizeCapacityValue(card.dataset?.locationCapacity ?? null);
            }

            function setLocationCardLoading(card, state) {
                if (!card) {
                    return;
                }

                const actions = card.querySelectorAll('[data-location-edit], [data-location-delete]');
                actions.forEach(button => toggleDisabledWithMemory(button, state));
            }

            function removeLocationCard(card) {
                if (!card) {
                    return;
                }

                card.remove();
                updateLocationEmptyState();
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

            function addLocationToReference(location) {
                const normalized = normalizeLocation(location);
                if (!normalized) {
                    return;
                }

                referenceData.bottleLocations = sortLocations([...referenceData.bottleLocations, normalized]);
                refreshLocationOptions();
            }

            function updateReferenceLocation(location) {
                const normalized = normalizeLocation(location);
                if (!normalized) {
                    return;
                }

                referenceData.bottleLocations = sortLocations([...referenceData.bottleLocations, normalized]);
                refreshLocationOptions();
            }

            function removeReferenceLocation(locationId) {
                if (!locationId) {
                    return;
                }

                referenceData.bottleLocations = referenceData.bottleLocations.filter(option => {
                    const normalized = normalizeLocation(option);
                    return normalized?.id && normalized.id !== locationId;
                });
                refreshLocationOptions();
            }

            function refreshLocationOptions() {
                if (addWineLocation) {
                    populateLocationSelect(addWineLocation, addWineLocation.value ?? '');
                }

                if (detailAddLocation) {
                    populateLocationSelect(detailAddLocation, detailAddLocation.value ?? '');
                }

                if (detailsBody) {
                    const selects = Array.from(detailsBody.querySelectorAll('.detail-location'));
                    selects.forEach(select => {
                        populateLocationSelect(select, select.value ?? '');
                    });
                }
            }

            function normalizeLocationSummary(raw) {
                if (!raw) {
                    return null;
                }

                const idValue = raw.id ?? raw.Id ?? raw.locationId ?? raw.LocationId;
                if (!idValue) {
                    return null;
                }

                const normalized = {
                    id: String(idValue)
                };

                const nameValue = raw.name ?? raw.Name ?? '';
                if (typeof nameValue === 'string' && nameValue.trim()) {
                    normalized.name = nameValue;
                } else if (nameValue != null) {
                    normalized.name = String(nameValue);
                }

                const capacityValue = raw.capacity ?? raw.Capacity ?? null;
                const normalizedCapacity = normalizeCapacityValue(capacityValue);
                if (normalizedCapacity != null) {
                    normalized.capacity = normalizedCapacity;
                }

                normalized.bottleCount = Number(raw.bottleCount ?? raw.BottleCount ?? 0) || 0;
                normalized.uniqueWineCount = Number(raw.uniqueWineCount ?? raw.UniqueWineCount ?? 0) || 0;
                normalized.cellaredBottleCount = Number(raw.cellaredBottleCount ?? raw.CellaredBottleCount ?? 0) || 0;
                normalized.drunkBottleCount = Number(raw.drunkBottleCount ?? raw.DrunkBottleCount ?? 0) || 0;

                return normalized;
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
                const userValue = raw.userId ?? raw.UserId ?? raw.ownerId ?? raw.OwnerId ?? '';
                const capacityValue = raw.capacity ?? raw.Capacity ?? raw.maxCapacity ?? raw.MaxCapacity;
                const normalized = {
                    id: String(id),
                    name: typeof nameValue === 'string' ? nameValue : String(nameValue ?? id)
                };
                if (userValue) {
                    normalized.userId = String(userValue);
                }
                const normalizedCapacity = normalizeCapacityValue(capacityValue);
                if (normalizedCapacity != null) {
                    normalized.capacity = normalizedCapacity;
                }
                return normalized;
            }

            function sortLocations(list) {
                const seen = new Map();
                list.forEach(item => {
                    const normalized = normalizeLocation(item);
                    if (normalized?.id) {
                        seen.set(normalized.id, normalized);
                    }
                });

                return Array.from(seen.values()).sort((a, b) => {
                    const nameA = (a.name ?? '').toString().toLocaleLowerCase();
                    const nameB = (b.name ?? '').toString().toLocaleLowerCase();
                    if (nameA === nameB) {
                        return (a.id ?? '').localeCompare(b.id ?? '');
                    }

                    return nameA.localeCompare(nameB);
                });
            }

            async function loadReferenceData() {
                try {
                    const response = await sendJson('/wine-manager/options', { method: 'GET' });
                    const subApps = Array.isArray(response?.subAppellations)
                        ? response.subAppellations
                        : Array.isArray(response?.SubAppellations)
                            ? response.SubAppellations
                            : [];
                    const locations = Array.isArray(response?.bottleLocations)
                        ? response.bottleLocations
                        : Array.isArray(response?.BottleLocations)
                            ? response.BottleLocations
                            : [];
                    const users = Array.isArray(response?.users)
                        ? response.users
                        : Array.isArray(response?.Users)
                            ? response.Users
                            : [];

                    referenceData.subAppellations = subApps;
                    referenceData.bottleLocations = sortLocations(locations);
                    referenceData.users = users;

                    refreshLocationOptions();
                } catch (error) {
                    showMessage(error.message, 'error');
                }
            }

            function populateSubAppellationSelect(select, selectedId) {
                if (!select) {
                    return;
                }

                const previousValue = selectedId ?? select.value ?? '';
                select.innerHTML = '';

                const placeholder = document.createElement('option');
                placeholder.value = '';
                placeholder.textContent = 'Select sub-appellation';
                select.appendChild(placeholder);

                referenceData.subAppellations.forEach(option => {
                    const id = option?.id ?? option?.Id;
                    const label = option?.label ?? option?.Label;
                    if (!id) {
                        return;
                    }

                    const opt = document.createElement('option');
                    opt.value = id;
                    opt.textContent = label ?? id;
                    select.appendChild(opt);
                });

                if (previousValue) {
                    select.value = previousValue;
                }
            }

            function populateLocationSelect(select, selectedId) {
                if (!select) {
                    return;
                }

                const previousValue = selectedId ?? select.value ?? '';
                select.innerHTML = '';

                const placeholder = document.createElement('option');
                placeholder.value = '';
                placeholder.textContent = 'No location';
                select.appendChild(placeholder);

                referenceData.bottleLocations.forEach(option => {
                    const normalized = normalizeLocation(option);
                    if (!normalized?.id) {
                        return;
                    }

                    const opt = document.createElement('option');
                    opt.value = normalized.id;
                    const label = normalized.name ?? normalized.id;
                    const capacity = normalizeCapacityValue(normalized.capacity);
                    const suffix = capacity != null ? ` (${capacity} capacity)` : '';
                    opt.textContent = `${label}${suffix}`;
                    select.appendChild(opt);
                });

                if (previousValue) {
                    select.value = previousValue;
                }
            }

            function buildSummaryRow(summary) {
                const row = document.createElement('tr');
                row.className = 'group-row';
                row.setAttribute('tabindex', '0');
                row.setAttribute('role', 'button');
                row.setAttribute('aria-controls', 'details-table');
                applySummaryToRow(row, summary, true);
                return row;
            }

            function ensureSummaryRowStructure(row) {
                if (row.querySelector('.summary-wine')) {
                    return;
                }

                row.innerHTML = `
                    <td class="summary-wine"></td>
                    <td class="summary-appellation"></td>
                    <td class="summary-vintage"></td>
                    <td class="summary-bottles" data-field="bottle-count"></td>
                    <td class="summary-color"></td>
                    <td class="summary-status"><span class="status-pill" data-field="status"></span></td>
                    <td class="summary-score" data-field="score"></td>`;
            }

            function applySummaryToRow(row, summary, isNewRow = false) {
                ensureSummaryRowStructure(row);

                row.dataset.groupId = summary?.wineVintageId ?? summary?.WineVintageId ?? '';
                row.dataset.wineId = summary?.wineId ?? summary?.WineId ?? '';
                row.dataset.subAppellationId = summary?.subAppellationId ?? summary?.SubAppellationId ?? '';
                row.dataset.appellationId = summary?.appellationId ?? summary?.AppellationId ?? '';

                const wineCell = row.querySelector('.summary-wine');
                const appCell = row.querySelector('.summary-appellation');
                const vintageCell = row.querySelector('.summary-vintage');
                const bottlesCell = row.querySelector('.summary-bottles');
                const colorCell = row.querySelector('.summary-color');
                const statusSpan = row.querySelector('[data-field="status"]');
                const scoreCell = row.querySelector('[data-field="score"]');

                const displayAppellation = buildAppellationDisplay(summary);
                const vintageRaw = summary?.vintage ?? summary?.Vintage;
                const vintageIsZero = Number(vintageRaw) === 0;
                const hasVintage = vintageRaw != null && String(vintageRaw) !== '' && !vintageIsZero;
                const wineName = summary?.wineName ?? summary?.WineName ?? '';
                const colorValue = summary?.color ?? summary?.Color ?? '';
                const statusValue = summary?.statusLabel ?? summary?.StatusLabel ?? '';

                row.dataset.summaryWine = wineName;
                row.dataset.summaryAppellation = normalizeGroupingValue(displayAppellation);
                row.dataset.summaryVintage = hasVintage ? String(vintageRaw) : '';
                row.dataset.summaryColor = colorValue;
                row.dataset.summaryStatus = statusValue;

                if (wineCell) {
                    wineCell.textContent = wineName;
                }

                if (appCell) {
                    appCell.textContent = displayAppellation;
                }

                if (vintageCell) {
                    vintageCell.textContent = hasVintage ? String(vintageRaw) : '—';
                }

                if (bottlesCell) {
                    const count = summary?.bottleCount ?? summary?.BottleCount ?? 0;
                    bottlesCell.textContent = Number(count).toString();
                }

                if (colorCell) {
                    colorCell.textContent = colorValue;
                }

                if (statusSpan) {
                    const cssClass = summary?.statusCssClass ?? summary?.StatusCssClass ?? '';
                    statusSpan.textContent = statusValue;
                    statusSpan.className = `status-pill ${cssClass}`;
                }

                if (scoreCell) {
                    const score = summary?.averageScore ?? summary?.AverageScore;
                    scoreCell.textContent = score != null ? Number(score).toFixed(1) : '—';
                }

                if (isNewRow) {
                    row.classList.remove('editing', 'selected');
                }
            }

            async function enterEditMode(row) {
                if (row.classList.contains('editing') || loading) {
                    return;
                }

                await referenceDataPromise;

                const summary = extractSummaryFromRow(row);
                row.dataset.originalSummary = JSON.stringify(summary);
                row.classList.add('editing');

                const wineCell = row.querySelector('.summary-wine');
                const appCell = row.querySelector('.summary-appellation');
                const vintageCell = row.querySelector('.summary-vintage');
                const colorCell = row.querySelector('.summary-color');
                const actionsCell = row.querySelector('.summary-actions .actions');

                if (wineCell) {
                    wineCell.innerHTML = `<input type="text" class="summary-edit-name" value="${escapeHtml(summary.wineName ?? '')}" />`;
                }

                if (appCell) {
                    const select = document.createElement('select');
                    select.className = 'summary-edit-sub-app';
                    populateSubAppellationSelect(select, summary.subAppellationId ?? '');
                    appCell.innerHTML = '';
                    appCell.appendChild(select);
                }

                if (vintageCell) {
                    vintageCell.innerHTML = `<input type="number" class="summary-edit-vintage" min="1900" max="2100" value="${summary.vintage ?? ''}" />`;
                }

                if (colorCell) {
                    const select = document.createElement('select');
                    select.className = 'summary-edit-color';
                    select.innerHTML = `
                        <option value="Red">Red</option>
                        <option value="White">White</option>
                        <option value="Rose">Rosé</option>`;
                    select.value = summary.color ?? 'Red';
                    colorCell.innerHTML = '';
                    colorCell.appendChild(select);
                }

                if (actionsCell) {
                    actionsCell.innerHTML = `
                        <button type="button" class="crud-table__action-button save-group">Save</button>
                        <button type="button" class="crud-table__action-button secondary cancel-group">Cancel</button>`;
                }
            }

            function cancelSummaryEdit(row) {
                const original = row.dataset.originalSummary
                    ? JSON.parse(row.dataset.originalSummary)
                    : extractSummaryFromRow(row);

                applySummaryToRow(row, original);
                row.classList.remove('editing');
                refreshGrouping({ expandForRow: row });
            }

            async function saveSummaryEdit(row) {
                if (!row.classList.contains('editing') || loading) {
                    return;
                }

                await referenceDataPromise;

                const nameInput = row.querySelector('.summary-edit-name');
                const subAppSelect = row.querySelector('.summary-edit-sub-app');
                const vintageInput = row.querySelector('.summary-edit-vintage');
                const colorSelect = row.querySelector('.summary-edit-color');

                const name = nameInput?.value?.trim() ?? '';
                const subAppellationId = subAppSelect?.value ?? '';
                const vintageValue = Number(vintageInput?.value ?? '');
                const color = colorSelect?.value ?? '';

                if (!name) {
                    showMessage('Wine name is required.', 'error');
                    return;
                }

                if (!subAppellationId) {
                    showMessage('Select a sub-appellation for the wine.', 'error');
                    return;
                }

                if (!Number.isInteger(vintageValue)) {
                    showMessage('Enter a valid vintage year.', 'error');
                    return;
                }

                if (!color) {
                    showMessage('Choose a wine color.', 'error');
                    return;
                }

                const payload = {
                    wineName: name,
                    subAppellationId,
                    vintage: vintageValue,
                    color
                };

                const groupId = row.dataset.groupId;
                if (!groupId) {
                    showMessage('Unable to determine the selected wine group.', 'error');
                    return;
                }

                try {
                    setSummaryRowLoading(row, true);
                    const response = await sendJson(`/wine-manager/groups/${groupId}`, {
                        method: 'PUT',
                        body: JSON.stringify(payload)
                    });

                    const summary = normalizeSummary(response?.group ?? response?.Group);
                    if (!summary) {
                        showMessage('Wine group updated, but it could not be displayed.', 'warning');
                        cancelSummaryEdit(row);
                        return;
                    }

                    applySummaryToRow(row, summary);
                    row.classList.remove('editing');
                    if (selectedRow === row) {
                        selectedSummary = summary;
                    }

                    refreshGrouping({ expandForRow: row });

                    showMessage('Wine group updated.', 'success');
                    await renderDetails(response, selectedRow === row);
                } catch (error) {
                    showMessage(error.message, 'error');
                } finally {
                    setSummaryRowLoading(row, false);
                }
            }

            async function handleSummaryDelete(row) {
                if (loading) {
                    return;
                }

                const groupId = row.dataset.groupId;
                if (!groupId) {
                    return;
                }

                const confirmed = window.confirm('Delete this wine group and all of its bottles?');
                if (!confirmed) {
                    return;
                }

                try {
                    setSummaryRowLoading(row, true);
                    await sendJson(`/wine-manager/groups/${groupId}`, { method: 'DELETE' });

                    if (selectedRow === row) {
                        showInventoryView();
                        resetDetailsView();
                    }

                    row.remove();
                    refreshGrouping({ expandForRow: null });
                    showMessage('Wine group deleted.', 'success');
                } catch (error) {
                    showMessage(error.message, 'error');
                } finally {
                    setSummaryRowLoading(row, false);
                }
            }

            function extractSummaryFromRow(row) {
                return {
                    wineVintageId: row.dataset.groupId ?? '',
                    wineId: row.dataset.wineId ?? '',
                    wineName: row.querySelector('.summary-wine')?.textContent?.trim() ?? '',
                    subAppellation: row.querySelector('.summary-appellation')?.textContent?.trim() ?? '',
                    subAppellationId: row.dataset.subAppellationId ?? '',
                    appellationId: row.dataset.appellationId ?? '',
                    vintage: Number(row.querySelector('.summary-vintage')?.textContent ?? '') || null,
                    bottleCount: Number(row.querySelector('.summary-bottles')?.textContent ?? '') || 0,
                    color: row.querySelector('.summary-color')?.textContent?.trim() ?? '',
                    statusLabel: row.querySelector('[data-field="status"]')?.textContent?.trim() ?? '',
                    statusCssClass: row.querySelector('[data-field="status"]')?.className?.replace('status-pill', '').trim() ?? '',
                    averageScore: (() => {
                        const value = row.querySelector('[data-field="score"]')?.textContent?.trim() ?? '';
                        const parsed = Number(value);
                        return Number.isFinite(parsed) ? parsed : null;
                    })()
                };
            }

            function showDetailsView() {
                if (inventorySection) {
                    inventorySection.hidden = true;
                    inventorySection.setAttribute('aria-hidden', 'true');
                }
                if (detailsSection) {
                    detailsSection.hidden = false;
                    detailsSection.setAttribute('aria-hidden', 'false');
                }
            }

            function showInventoryView() {
                if (inventorySection) {
                    inventorySection.hidden = false;
                    inventorySection.setAttribute('aria-hidden', 'false');
                }
                if (detailsSection) {
                    detailsSection.hidden = true;
                    detailsSection.setAttribute('aria-hidden', 'true');
                }
            }

            function resetDetailsView() {
                if (selectedRow) {
                    selectedRow.classList.remove('selected');
                    selectedRow.setAttribute('aria-expanded', 'false');
                }

                selectedRow = null;
                selectedGroupId = null;
                selectedSummary = null;

                closeNotesPanel();
                selectedDetailRowElement = null;
                notesSelectedBottleId = null;

                setDetailsTitle('Bottle Details', '');
                detailsSubtitle.textContent = 'Select a wine group to view individual bottles.';
                if (detailAddRow) {
                    detailAddRow.hidden = true;
                }

                if (detailAddPrice) {
                    detailAddPrice.value = '';
                }
                if (detailAddLocation) {
                    detailAddLocation.value = '';
                }
                if (detailAddQuantity) {
                    detailAddQuantity.value = '1';
                }
                if (detailAddButton) {
                    detailAddButton.disabled = true;
                }

                closeActiveDetailActionsMenu();
                closeActiveDetailActionsMenu();
                detailsBody.querySelectorAll('.detail-row').forEach(r => r.remove());
                emptyRow.hidden = false;
                showMessage('', 'info');
            }

            async function handleRowSelection(row, options = {}) {
                if (loading && !options.force) {
                    return;
                }

                const groupId = row.dataset.groupId;
                const wineId = row.dataset.wineId;
                if (!groupId && !wineId) {
                    return;
                }

                const selectionKey = wineId || groupId;
                if (selectedGroupId === selectionKey && !options.force) {
                    return;
                }

                if (selectedRow && selectedRow !== row) {
                    selectedRow.classList.remove('selected');
                    selectedRow.setAttribute('aria-expanded', 'false');
                }

                showDetailsView();

                selectedRow = row;
                selectedGroupId = selectionKey;
                row.classList.add('selected');
                row.setAttribute('aria-expanded', 'true');

                if (options.response) {
                    await renderDetails(options.response, true);
                    return;
                }

                await loadDetails({ wineId, groupId }, false);
            }

            async function loadDetails(key, updateRow) {
                showMessage('Loading bottle details…', 'info');
                emptyRow.hidden = false;
                closeActiveDetailActionsMenu();
                detailsBody.querySelectorAll('.detail-row').forEach(r => r.remove());

                const groupId = typeof key === 'string' ? key : key?.groupId;
                const wineId = typeof key === 'object' ? key?.wineId : null;

                try {
                    setLoading(true);
                    const url = wineId ? `/wine-manager/wine/${wineId}/details` : `/wine-manager/bottles/${groupId}`;
                    const response = await sendJson(url, { method: 'GET' });
                    await renderDetails(response, updateRow);
                    showMessage('', 'info');
                } catch (error) {
                    showMessage(error.message, 'error');
                } finally {
                    setLoading(false);
                }
            }

            async function renderDetails(data, shouldUpdateRow) {
                await referenceDataPromise;

                const rawSummary = data?.group ?? data?.Group ?? null;
                const summary = normalizeSummary(rawSummary);
                const rawDetails = Array.isArray(data?.details)
                    ? data.details
                    : Array.isArray(data?.Details)
                        ? data.Details
                        : [];
                const details = rawDetails.map(normalizeDetail).filter(Boolean);

                selectedSummary = summary;

                if (summary) {
                    setDetailsTitle(summary.wineName ?? '', summary.vintage ?? '');
                    detailsSubtitle.textContent = `${summary.bottleCount ?? 0} bottle${summary.bottleCount === 1 ? '' : 's'} · ${summary.statusLabel ?? ''}`;
                    if (detailAddRow) {
                        detailAddRow.hidden = false;
                    }
                    if (detailAddPrice) {
                        detailAddPrice.value = '';
                    }
                    if (detailAddLocation) {
                        populateLocationSelect(detailAddLocation, '');
                    }
                    if (detailAddQuantity) {
                        detailAddQuantity.value = '1';
                    }
                    if (detailAddButton) {
                        detailAddButton.disabled = loading;
                    }
                    disableNotesAddRow(notesLoading || !notesSelectedBottleId);
                } else {
                    setDetailsTitle('Bottle Details', '');
                    detailsSubtitle.textContent = 'No bottles remain for the selected group.';
                    if (detailAddRow) {
                        detailAddRow.hidden = true;
                    }
                    closeNotesPanel();
                }

                detailsBody.querySelectorAll('.detail-row').forEach(r => r.remove());
                selectedDetailRowElement = null;

                if (details.length === 0) {
                    emptyRow.hidden = false;
                    closeNotesPanel();
                } else {
                    emptyRow.hidden = true;
                    let detailMatchFound = false;
                    details.forEach(detail => {
                        const row = buildDetailRow(detail, summary);
                        detailsBody.appendChild(row);
                        if (notesSelectedBottleId && row.dataset.bottleId === notesSelectedBottleId) {
                            row.classList.add('selected');
                            selectedDetailRowElement = row;
                            detailMatchFound = true;
                        }
                    });

                    if (notesSelectedBottleId && !detailMatchFound) {
                        closeNotesPanel();
                    }
                }

                updateLocationCardsFromResponse(data);

                if (shouldUpdateRow) {
                    updateSummaryRow(summary ?? null);
                }
            }

            function setDetailsTitle(name, vintage) {
                const resolvedName = typeof name === 'string' ? name : name != null ? String(name) : '';
                let resolvedVintage = typeof vintage === 'string' ? vintage : vintage != null ? String(vintage) : '';
                if (resolvedVintage === '0') {
                    resolvedVintage = '';
                }

                if (detailsTitleName) {
                    detailsTitleName.textContent = resolvedName;
                } else if (detailsTitle && !resolvedVintage) {
                    detailsTitle.textContent = resolvedName;
                } else if (detailsTitle && resolvedVintage) {
                    detailsTitle.textContent = `${resolvedName} • ${resolvedVintage}`;
                }

                if (detailsTitleVintage) {
                    detailsTitleVintage.textContent = resolvedVintage;
                }

                if (detailsTitle) {
                    const hasVintage = resolvedVintage.length > 0;
                    detailsTitle.classList.toggle('details-title--has-vintage', hasVintage);
                }
            }

            function updateSummaryRow(summary) {
                if (!selectedGroupId) {
                    return;
                }

                let row = inventoryTable.querySelector(`tr[data-wine-id="${selectedGroupId}"]`);
                if (!row) {
                    row = inventoryTable.querySelector(`tr[data-group-id="${selectedGroupId}"]`);
                }
                if (!row) {
                    return;
                }

                if (!summary) {
                    row.remove();
                    selectedRow = null;
                    selectedGroupId = null;
                    refreshGrouping({ expandForRow: null });
                    return;
                }

                applySummaryToRow(row, summary);
                refreshGrouping({ expandForRow: row });
            }

            function buildDetailRow(detail, summary) {
                const row = document.createElement('tr');
                row.className = 'detail-row';
                const normalizedBottleId = detail.bottleId
                    ? String(detail.bottleId)
                    : detail.BottleId
                        ? String(detail.BottleId)
                        : '';
                const rawDrunkAt = detail.drunkAt ?? detail.DrunkAt ?? null;
                const normalizedDrunkAt = normalizeIsoDate(rawDrunkAt);
                const isDrunk = Boolean(detail.isDrunk ?? detail.IsDrunk);
                const rawUserId = detail.userId ?? detail.UserId ?? '';
                const normalizedUserId = rawUserId ? String(rawUserId) : '';
                const rawNoteId = detail.currentUserNoteId ?? detail.CurrentUserNoteId ?? null;
                const rawNoteText = detail.currentUserNote ?? detail.CurrentUserNote ?? '';
                const rawNoteScore = detail.currentUserScore ?? detail.CurrentUserScore ?? null;
                const normalizedNoteId = rawNoteId ? String(rawNoteId) : '';
                const normalizedNoteText = typeof rawNoteText === 'string'
                    ? rawNoteText
                    : rawNoteText != null
                        ? String(rawNoteText)
                        : '';
                const normalizedNoteScoreNumber = (() => {
                    if (rawNoteScore == null || rawNoteScore === '') {
                        return null;
                    }

                    const parsed = Number(rawNoteScore);
                    return Number.isFinite(parsed) ? parsed : null;
                })();
                const normalizedNoteScoreString = normalizedNoteScoreNumber != null
                    ? String(normalizedNoteScoreNumber)
                    : '';

                const normalizedDetail = {
                    ...detail,
                    bottleId: normalizedBottleId,
                    userId: normalizedUserId,
                    currentUserNoteId: normalizedNoteId ? normalizedNoteId : null,
                    currentUserNote: normalizedNoteText,
                    currentUserScore: normalizedNoteScoreNumber
                };

                detail = normalizedDetail;
                row[DETAIL_ROW_DATA_KEY] = detail;
                row.dataset.bottleId = normalizedBottleId;

                const enjoyedAtDisplay = normalizedDrunkAt ? formatDateDisplay(normalizedDrunkAt) : '—';
                const drinkButtonLabel = isDrunk ? 'Update Drink Details' : 'Drink Bottle';

                row.dataset.drunkAt = normalizedDrunkAt;
                row.dataset.isDrunk = isDrunk ? 'true' : 'false';
                row.dataset.userId = normalizedUserId;
                row.dataset.noteId = normalizedNoteId;
                if (normalizedNoteScoreString) {
                    row.dataset.noteScore = normalizedNoteScoreString;
                } else {
                    delete row.dataset.noteScore;
                }

                row.innerHTML = `
                    <td class="detail-location-cell">
                        <div class="detail-location-content">
                            <div class="detail-location-select-container" data-location-select-container></div>
                            <div class="detail-row-mobile-actions" data-detail-actions-menu>
                                <button type="button" class="detail-row-mobile-actions__trigger" aria-haspopup="menu" aria-expanded="false">
                                    <span class="visually-hidden">Open bottle actions</span>
                                    <span aria-hidden="true" class="detail-row-mobile-actions__icon">⋮</span>
                                </button>
                                <div class="detail-row-mobile-actions__list" role="menu">
                                    <button type="button" class="detail-row-mobile-actions__item" role="menuitem" data-menu-action="drink">Drink Bottle</button>
                                    <button type="button" class="detail-row-mobile-actions__item" role="menuitem" data-menu-action="save">Save</button>
                                    <button type="button" class="detail-row-mobile-actions__item detail-row-mobile-actions__item--danger" role="menuitem" data-menu-action="delete">Remove</button>
                                </div>
                            </div>
                        </div>
                    </td>
                    <td class="detail-price-cell"><input type="number" step="0.01" min="0" class="detail-price" value="${detail.price ?? detail.Price ?? ''}" placeholder="0.00" /></td>
                    <td class="detail-average">${formatScore(detail.averageScore ?? detail.AverageScore)}</td>
                    <td class="detail-enjoyed-at">${escapeHtml(enjoyedAtDisplay)}</td>
                    <td class="actions">
                        <button type="button" class="crud-table__action-button drink-bottle-trigger">${escapeHtml(drinkButtonLabel)}</button>
                        <button type="button" class="crud-table__action-button save">Save</button>
                        <button type="button" class="crud-table__action-button secondary delete">Remove</button>
                    </td>`;

                const locationCell = row.querySelector('.detail-location-cell');
                const locationSelectContainer = locationCell?.querySelector('[data-location-select-container]');
                const locationSelect = document.createElement('select');
                locationSelect.className = 'detail-location';
                populateLocationSelect(locationSelect, detail.bottleLocationId ?? detail.BottleLocationId ?? '');
                if (locationSelectContainer) {
                    locationSelectContainer.appendChild(locationSelect);
                } else {
                    locationCell?.appendChild(locationSelect);
                }

                const drinkButton = row.querySelector('.drink-bottle-trigger');
                const saveButton = row.querySelector('.save');
                const deleteButton = row.querySelector('.delete');

                setupDetailRowMenu(row, drinkButton, saveButton, deleteButton);

                drinkButton?.addEventListener('click', (event) => {
                    event.preventDefault();
                    event.stopPropagation();

                    try {
                        const detailContext = row[DETAIL_ROW_DATA_KEY] ?? detail;
                        openDrinkBottleModal(detailContext, summary);
                    } catch (error) {
                        showMessage(error?.message ?? String(error), 'error');
                    }
                });

                saveButton?.addEventListener('click', async () => {
                    if (!selectedSummary || loading) {
                        return;
                    }

                    const rowUserId = row.dataset.userId ? row.dataset.userId : '';
                    const detailContext = row[DETAIL_ROW_DATA_KEY] ?? detail;
                    const normalizedUserId = rowUserId
                        || (detailContext?.userId ? String(detailContext.userId) : detailContext?.UserId ? String(detailContext.UserId) : '');
                    const payloadUserId = normalizedUserId ? normalizedUserId : null;

                    const payload = {
                        wineVintageId: selectedSummary.wineVintageId,
                        price: parsePrice(row.querySelector('.detail-price')?.value ?? ''),
                        isDrunk: row.dataset.isDrunk === 'true',
                        drunkAt: row.dataset.drunkAt || null,
                        bottleLocationId: locationSelect.value || null,
                        userId: payloadUserId
                    };

                    try {
                        setRowLoading(row, true);
                        const response = await sendJson(`/wine-manager/bottles/${detailContext?.bottleId ?? detailContext?.BottleId ?? detail.bottleId ?? detail.BottleId}`, {
                            method: 'PUT',
                            body: JSON.stringify(payload)
                        });
                        await renderDetails(response, true);
                        showMessage('Bottle updated.', 'success');
                    } catch (error) {
                        showMessage(error.message, 'error');
                    } finally {
                        setRowLoading(row, false);
                    }
                });

                deleteButton?.addEventListener('click', async () => {
                    if (!selectedSummary || loading) {
                        return;
                    }

                    const confirmed = window.confirm('Remove this bottle from the inventory?');
                    if (!confirmed) {
                        return;
                    }

                    try {
                        setRowLoading(row, true);
                        const response = await sendJson(`/wine-manager/bottles/${detail.bottleId ?? detail.BottleId}`, {
                            method: 'DELETE'
                        });
                        await renderDetails(response, true);
                        showMessage('Bottle removed.', 'success');
                    } catch (error) {
                        showMessage(error.message, 'error');
                    } finally {
                        setRowLoading(row, false);
                    }
                });

                if (notesEnabled) {
                    row.addEventListener('click', async (event) => {
                        if (shouldIgnoreDetailRowClick(event)) {
                            return;
                        }

                        event.preventDefault();
                        try {
                            await handleDetailRowSelection(row, detail, summary);
                        } catch (error) {
                            showNotesMessage(error?.message ?? String(error), 'error');
                        }
                    });
                }

                return row;
            }

            function setupDetailRowMenu(row, drinkButton, saveButton, deleteButton) {
                const menuContainer = row.querySelector('[data-detail-actions-menu]');
                if (!menuContainer) {
                    return;
                }

                const trigger = menuContainer.querySelector('.detail-row-mobile-actions__trigger');
                const list = menuContainer.querySelector('.detail-row-mobile-actions__list');

                if (!(trigger instanceof HTMLElement) || !(list instanceof HTMLElement)) {
                    return;
                }

                menuContainer.dataset.open = 'false';
                trigger.setAttribute('aria-expanded', 'false');
                list.hidden = true;

                trigger.addEventListener('click', (event) => {
                    event.preventDefault();
                    event.stopPropagation();

                    const isAlreadyOpen = activeDetailActionsMenu === menuContainer;

                    if (activeDetailActionsMenu && activeDetailActionsMenu !== menuContainer) {
                        closeActiveDetailActionsMenu();
                    }

                    if (isAlreadyOpen) {
                        closeActiveDetailActionsMenu();
                        return;
                    }

                    activeDetailActionsMenu = menuContainer;
                    menuContainer.dataset.open = 'true';
                    trigger.setAttribute('aria-expanded', 'true');
                    list.hidden = false;

                    const firstAction = list.querySelector('.detail-row-mobile-actions__item');
                    if (firstAction instanceof HTMLElement) {
                        firstAction.focus();
                    }
                });

                list.addEventListener('click', (event) => {
                    event.stopPropagation();

                    const target = event.target;
                    if (!(target instanceof HTMLElement)) {
                        return;
                    }

                    const action = target.dataset.menuAction;
                    closeActiveDetailActionsMenu();

                    if (action === 'drink') {
                        drinkButton?.click();
                    } else if (action === 'save') {
                        saveButton?.click();
                    } else if (action === 'delete') {
                        deleteButton?.click();
                    }
                });

                list.addEventListener('keydown', (event) => {
                    if (event.key === 'Escape') {
                        event.preventDefault();
                        closeActiveDetailActionsMenu({ focusTrigger: true });
                    }
                });

                menuContainer.addEventListener('focusout', (event) => {
                    const nextFocus = event.relatedTarget;
                    if (!(nextFocus instanceof HTMLElement) || !menuContainer.contains(nextFocus)) {
                        closeActiveDetailActionsMenu();
                    }
                });
            }

            function closeActiveDetailActionsMenu(options = {}) {
                const { focusTrigger = false } = options;
                const menu = activeDetailActionsMenu;

                if (!menu) {
                    return;
                }

                if (!document.body.contains(menu)) {
                    activeDetailActionsMenu = null;
                    return;
                }

                menu.dataset.open = 'false';

                const trigger = menu.querySelector('.detail-row-mobile-actions__trigger');
                const list = menu.querySelector('.detail-row-mobile-actions__list');

                if (list) {
                    list.hidden = true;
                }

                if (trigger instanceof HTMLElement) {
                    trigger.setAttribute('aria-expanded', 'false');

                    if (focusTrigger) {
                        trigger.focus();
                    }
                }

                activeDetailActionsMenu = null;
            }

            async function handleAddBottle() {
                if (!selectedSummary || loading) {
                    return;
                }

                const quantityValue = parseInt(detailAddQuantity?.value ?? '1', 10);
                const quantity = Number.isNaN(quantityValue) ? 1 : Math.min(Math.max(quantityValue, 1), 12);

                const locationValue = detailAddLocation?.value ?? '';

                const payload = {
                    wineVintageId: selectedSummary.wineVintageId,
                    price: parsePrice(detailAddPrice?.value ?? ''),
                    isDrunk: false,
                    drunkAt: null,
                    bottleLocationId: locationValue || null,
                    userId: null,
                    quantity
                };

                try {
                    setLoading(true);
                    const response = await sendJson('/wine-manager/bottles', {
                        method: 'POST',
                        body: JSON.stringify(payload)
                    });

                    if (detailAddPrice) {
                        detailAddPrice.value = '';
                    }
                    if (detailAddLocation) {
                        detailAddLocation.value = '';
                    }
                    if (detailAddQuantity) {
                        detailAddQuantity.value = '1';
                    }

                    await renderDetails(response, true);
                    showMessage(quantity > 1 ? 'Bottles added successfully.' : 'Bottle added successfully.', 'success');
                } catch (error) {
                    showMessage(error.message, 'error');
                } finally {
                    setLoading(false);
                }
            }

            function setLoading(state) {
                loading = state;
                if (addWineButton) {
                    addWineButton.disabled = state;
                }
                if (detailAddButton) {
                    detailAddButton.disabled = state || Boolean(detailAddRow?.hidden);
                }
            }

            function setSummaryRowLoading(row, state) {
                if (state) {
                    row.classList.add('loading');
                } else {
                    row.classList.remove('loading');
                }

                row.querySelectorAll('input, select, button').forEach(element => {
                    element.disabled = state;
                });
            }

            function setRowLoading(row, state) {
                if (!row) {
                    return;
                }

                if (state) {
                    row.classList.add('loading');
                } else {
                    row.classList.remove('loading');
                }

                row.querySelectorAll('input, button, select').forEach(element => {
                    element.disabled = state;
                });

                if (state && activeDetailActionsMenu) {
                    const menuRow = activeDetailActionsMenu.closest('tr');
                    if (menuRow === row) {
                        closeActiveDetailActionsMenu();
                    }
                }
            }

            function disableNotesAddRow(disabled) {
                if (notesAddScore) {
                    notesAddScore.disabled = disabled;
                }
                if (notesAddText) {
                    notesAddText.disabled = disabled;
                }
                if (notesAddButton) {
                    notesAddButton.disabled = disabled;
                }
            }

            function clearNotesAddInputs() {
                if (notesAddScore) {
                    notesAddScore.value = '';
                }
                if (notesAddText) {
                    notesAddText.value = '';
                }
            }

            function showNotesMessage(text, state) {
                if (!notesMessage) {
                    return;
                }

                if (!text) {
                    notesMessage.style.display = 'none';
                    notesMessage.textContent = '';
                    return;
                }

                notesMessage.dataset.state = state ?? 'info';
                notesMessage.textContent = text;
                notesMessage.style.display = 'block';
            }

            function setNotesHeader(summary, noteCount) {
                if (!notesTitle || !notesSubtitle) {
                    return;
                }

                if (!summary) {
                    notesTitle.textContent = 'Consumption Notes';
                    notesSubtitle.textContent = 'Select a bottle to view consumption notes.';
                    if (notesAddUserDisplay) {
                        notesAddUserDisplay.textContent = '—';
                        notesAddUserDisplay.dataset.userId = '';
                    }
                    return;
                }

                const wineName = summary.wineName ?? '';
                const vintage = summary.vintage ?? '';
                notesTitle.textContent = wineName && vintage ? `${wineName} • ${vintage}` : wineName || 'Consumption Notes';

                const parts = [];

                if (typeof noteCount === 'number') {
                    parts.push(`${noteCount} note${noteCount === 1 ? '' : 's'}`);
                }

                const owner = summary.userName ?? summary.UserName ?? '';
                const summaryUserId = summary.userId ?? summary.UserId ?? '';
                if (owner) {
                    parts.push(`Owner: ${owner}`);
                } else if (summaryUserId) {
                    parts.push('Owner: You');
                }

                const location = summary.bottleLocation ?? '';
                if (location) {
                    parts.push(`Location: ${location}`);
                }

                if (summary.isDrunk) {
                    const formatted = formatDateTime(summary.drunkAt ?? '')?.replace('T', ' ');
                    parts.push(formatted ? `Drunk at ${formatted}` : 'Drunk');
                }

                notesSubtitle.textContent = parts.length > 0 ? parts.join(' · ') : 'Consumption notes';

                if (notesAddUserDisplay) {
                    notesAddUserDisplay.dataset.userId = summaryUserId ? String(summaryUserId) : '';
                    if (owner) {
                        notesAddUserDisplay.textContent = owner;
                    } else if (summaryUserId) {
                        notesAddUserDisplay.textContent = 'You';
                    } else {
                        notesAddUserDisplay.textContent = '—';
                    }
                }
            }

            function setNotesLoading(state) {
                notesLoading = state;
                disableNotesAddRow(state || !notesSelectedBottleId);
            }

            function setNoteRowLoading(row, state) {
                if (!row) {
                    return;
                }

                if (state) {
                    row.classList.add('loading');
                } else {
                    row.classList.remove('loading');
                }

                row.querySelectorAll('button, select, textarea, input').forEach(element => {
                    element.disabled = state;
                });
            }

            function shouldIgnoreDetailRowClick(event) {
                const target = event.target;
                return Boolean(target && target.closest('button, select, input, textarea, label, a'));
            }

            async function handleDetailRowSelection(row, detail, summary) {
                if (!notesEnabled || !row || !detail) {
                    return;
                }

                await referenceDataPromise;
                await openNotesPanel(row, detail, summary);
            }

            async function openNotesPanel(row, detail, summary) {
                if (!notesEnabled || !detailsPanel || !notesPanel) {
                    return;
                }

                const bottleId = detail?.bottleId ?? detail?.BottleId ?? row?.dataset?.bottleId;
                if (!bottleId) {
                    return;
                }

                notesSelectedBottleId = bottleId;

                if (selectedDetailRowElement && selectedDetailRowElement !== row) {
                    selectedDetailRowElement.classList.remove('selected');
                }

                selectedDetailRowElement = row;
                row.classList.add('selected');

                detailsPanel.classList.add('notes-visible');
                notesPanel.setAttribute('aria-hidden', 'false');

                const headerSummary = {
                    wineName: summary?.wineName ?? summary?.WineName ?? '',
                    vintage: summary?.vintage ?? summary?.Vintage,
                    bottleLocation: detail?.bottleLocation ?? detail?.BottleLocation ?? '',
                    userId: detail?.userId ?? detail?.UserId ?? '',
                    userName: detail?.userName ?? detail?.UserName ?? '',
                    isDrunk: Boolean(detail?.isDrunk ?? detail?.IsDrunk),
                    drunkAt: detail?.drunkAt ?? detail?.DrunkAt ?? null
                };

                setNotesHeader(headerSummary);
                showNotesMessage('', 'info');
                clearNotesAddInputs();
                disableNotesAddRow(notesLoading);

                await loadNotesForBottle(bottleId);
            }

            async function loadNotesForBottle(bottleId) {
                if (!notesEnabled || !notesBody) {
                    return;
                }

                notesBody.querySelectorAll('.note-row').forEach(r => r.remove());
                if (notesEmptyRow) {
                    notesEmptyRow.hidden = false;
                }

                if (!bottleId) {
                    return;
                }

                try {
                    setNotesLoading(true);
                    showNotesMessage('Loading consumption notes…', 'info');
                    const response = await sendJson(`/wine-manager/bottles/${bottleId}/notes`, { method: 'GET' });
                    renderNotes(response);
                    showNotesMessage('', 'info');
                } catch (error) {
                    showNotesMessage(error.message, 'error');
                } finally {
                    setNotesLoading(false);
                }
            }

            function renderNotes(data) {
                if (!notesEnabled || !notesBody) {
                    return;
                }

                const rawBottle = data?.bottle ?? data?.Bottle ?? null;
                const summary = normalizeBottleNoteSummary(rawBottle);
                const rawNotes = Array.isArray(data?.notes)
                    ? data.notes
                    : Array.isArray(data?.Notes)
                        ? data.Notes
                        : [];
                const notes = rawNotes.map(normalizeNote).filter(Boolean);

                if (summary) {
                    setNotesHeader(summary, notes.length);
                    updateScoresFromNotesSummary(summary);
                } else if (!notesSelectedBottleId) {
                    setNotesHeader(null);
                }

                notesBody.querySelectorAll('.note-row').forEach(r => r.remove());

                if (notes.length === 0) {
                    if (notesEmptyRow) {
                        notesEmptyRow.hidden = false;
                    }
                    return;
                }

                if (notesEmptyRow) {
                    notesEmptyRow.hidden = true;
                }

                notes.forEach(note => {
                    const row = buildNoteRow(note);
                    notesBody.appendChild(row);
                });
            }

            function buildNoteRow(note) {
                const row = document.createElement('tr');
                row.className = 'note-row';
                row.dataset.noteId = note.id ?? note.Id ?? '';

                const rawScore = note.score ?? note.Score ?? null;
                const scoreValue = rawScore == null ? '' : String(rawScore);
                const scoreDisplayValue = formatScore(rawScore);
                const noteText = note.note ?? note.Note ?? '';
                const userId = note.userId ?? note.UserId ?? '';
                const userName = note.userName ?? note.UserName ?? '';
                const normalizedUserId = userId ? String(userId) : '';
                const currentUserId = notesAddUserDisplay?.dataset?.userId ?? '';
                let userLabel = userName;
                if (!userLabel) {
                    if (normalizedUserId && currentUserId && normalizedUserId === currentUserId) {
                        userLabel = 'You';
                    } else {
                        userLabel = '—';
                    }
                }

                row.dataset.userId = normalizedUserId;
                const canEdit = Boolean(normalizedUserId) && Boolean(currentUserId)
                    ? normalizedUserId === currentUserId
                    : false;

                if (!canEdit) {
                    row.classList.add('note-row--readonly');
                }

                const noteDisplayValue = noteText
                    ? escapeHtml(noteText).replace(/\r?\n/g, '<br />')
                    : '—';

                const scoreCellContent = canEdit
                    ? `<input type="number" class="note-score" min="0" max="10" step="0.1" value="${escapeHtml(scoreValue)}" placeholder="0-10" />`
                    : `<span class="note-score-display">${escapeHtml(scoreDisplayValue)}</span>`;

                const noteCellContent = canEdit
                    ? `<textarea class="note-text" rows="3">${escapeHtml(noteText)}</textarea>`
                    : `<div class="note-text-display">${noteDisplayValue}</div>`;

                const actionsCellContent = canEdit
                    ? `<button type="button" class="crud-table__action-button save-note">Save</button>
                        <button type="button" class="crud-table__action-button secondary delete-note">Delete</button>`
                    : `<span class="note-actions-readonly" aria-hidden="true">—</span>`;

                row.dataset.editable = canEdit ? 'true' : 'false';

                row.innerHTML = `
                    <td class="note-user"><span class="note-user-name">${escapeHtml(userLabel)}</span></td>
                    <td>${scoreCellContent}</td>
                    <td>${noteCellContent}</td>
                    <td class="actions">
                        ${actionsCellContent}
                    </td>`;

                if (canEdit) {
                    const scoreInput = row.querySelector('.note-score');
                    const noteTextarea = row.querySelector('.note-text');
                    const saveButton = row.querySelector('.save-note');
                    const deleteButton = row.querySelector('.delete-note');

                    saveButton?.addEventListener('click', async () => {
                        if (!notesSelectedBottleId || notesLoading) {
                            return;
                        }

                        const noteValue = noteTextarea?.value?.trim() ?? '';
                        if (!noteValue) {
                            showNotesMessage('Note text is required.', 'error');
                            return;
                        }

                        const parsedScore = parseScore(scoreInput?.value ?? '');
                        if (parsedScore === undefined) {
                            showNotesMessage('Score must be between 0 and 10.', 'error');
                            return;
                        }

                        const payload = {
                            note: noteValue,
                            score: parsedScore
                        };

                        try {
                            setNoteRowLoading(row, true);
                            const response = await sendJson(`/wine-manager/notes/${note.id ?? note.Id}`, {
                                method: 'PUT',
                                body: JSON.stringify(payload)
                            });
                            renderNotes(response);
                            showNotesMessage('Note updated.', 'success');
                        } catch (error) {
                            showNotesMessage(error.message, 'error');
                        } finally {
                            setNoteRowLoading(row, false);
                        }
                    });

                    deleteButton?.addEventListener('click', async () => {
                        if (!notesSelectedBottleId || notesLoading) {
                            return;
                        }

                        const confirmed = window.confirm('Delete this consumption note?');
                        if (!confirmed) {
                            return;
                        }

                        try {
                            setNoteRowLoading(row, true);
                            const response = await sendJson(`/wine-manager/notes/${note.id ?? note.Id}`, {
                                method: 'DELETE'
                            });
                            renderNotes(response);
                            showNotesMessage('Note deleted.', 'success');
                        } catch (error) {
                            showNotesMessage(error.message, 'error');
                        } finally {
                            setNoteRowLoading(row, false);
                        }
                    });
                }

                return row;
            }

            function closeNotesPanel() {
                if (!notesEnabled || !detailsPanel || !notesPanel) {
                    return;
                }

                if (selectedDetailRowElement) {
                    selectedDetailRowElement.classList.remove('selected');
                }

                selectedDetailRowElement = null;
                notesSelectedBottleId = null;
                detailsPanel.classList.remove('notes-visible');
                notesPanel.setAttribute('aria-hidden', 'true');

                if (notesBody) {
                    notesBody.querySelectorAll('.note-row').forEach(r => r.remove());
                }

                if (notesEmptyRow) {
                    notesEmptyRow.hidden = false;
                }

                clearNotesAddInputs();
                disableNotesAddRow(true);
                setNotesHeader(null);
                showNotesMessage('', 'info');
            }

            async function handleAddNote() {
                if (!notesEnabled || !notesSelectedBottleId || notesLoading) {
                    return;
                }

                const noteValue = notesAddText?.value?.trim() ?? '';
                if (!noteValue) {
                    showNotesMessage('Note text is required.', 'error');
                    return;
                }

                const parsedScore = parseScore(notesAddScore?.value ?? '');
                if (parsedScore === undefined) {
                    showNotesMessage('Score must be between 0 and 10.', 'error');
                    return;
                }

                const payload = {
                    bottleId: notesSelectedBottleId,
                    note: noteValue,
                    score: parsedScore
                };

                try {
                    setNotesLoading(true);
                    const response = await sendJson('/wine-manager/notes', {
                        method: 'POST',
                        body: JSON.stringify(payload)
                    });
                    renderNotes(response);
                    showNotesMessage('Note saved.', 'success');
                    clearNotesAddInputs();
                } catch (error) {
                    showNotesMessage(error.message, 'error');
                } finally {
                    setNotesLoading(false);
                }
            }

            function parseScore(value) {
                if (value == null || value === '') {
                    return null;
                }

                const parsed = Number.parseFloat(value);
                if (!Number.isFinite(parsed)) {
                    return undefined;
                }

                if (parsed < 0 || parsed > 10) {
                    return undefined;
                }

                return parsed;
            }

            function normalizeBottleNoteSummary(raw) {
                if (!raw) {
                    return null;
                }

                return {
                    bottleId: pick(raw, ['bottleId', 'BottleId']),
                    wineVintageId: pick(raw, ['wineVintageId', 'WineVintageId']),
                    wineName: pick(raw, ['wineName', 'WineName']) ?? '',
                    vintage: pick(raw, ['vintage', 'Vintage']),
                    bottleLocation: pick(raw, ['bottleLocation', 'BottleLocation']) ?? '',
                    userId: pick(raw, ['userId', 'UserId']) ?? '',
                    userName: pick(raw, ['userName', 'UserName']) ?? '',
                    isDrunk: Boolean(pick(raw, ['isDrunk', 'IsDrunk'])),
                    drunkAt: pick(raw, ['drunkAt', 'DrunkAt']),
                    bottleAverageScore: pick(raw, ['bottleAverageScore', 'BottleAverageScore']),
                    groupAverageScore: pick(raw, ['groupAverageScore', 'GroupAverageScore'])
                };
            }

            function updateScoresFromNotesSummary(summary) {
                if (!summary) {
                    return;
                }

                const bottleId = summary.bottleId ?? summary.BottleId ?? notesSelectedBottleId;
                const bottleAverage = summary.bottleAverageScore ?? summary.BottleAverageScore ?? null;
                if (bottleId) {
                    const detailRow = detailsBody.querySelector(`.detail-row[data-bottle-id="${bottleId}"]`);
                    const averageCell = detailRow?.querySelector('.detail-average');
                    if (averageCell) {
                        averageCell.textContent = formatScore(bottleAverage);
                    }
                }

                const groupId = summary.wineVintageId ?? summary.WineVintageId ?? selectedGroupId;
                const groupAverage = summary.groupAverageScore ?? summary.GroupAverageScore ?? null;
                if (groupId) {
                    const summaryRow = inventoryTable.querySelector(`.group-row[data-group-id="${groupId}"]`);
                    const scoreCell = summaryRow?.querySelector('[data-field="score"]');
                    if (scoreCell) {
                        scoreCell.textContent = formatScore(groupAverage);
                    }
                }

                if (selectedSummary) {
                    selectedSummary.averageScore = groupAverage ?? null;
                }
            }

            function updateDetailRowNote(bottleId, noteId, noteText, noteScore) {
                if (!detailsBody) {
                    return;
                }

                const row = detailsBody.querySelector(`.detail-row[data-bottle-id="${bottleId}"]`);
                if (!row) {
                    return;
                }

                if (noteId) {
                    row.dataset.noteId = String(noteId);
                } else {
                    delete row.dataset.noteId;
                }

                if (noteScore !== undefined && noteScore !== null) {
                    const numericScore = Number(noteScore);
                    row.dataset.noteScore = Number.isFinite(numericScore) ? String(numericScore) : '';
                } else {
                    delete row.dataset.noteScore;
                }

                const detailContext = row[DETAIL_ROW_DATA_KEY] ?? {};
                if (!row[DETAIL_ROW_DATA_KEY]) {
                    row[DETAIL_ROW_DATA_KEY] = detailContext;
                }

                detailContext.currentUserNoteId = noteId ? String(noteId) : null;
                detailContext.currentUserNote = typeof noteText === 'string'
                    ? noteText
                    : noteText != null
                        ? String(noteText)
                        : '';

                if (noteScore === undefined || noteScore === null) {
                    detailContext.currentUserScore = null;
                } else {
                    const parsedScore = Number(noteScore);
                    detailContext.currentUserScore = Number.isFinite(parsedScore) ? parsedScore : null;
                }

                if (!detailContext.bottleId) {
                    detailContext.bottleId = row.dataset.bottleId ?? '';
                }

                if (!detailContext.userId) {
                    detailContext.userId = row.dataset.userId ?? '';
                }
            }

            function normalizeNote(raw) {
                if (!raw) {
                    return null;
                }

                const rawScore = pick(raw, ['score', 'Score']);
                const normalizedScore = (() => {
                    if (rawScore == null || rawScore === '') {
                        return null;
                    }

                    const parsed = Number(rawScore);
                    return Number.isFinite(parsed) ? parsed : null;
                })();
                const rawUserId = pick(raw, ['userId', 'UserId']) ?? '';
                const normalizedUserId = rawUserId ? String(rawUserId) : '';

                return {
                    id: pick(raw, ['id', 'Id']),
                    note: pick(raw, ['note', 'Note']) ?? '',
                    score: normalizedScore,
                    userId: normalizedUserId,
                    userName: pick(raw, ['userName', 'UserName']) ?? ''
                };
            }

            function extractUserNoteFromNotesResponse(data, userId) {
                const normalizedUserId = userId ? String(userId) : '';
                const rawNotes = Array.isArray(data?.notes)
                    ? data.notes
                    : Array.isArray(data?.Notes)
                        ? data.Notes
                        : [];
                const notes = rawNotes.map(normalizeNote).filter(Boolean);

                if (normalizedUserId) {
                    const match = notes.find(note => {
                        const noteUserId = note?.userId ?? note?.UserId ?? '';
                        return noteUserId && String(noteUserId) === normalizedUserId;
                    });
                    if (match) {
                        return match;
                    }
                }

                return notes.length === 1 ? notes[0] : null;
            }

            function parsePrice(value) {
                if (!value) {
                    return null;
                }

                const parsed = Number.parseFloat(value);
                return Number.isFinite(parsed) ? parsed : null;
            }

            function parseDateOnly(value) {
                if (!value) {
                    return null;
                }

                const date = new Date(`${value}T00:00:00`);
                return Number.isNaN(date.getTime()) ? null : date.toISOString();
            }

            function parseDateTime(value) {
                if (!value) {
                    return null;
                }

                const date = new Date(value);
                return Number.isNaN(date.getTime()) ? null : date.toISOString();
            }

            function formatScore(value) {
                if (value == null || value === '') {
                    return '—';
                }

                const parsed = Number(value);
                return Number.isFinite(parsed) ? parsed.toFixed(1) : '—';
            }

            function formatDateDisplay(value) {
                if (!value) {
                    return '—';
                }

                const date = new Date(value);
                if (Number.isNaN(date.getTime())) {
                    return '—';
                }

                return date.toLocaleDateString(undefined, {
                    year: 'numeric',
                    month: 'short',
                    day: 'numeric'
                });
            }

            function formatDateTime(value) {
                if (!value) {
                    return '';
                }

                const date = new Date(value);
                if (Number.isNaN(date.getTime())) {
                    return '';
                }

                const year = date.getFullYear();
                const month = String(date.getMonth() + 1).padStart(2, '0');
                const day = String(date.getDate()).padStart(2, '0');
                const hours = String(date.getHours()).padStart(2, '0');
                const minutes = String(date.getMinutes()).padStart(2, '0');
                return `${year}-${month}-${day}T${hours}:${minutes}`;
            }

            function formatDateInputValue(value) {
                if (!value) {
                    return '';
                }

                const date = value instanceof Date ? value : new Date(value);
                if (Number.isNaN(date.getTime())) {
                    return '';
                }

                const year = date.getFullYear();
                const month = String(date.getMonth() + 1).padStart(2, '0');
                const day = String(date.getDate()).padStart(2, '0');
                return `${year}-${month}-${day}`;
            }

            function normalizeIsoDate(value) {
                if (!value) {
                    return '';
                }

                const date = value instanceof Date ? value : new Date(value);
                return Number.isNaN(date.getTime()) ? '' : date.toISOString();
            }

            function shortId(id) {
                if (!id) {
                    return '';
                }

                const value = String(id);
                return value.length > 8 ? `${value.substring(0, 8)}…` : value;
            }

            function showMessage(text, state) {
                if (!text) {
                    messageBanner.style.display = 'none';
                    messageBanner.textContent = '';
                    return;
                }

                messageBanner.dataset.state = state ?? 'info';
                messageBanner.textContent = text;
                messageBanner.style.display = 'block';
            }

            function buildAppellationDisplay(summary) {
                const subApp = summary?.subAppellation ?? summary?.SubAppellation;
                const appellation = summary?.appellation ?? summary?.Appellation;

                if (subApp && appellation && !equalsIgnoreCase(subApp, appellation)) {
                    return `${subApp} (${appellation})`;
                }

                if (subApp) {
                    return subApp;
                }

                if (appellation) {
                    return appellation;
                }

                return '—';
            }

            function equalsIgnoreCase(a, b) {
                if (a == null || b == null) {
                    return false;
                }

                return String(a).toLowerCase() === String(b).toLowerCase();
            }

            function pick(obj, keys) {
                for (const key of keys) {
                    if (obj && obj[key] !== undefined && obj[key] !== null) {
                        return obj[key];
                    }
                }

                return undefined;
            }

            function normalizeWineOption(raw) {
                if (!raw) {
                    return null;
                }

                const id = pick(raw, ['id', 'Id']);
                if (!id) {
                    return null;
                }

                const name = pick(raw, ['name', 'Name']) ?? '';
                const subAppellation = pick(raw, ['subAppellation', 'SubAppellation']);
                const appellation = pick(raw, ['appellation', 'Appellation']);
                const region = pick(raw, ['region', 'Region']);
                const country = pick(raw, ['country', 'Country']);
                const color = pick(raw, ['color', 'Color']) ?? '';
                const rawVintages = Array.isArray(raw?.vintages)
                    ? raw.vintages
                    : Array.isArray(raw?.Vintages)
                        ? raw.Vintages
                        : [];
                const vintages = rawVintages
                    .map((value) => Number(value))
                    .filter((value) => Number.isInteger(value))
                    .sort((a, b) => b - a);

                const locationParts = [];
                if (subAppellation) {
                    locationParts.push(subAppellation);
                }
                if (appellation && !equalsIgnoreCase(appellation, subAppellation)) {
                    locationParts.push(appellation);
                }
                if (region) {
                    locationParts.push(region);
                }
                if (country) {
                    locationParts.push(country);
                }

                const labelParts = [name];
                if (locationParts.length > 0) {
                    labelParts.push(`(${locationParts.join(' • ')})`);
                }

                return {
                    id: String(id),
                    name,
                    color,
                    subAppellation,
                    appellation,
                    region,
                    country,
                    vintages,
                    label: labelParts.join(' ')
                };
            }

            function normalizeWineSurferResult(raw) {
                if (!raw) {
                    return null;
                }

                const nameValue = pick(raw, ['name', 'Name', 'label', 'Label']);
                if (!nameValue) {
                    return null;
                }

                const name = String(nameValue).trim();
                if (!name) {
                    return null;
                }

                const countryValue = pick(raw, ['country', 'Country']);
                const regionValue = pick(raw, ['region', 'Region']);
                const appellationValue = pick(raw, ['appellation', 'Appellation']);
                const subAppellationValue = pick(raw, ['subAppellation', 'SubAppellation', 'sub_appellation', 'Sub_Appellation']);
                const colorValue = pick(raw, ['color', 'Color']);

                const country = typeof countryValue === 'string' ? countryValue.trim() : countryValue != null ? String(countryValue).trim() : '';
                const region = typeof regionValue === 'string' ? regionValue.trim() : regionValue != null ? String(regionValue).trim() : '';
                const appellation = typeof appellationValue === 'string' ? appellationValue.trim() : appellationValue != null ? String(appellationValue).trim() : '';
                const subAppellation = typeof subAppellationValue === 'string' ? subAppellationValue.trim() : subAppellationValue != null ? String(subAppellationValue).trim() : '';
                const color = typeof colorValue === 'string' ? colorValue.trim() : colorValue != null ? String(colorValue).trim() : '';

                return {
                    name,
                    country: country || null,
                    region: region || null,
                    appellation: appellation || null,
                    subAppellation: subAppellation || null,
                    color: color || null
                };
            }

            function createCreateWineOption(query) {
                const trimmed = (query ?? '').trim();
                const hasQuery = trimmed.length > 0;
                return {
                    id: '__create_wine__',
                    name: 'Create wine',
                    label: hasQuery ? `Create “${trimmed}”` : 'Create wine',
                    isCreateWine: true,
                    isAction: true,
                    query: trimmed
                };
            }

            function appendActionOptions(options, query, { includeCreate } = {}) {
                const baseOptions = Array.isArray(options)
                    ? options.filter(option => option && option.isWineSurfer !== true && option.isCreateWine !== true)
                    : [];
                const trimmed = (query ?? '').trim();
                const hasQuery = trimmed.length > 0;
                const shouldIncludeCreate = includeCreate ?? hasQuery;
                const canAppendCreate = hasQuery || includeCreate === true;

                if (shouldIncludeCreate && canAppendCreate) {
                    baseOptions.push(createCreateWineOption(trimmed));
                }

                return baseOptions;
            }

            function normalizeSummary(raw) {
                if (!raw) {
                    return null;
                }

                return {
                    wineVintageId: pick(raw, ['wineVintageId', 'WineVintageId']),
                    wineId: pick(raw, ['wineId', 'WineId']),
                    wineName: pick(raw, ['wineName', 'WineName']) ?? '',
                    subAppellation: pick(raw, ['subAppellation', 'SubAppellation']),
                    appellation: pick(raw, ['appellation', 'Appellation']),
                    subAppellationId: pick(raw, ['subAppellationId', 'SubAppellationId']),
                    appellationId: pick(raw, ['appellationId', 'AppellationId']),
                    vintage: pick(raw, ['vintage', 'Vintage']),
                    bottleCount: Number(pick(raw, ['bottleCount', 'BottleCount']) ?? 0),
                    statusLabel: pick(raw, ['statusLabel', 'StatusLabel']) ?? '',
                    statusCssClass: pick(raw, ['statusCssClass', 'StatusCssClass']) ?? '',
                    averageScore: pick(raw, ['averageScore', 'AverageScore']),
                    color: pick(raw, ['color', 'Color']) ?? ''
                };
            }

            function normalizeDetail(raw) {
                if (!raw) {
                    return null;
                }

                const rawBottleId = pick(raw, ['bottleId', 'BottleId']);
                const rawUserId = pick(raw, ['userId', 'UserId']);
                const rawNoteId = pick(raw, ['currentUserNoteId', 'CurrentUserNoteId']);
                const rawNoteText = pick(raw, ['currentUserNote', 'CurrentUserNote']);
                const rawNoteScore = pick(raw, ['currentUserScore', 'CurrentUserScore']);
                const normalizedNoteScore = (() => {
                    if (rawNoteScore == null || rawNoteScore === '') {
                        return null;
                    }

                    const parsed = Number(rawNoteScore);
                    return Number.isFinite(parsed) ? parsed : null;
                })();

                return {
                    bottleId: rawBottleId ? String(rawBottleId) : '',
                    price: pick(raw, ['price', 'Price']),
                    isDrunk: Boolean(pick(raw, ['isDrunk', 'IsDrunk'])),
                    drunkAt: pick(raw, ['drunkAt', 'DrunkAt']),
                    bottleLocationId: pick(raw, ['bottleLocationId', 'BottleLocationId']),
                    bottleLocation: pick(raw, ['bottleLocation', 'BottleLocation']),
                    userId: rawUserId ? String(rawUserId) : '',
                    userName: pick(raw, ['userName', 'UserName']) ?? '',
                    vintage: pick(raw, ['vintage', 'Vintage']),
                    wineName: pick(raw, ['wineName', 'WineName']),
                    averageScore: pick(raw, ['averageScore', 'AverageScore']),
                    currentUserNoteId: rawNoteId ? String(rawNoteId) : null,
                    currentUserNote: typeof rawNoteText === 'string'
                        ? rawNoteText
                        : rawNoteText != null
                            ? String(rawNoteText)
                            : '',
                    currentUserScore: normalizedNoteScore,
                    wineVintageId: pick(raw, ['wineVintageId', 'WineVintageId'])
                };
            }

            async function sendJson(url, options) {
                const requestInit = {
                    headers: {
                        'Content-Type': 'application/json'
                    },
                    credentials: 'same-origin',
                    ...options
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

            function escapeHtml(value) {
                if (value == null) {
                    return '';
                }

                return String(value)
                    .replace(/&/g, '&amp;')
                    .replace(/</g, '&lt;')
                    .replace(/>/g, '&gt;')
                    .replace(/"/g, '&quot;')
                    .replace(/'/g, '&#39;');
            }

            window.WineInventoryTables.hideDetailsPanel = function () {
                showInventoryView();
                resetDetailsView();
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
