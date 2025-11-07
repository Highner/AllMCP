(function () {
    let initialized = false;
    let openModalHandler = null;
    let closeModalHandler = null;
    let lastOpenContext = null;

    function onReady(callback) {
        if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', callback, { once: true });
        } else {
            callback();
        }
    }

    function toTrimmedString(value) {
        if (typeof value === 'string') {
            return value.trim();
        }

        if (value === null || value === undefined) {
            return '';
        }

        return String(value).trim();
    }

    function createComparisonKey(value) {
        const text = toTrimmedString(value);
        if (!text) {
            return '';
        }

        let normalized = text;
        if (typeof normalized.normalize === 'function') {
            normalized = normalized
                .normalize('NFKD')
                .replace(/[\u0300-\u036f]/g, '');
        }

        return normalized
            .toLowerCase()
            .replace(/[^a-z0-9]+/g, '');
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
        const color = pick(raw, ['color', 'Color']);
        const vintages = Array.isArray(raw?.vintages ?? raw?.Vintages)
            ? (raw?.vintages ?? raw?.Vintages)
            : [];
        const label = pick(raw, ['label', 'Label']) ?? name;
        const searchKey = createComparisonKey(label || name);
        const regionKeys = [
            createComparisonKey(subAppellation),
            createComparisonKey(appellation),
            createComparisonKey(region),
            createComparisonKey(country)
        ].filter(Boolean);

        return {
            id,
            name,
            subAppellation,
            appellation,
            region,
            country,
            color,
            vintages,
            label,
            searchKey,
            regionKeys
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
            ? options.filter(option => option && option.isCreateWine !== true)
            : [];
        const trimmed = (query ?? '').trim();
        const shouldIncludeCreate = includeCreate ?? (trimmed.length >= 3);

        if (shouldIncludeCreate) {
            baseOptions.push(createCreateWineOption(trimmed));
        }
        return baseOptions;
    }

    function pick(obj, keys) {
        for (const key of keys) {
            if (obj && obj[key] !== undefined && obj[key] !== null) {
                return obj[key];
            }
        }

        return undefined;
    }

    async function sendJson(url, options) {
        const requestInit = {
            headers: {
                'Content-Type': 'application/json',
                'Accept': 'application/json'
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

    function initializeFavoritesModal() {
        const overlay = document.getElementById('inventory-add-overlay');
        const popover = document.getElementById('inventory-add-popover');
        if (!overlay || !popover) {
            return;
        }

        if (initialized) {
            return;
        }

        initialized = true;

        const form = popover.querySelector('.inventory-add-form');
        const wineSearch = popover.querySelector('.inventory-add-wine-search');
        const wineIdInput = popover.querySelector('.inventory-add-wine-id');
        const wineResults = popover.querySelector('.inventory-add-wine-results');
        const wineCombobox = popover.querySelector('.inventory-add-combobox');
        const vintage = popover.querySelector('.inventory-add-vintage');
        const locationSelect = popover.querySelector('.inventory-add-location');
        const quantity = popover.querySelector('.inventory-add-quantity');
        const pendingDeliveryCheckbox = popover.querySelector('.inventory-add-pending-delivery');
        const summary = popover.querySelector('.inventory-add-summary');
        const hint = popover.querySelector('.inventory-add-vintage-hint');
        const error = popover.querySelector('.inventory-add-error');
        const submit = popover.querySelector('.inventory-add-submit');
        const cancel = popover.querySelector('.inventory-add-cancel');
        const headerClose = popover.querySelector('[data-add-wine-close]');
        const wineSurferOverlay = document.getElementById('inventory-wine-surfer-overlay');
        const wineSurferPopover = document.getElementById('inventory-wine-surfer-popover');
        const wineSurferClose = wineSurferPopover?.querySelector('[data-wine-surfer-close]');
        const wineSurferStatus = wineSurferPopover?.querySelector('.inventory-wine-surfer-status');
        const wineSurferList = wineSurferPopover?.querySelector('.inventory-wine-surfer-list');
        const wineSurferIntro = wineSurferPopover?.querySelector('.inventory-wine-surfer-intro');
        const wineSurferQueryLabel = wineSurferPopover?.querySelector('.inventory-wine-surfer-query');
        const statusMessage = document.querySelector('[data-favorite-message]');
        const inventoryStatusStorageKey = 'wine-inventory:last-status';

        let persistedInventoryStatus = null;
        try {
            persistedInventoryStatus = sessionStorage.getItem(inventoryStatusStorageKey);
            if (persistedInventoryStatus) {
                sessionStorage.removeItem(inventoryStatusStorageKey);
            }
        } catch {
            persistedInventoryStatus = null;
        }

        if (persistedInventoryStatus && statusMessage) {
            try {
                const parsedStatus = JSON.parse(persistedInventoryStatus);
                if (parsedStatus && parsedStatus.message) {
                    showStatus(parsedStatus.message, parsedStatus.state ?? 'success');
                }
            } catch {
                showStatus(persistedInventoryStatus, 'success');
            }
        }

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
        let bottleLocations = [];
        let bottleLocationsPromise = null;
        let modalLoading = false;

        const triggerSelector = '[data-add-wine-trigger]';

        const normalizeContext = (context) => {
            if (!context) {
                return null;
            }

            return {
                source: toTrimmedString(context.source),
                rowId: toTrimmedString(context.rowId),
                id: toTrimmedString(context.id),
                name: toTrimmedString(context.name),
                producer: toTrimmedString(context.producer),
                country: toTrimmedString(context.country),
                region: toTrimmedString(context.region),
                appellation: toTrimmedString(context.appellation),
                subAppellation: toTrimmedString(context.subAppellation),
                color: toTrimmedString(context.color),
                variety: toTrimmedString(context.variety),
                vintage: toTrimmedString(context.vintage)
            };
        };

        const handleTriggerClick = (event) => {
            const trigger = event.target.closest(triggerSelector);
            if (!trigger) {
                return;
            }

            event.preventDefault();
        const context = buildContextFromTrigger(trigger);

        if (context?.source === 'import-preview') {
            openCreateWineFromImport(trigger, context);
            return;
        }

        openModalWithStatus(context).catch(() => { /* handled via showStatus */ });
    };

    document.addEventListener('click', handleTriggerClick);

    function buildContextFromTrigger(trigger) {
        if (!trigger || !trigger.dataset) {
            return null;
        }

            const dataset = trigger.dataset;
            return normalizeContext({
                source: dataset.addWineTrigger,
                rowId: dataset.previewRowId,
                id: dataset.wineId,
                name: dataset.wineName,
                producer: dataset.wineProducer,
                country: dataset.wineCountry,
                region: dataset.wineRegion,
                appellation: dataset.wineAppellation,
                subAppellation: dataset.wineSubAppellation,
                color: dataset.wineColor,
                variety: dataset.wineVariety,
                vintage: dataset.wineVintage
            });
        }

        function openCreateWineFromImport(trigger, context) {
            const module = window.WineCreatePopover;
            if (!module || typeof module.open !== 'function') {
                showStatus('Unable to open the create wine dialog right now.', 'error');
                return;
            }

            const options = {
                initialName: context?.name ?? '',
                initialColor: context?.color ?? '',
                initialCountry: context?.country ?? '',
                initialRegion: context?.region ?? '',
                initialAppellation: context?.appellation ?? '',
                initialSubAppellation: context?.subAppellation ?? '',
                parentDialog: null,
                triggerElement: trigger instanceof HTMLElement ? trigger : null,
                onSuccess: (response) => {
                    const detail = {
                        rowId: context?.rowId ?? '',
                        context,
                        wine: response ?? null
                    };
                    document.dispatchEvent(new CustomEvent('wineImportPreview:wineCreated', { detail }));
                }
            };

            const opened = module.open(options);
            if (!opened) {
                showStatus('Unable to open the create wine dialog right now.', 'error');
            }
        }

        const bindClose = (element, reason = 'dismissed') => {
            if (!element) {
                return;
            }

            element.addEventListener('click', () => {
                closeModal(reason);
            });
        };

        bindClose(cancel, 'cancel');
        bindClose(headerClose, 'close');

        const bindWineSurferClose = (element) => {
            if (!element) {
                return;
            }

            element.addEventListener('click', () => {
                closeWineSurferPopover();
            });
        };

        bindWineSurferClose(wineSurferClose);

        overlay.addEventListener('click', event => {
            if (event.target === overlay) {
                closeModal('backdrop');
            }
        });

        wineSurferOverlay?.addEventListener('click', event => {
            if (event.target === wineSurferOverlay) {
                closeWineSurferPopover();
            }
        });

        document.addEventListener('keydown', event => {
            if (event.key !== 'Escape') {
                return;
            }

            if (wineSurferOverlay && !wineSurferOverlay.hidden) {
                event.preventDefault();
                closeWineSurferPopover();
                return;
            }

            if (!overlay.hidden) {
                event.preventDefault();
                closeModal('escape');
            }
        });

        locationSelect?.addEventListener('change', () => {
            showError('');
        });

        form?.addEventListener('submit', handleSubmit);
        wineSearch?.addEventListener('input', handleWineSearchInput);
        wineSearch?.addEventListener('keydown', handleWineSearchKeyDown);
        wineSearch?.addEventListener('focus', handleWineSearchFocus);
        document.addEventListener('pointerdown', handleWinePointerDown);

        function showStatus(message, state) {
            if (!statusMessage) {
                return;
            }

            const text = message ?? '';
            statusMessage.textContent = text;
            statusMessage.dataset.state = state ?? 'info';
            statusMessage.hidden = text.trim().length === 0;
        }

        function showError(message) {
            if (!error) {
                return;
            }

            const text = message ?? '';
            error.textContent = text;
            error.setAttribute('aria-hidden', text ? 'false' : 'true');
        }

        function setModalLoading(state) {
            modalLoading = state;
            if (submit) {
                submit.disabled = state;
            }
            if (wineSearch) {
                wineSearch.disabled = state;
                if (state) {
                    wineSearch.setAttribute('aria-busy', 'true');
                    cancelWineSearchTimeout();
                    abortWineSearchRequest();
                    closeWineResults();
                } else {
                    wineSearch.removeAttribute('aria-busy');
                }
            }
            if (vintage) {
                vintage.disabled = state;
            }
            if (locationSelect) {
                locationSelect.disabled = state;
            }
            if (quantity) {
                quantity.disabled = state;
            }
            if (pendingDeliveryCheckbox) {
                pendingDeliveryCheckbox.disabled = state;
            }
        }

        async function openModal(context = null) {
            showError('');
            showStatus('', 'info');
            overlay.hidden = false;
            overlay.setAttribute('aria-hidden', 'false');
            overlay.classList.add('is-open');
            document.body.style.overflow = 'hidden';

            const normalizedContext = normalizeContext(context);
            lastOpenContext = normalizedContext;
            resetWineSelection();
            closeWineSurferPopover({ restoreFocus: false });

            setModalLoading(true);
            if (pendingDeliveryCheckbox) {
                pendingDeliveryCheckbox.checked = false;
            }
            let matchedOption = null;
            let normalizedVintage = '';

            try {
                await ensureBottleLocations();
                populateLocationSelect('');
                if (locationSelect) {
                    locationSelect.value = '';
                }

                normalizedVintage = normalizeVintageValue(normalizedContext?.vintage);
                if (vintage) {
                    vintage.value = normalizedVintage;
                }
                if (quantity) {
                    quantity.value = '1';
                }

                const contextName = normalizedContext?.name ?? '';
                if (contextName) {
                    if (wineSearch) {
                        wineSearch.value = contextName;
                    }
                    currentWineQuery = contextName.trim();
                    if (currentWineQuery.length >= 3) {
                        await performWineSearch(currentWineQuery);
                        matchedOption = findBestWineOptionMatch(normalizedContext);
                        if (matchedOption) {
                            setSelectedWine(matchedOption);
                            closeWineResults();
                        }
                    } else {
                        renderWineSearchResults();
                    }
                }

                if (!matchedOption) {
                    setSelectedWine(null, { preserveSearchValue: true });
                }

                if (normalizedContext?.source === 'surf-eye') {
                    let statusText = '';
                    if (matchedOption) {
                        const label = matchedOption.label ?? matchedOption.name ?? 'this wine';
                        statusText = `Surf Eye matched ${label}. Confirm the details before adding.`;
                    } else if (normalizedContext?.name) {
                        statusText = `Surf Eye spotted "${normalizedContext.name}". Select the matching wine to add it to your cellar.`;
                    }

                    const originalVintage = normalizedContext?.vintage ?? '';
                    if (originalVintage && !normalizedVintage) {
                        const vintageReminder = `Enter the correct vintage (Surf Eye suggested ${originalVintage}).`;
                        statusText = statusText ? `${statusText} ${vintageReminder}` : vintageReminder;
                    }

                    if (statusText) {
                        showStatus(statusText, 'info');
                    }
                }
            } catch (error) {
                showError(error?.message ?? 'Unable to load wines.');
                closeModal('error');
                throw error;
            } finally {
                setModalLoading(false);
            }

            wineSearch?.focus();
        }

        const openModalWithStatus = async (context = null) => {
            try {
                await openModal(context);
            } catch (error) {
                showStatus(error?.message ?? 'Unable to open add wine modal.', 'error');
                throw error;
            }
        };

        const handleProgrammaticTrigger = (event) => {
            openModalWithStatus(event?.detail ?? null).catch(() => { /* handled via showStatus */ });
        };

        document.addEventListener('wineSurfer:addWine', handleProgrammaticTrigger);

        openModalHandler = openModalWithStatus;
        closeModalHandler = closeModal;

        function findBestWineOptionMatch(context) {
            if (!context) {
                return null;
            }

            if (context.id) {
                const matchById = wineOptions.find(option => option && option.id === context.id);
                if (matchById) {
                    return matchById;
                }
            }

            const nameKey = createComparisonKey(context.name);
            const regionKeys = createContextRegionKeys(context);

            let candidates = [];
            if (nameKey) {
                candidates = wineOptions.filter(option => {
                    if (!option || !option.id) {
                        return false;
                    }

                    const optionKey = option.searchKey ?? createComparisonKey(option.name ?? option.label);
                    return optionKey === nameKey;
                });
            }

            if (candidates.length === 1) {
                return candidates[0];
            }

            if (regionKeys.length > 0) {
                const scopedOptions = candidates.length > 0 ? candidates : wineOptions;
                const regionMatches = filterMatchesByRegion(scopedOptions, regionKeys);
                if (regionMatches.length === 1) {
                    return regionMatches[0];
                }
                if (regionMatches.length > 1) {
                    return regionMatches[0];
                }
            }

            if (candidates.length > 0) {
                return candidates[0];
            }

            return null;
        }

        function filterMatchesByRegion(options, regionKeys) {
            if (!Array.isArray(options) || !Array.isArray(regionKeys) || regionKeys.length === 0) {
                return [];
            }

            return options.filter(option => matchesRegionKey(option, regionKeys));
        }

        function matchesRegionKey(option, regionKeys) {
            if (!option || !Array.isArray(regionKeys) || regionKeys.length === 0) {
                return false;
            }

            const keys = Array.isArray(option.regionKeys) && option.regionKeys.length > 0
                ? option.regionKeys
                : [
                    createComparisonKey(option.subAppellation),
                    createComparisonKey(option.appellation),
                    createComparisonKey(option.region),
                    createComparisonKey(option.country)
                ].filter(Boolean);

            if (keys.length === 0) {
                return false;
            }

            return regionKeys.some(regionKey =>
                keys.some(key => key === regionKey || key.includes(regionKey) || regionKey.includes(key))
            );
        }

        function createContextRegionKeys(context) {
            const keys = [
                createComparisonKey(context.subAppellation),
                createComparisonKey(context.appellation),
                createComparisonKey(context.region),
                createComparisonKey(context.country)
            ].filter(Boolean);

            return Array.from(new Set(keys));
        }

        function normalizeVintageValue(value) {
            const trimmed = toTrimmedString(value);
            if (!trimmed) {
                return '';
            }

            const parsed = Number.parseInt(trimmed, 10);
            if (Number.isNaN(parsed)) {
                return '';
            }

            if (parsed < 1900 || parsed > 2100) {
                return '';
            }

            return String(parsed);
        }

        function closeModal(reason = 'dismissed') {
            const closedContext = lastOpenContext;
            setModalLoading(false);
            closeWineSurferPopover({ restoreFocus: false });
            overlay.classList.remove('is-open');
            overlay.setAttribute('aria-hidden', 'true');
            overlay.hidden = true;
            document.body.style.overflow = '';
            showError('');
            resetWineSelection();
            lastOpenContext = null;
            if (vintage) {
                vintage.value = '';
            }
            if (locationSelect) {
                locationSelect.value = '';
            }
            if (quantity) {
                quantity.value = '1';
            }
            if (pendingDeliveryCheckbox) {
                pendingDeliveryCheckbox.checked = false;
            }

            document.dispatchEvent(new CustomEvent('inventoryAddModal:closed', {
                detail: {
                    reason,
                    context: closedContext
                }
            }));
        }

        async function handleSubmit(event) {
            event.preventDefault();
            if (modalLoading) {
                return;
            }

            const wineId = wineIdInput?.value ?? '';
            const vintageValue = Number(vintage?.value ?? '');
            const quantityValue = Number(quantity?.value ?? '1');
            const locationValue = locationSelect?.value ?? '';
            const pendingDeliveryValue = Boolean(pendingDeliveryCheckbox?.checked);

            if (!wineId) {
                showError('Select a wine to add to your inventory.');
                wineSearch?.focus();
                return;
            }

            if (!Number.isInteger(vintageValue)) {
                showError('Enter a valid vintage year.');
                vintage?.focus();
                return;
            }

            if (!Number.isInteger(quantityValue) || quantityValue < 1 || quantityValue > 12) {
                showError('Select how many bottles to add.');
                quantity?.focus();
                return;
            }

            showError('');

            try {
                setModalLoading(true);
                if (lastOpenContext?.mode === 'wine-wizard') {
                    const detail = {
                        context: lastOpenContext,
                        wineId,
                        vintage: vintageValue,
                        quantity: quantityValue,
                        bottleLocationId: locationValue || null,
                        pendingDelivery: pendingDeliveryValue
                    };

                    document.dispatchEvent(new CustomEvent('inventoryAddModal:wizardSelection', { detail }));
                    closeModal('wizard');
                    return;
                }

                const result = await sendJson('/wine-manager/inventory', {
                    method: 'POST',
                    body: JSON.stringify({
                        wineId,
                        vintage: vintageValue,
                        quantity: quantityValue,
                        bottleLocationId: locationValue || null,
                        pendingDelivery: pendingDeliveryValue
                    })
                });
                const message = quantityValue === 1
                    ? 'Bottle added to your inventory.'
                    : `${quantityValue} bottles added to your inventory.`;

                document.dispatchEvent(new CustomEvent('inventoryAddModal:submitted', {
                    detail: {
                        context: lastOpenContext,
                        wineId,
                        vintage: vintageValue,
                        quantity: quantityValue,
                        bottleLocationId: locationValue || null,
                        pendingDelivery: pendingDeliveryValue,
                        message,
                        result
                    }
                }));

                const isInventoryView = document.getElementById('inventory-table') instanceof HTMLTableElement;
                if (isInventoryView) {
                    try {
                        sessionStorage.setItem(inventoryStatusStorageKey, JSON.stringify({ message, state: 'success' }));
                    } catch {
                        try {
                            sessionStorage.removeItem(inventoryStatusStorageKey);
                        } catch {
                            // Ignore storage cleanup errors
                        }
                    }

                    closeModal('submit');
                    window.location.reload();
                    return;
                }

                closeModal('submit');
                showStatus(message, 'success');
            } catch (error) {
                showError(error?.message ?? 'Unable to add wine to your inventory.');
            } finally {
                setModalLoading(false);
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
                        wineSurferStatus.textContent = 'Wine Surfer is searching…';
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

        function handleWineSurferSelection(result) {
            if (!result || wineSurferSelectionPending) {
                return;
            }

            wineSurferSelectionPending = false;
            wineSurferError = 'Wine Surfer can no longer add wines automatically. Please create the wine manually.';
            renderWineSurferResults();

            const query = (result.query ?? result.name ?? wineSurferActiveQuery ?? '').trim();
            closeWineSurferPopover({ restoreFocus: false });
            openCreateWinePopover(query);
        }

        function closeWineSurferPopover({ restoreFocus = true } = {}) {
            cancelWineSurferRequest();
            wineSurferLoading = false;
            wineSurferError = '';
            wineSurferResults = [];
            wineSurferActiveQuery = '';
            wineSurferSelectionPending = false;

            if (wineSurferOverlay) {
                wineSurferOverlay.classList.remove('is-open');
                wineSurferOverlay.setAttribute('aria-hidden', 'true');
                wineSurferOverlay.hidden = true;
            }

            renderWineSurferResults();

            if (restoreFocus && wineSearch && (!wineSurferOverlay || wineSurferOverlay.hidden)) {
                wineSearch.focus();
            }
        }

        function openWineSurferPopover(query) {
            if (!wineSurferOverlay || !wineSurferPopover) {
                return;
            }

            const trimmedQuery = (query ?? '').trim();
            if (!trimmedQuery) {
                showError('Enter a wine name before using Wine Surfer.');
                return;
            }

            wineSurferOverlay.hidden = false;
            wineSurferOverlay.setAttribute('aria-hidden', 'false');
            wineSurferOverlay.classList.add('is-open');

            cancelWineSurferRequest();
            wineSurferController = null;
            wineSurferLoading = false;
            wineSurferSelectionPending = false;
            wineSurferError = 'Wine Surfer search is no longer available.';
            wineSurferResults = [];
            wineSurferActiveQuery = trimmedQuery;
            renderWineSurferResults();

            if (wineSurferClose) {
                wineSurferClose.focus();
            }
        }

        function openCreateWinePopover(query) {
            const module = window.WineCreatePopover;
            if (!module || typeof module.open !== 'function') {
                showError('Unable to open the create wine dialog right now.');
                return;
            }

            const initialName = (query ?? '').trim()
                || currentWineQuery
                || wineSearch?.value
                || '';

            module.open({
                initialName,
                parentDialog: wineSurferPopover ?? null,
                triggerElement: wineSearch ?? null,
                onSuccess: (response) => {
                    const option = normalizeWineOption(response);
                    if (!option) {
                        showError('Wine was created, but it could not be selected automatically.');
                        return;
                    }

                    wineOptions = appendActionOptions([option], option.name, { includeCreate: false });
                    lastCompletedQuery = option.name;
                    setSelectedWine(option);
                    showError('');
                    closeWineResults();
                    wineSearch?.focus();
                },
                onCancel: () => {
                    wineSearch?.focus();
                }
            });
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
            if (wineIdInput) {
                wineIdInput.value = '';
            }
            if (wineSearch) {
                wineSearch.value = '';
                wineSearch.setAttribute('aria-expanded', 'false');
                wineSearch.removeAttribute('aria-activedescendant');
            }
            if (wineResults) {
                wineResults.innerHTML = '';
                wineResults.dataset.visible = 'false';
                wineResults.setAttribute('hidden', '');
            }
            updateSummary(null);
            updateHint(null);
        }

        function scheduleWineSearch(query) {
            cancelWineSearchTimeout();
            wineSearchTimeoutId = window.setTimeout(() => {
                performWineSearch(query).catch(() => {
                    /* handled via render state */
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
                const data = await sendJson(`/wine-manager/wines?search=${encodeURIComponent(query)}`, {
                    method: 'GET',
                    signal: controller.signal
                });
                if (wineSearchController !== controller) {
                    return;
                }

                const items = Array.isArray(data) ? data : [];
                const normalized = items
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
            if (!wineResults) {
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
            wineResults.innerHTML = '';

            if (wineSearchTimeoutId !== null || wineSearchLoading) {
                wineResults.appendChild(buildWineStatusElement('Searching…'));
                return;
            }

            const hasCreateOption = wineOptions.some(option => option?.isCreateWine);
            const cellarOptions = wineOptions.filter(option => option && !option.isCreateWine);
            const hasCellarOptions = cellarOptions.length > 0;

            if (wineSearchError) {
                const errorStatus = buildWineStatusElement(wineSearchError);
                errorStatus.classList.add('inventory-add-wine-result-status--error');
                wineResults.appendChild(errorStatus);
            } else if (!hasCellarOptions) {
                if (trimmedQuery === lastCompletedQuery) {
                    const message = hasCreateOption
                        ? 'No wines found in your cellar. Create a new wine.'
                        : 'No wines found.';
                    wineResults.appendChild(buildWineStatusElement(message));
                } else {
                    wineResults.appendChild(buildWineStatusElement('Keep typing to search the cellar.'));
                }
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

                if (!option.isCreateWine && option.id) {
                    element.dataset.wineId = option.id;
                }

                if (option.isCreateWine) {
                    element.classList.add('inventory-add-wine-option--action');
                    element.classList.add('create-wine-option--action');
                }

                const optionId = option.isCreateWine
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

            wineResults.appendChild(list);
            highlightActiveWineOption();
        }

        function buildWineStatusElement(text) {
            const status = document.createElement('div');
            status.className = 'inventory-add-wine-result-status';
            status.textContent = text;
            status.setAttribute('role', 'status');
            return status;
        }

        function setWineResultsVisibility(visible) {
            if (!wineResults || !wineSearch) {
                return;
            }

            if (visible) {
                wineResults.dataset.visible = 'true';
                wineResults.removeAttribute('hidden');
                wineSearch.setAttribute('aria-expanded', 'true');
            } else {
                wineResults.dataset.visible = 'false';
                wineResults.setAttribute('hidden', '');
                wineSearch.setAttribute('aria-expanded', 'false');
                wineSearch.removeAttribute('aria-activedescendant');
            }
        }

        function closeWineResults() {
            activeWineOptionIndex = -1;
            setWineResultsVisibility(false);
            if (wineResults) {
                wineResults.innerHTML = '';
            }
        }

        function highlightActiveWineOption() {
            if (!wineResults || !wineSearch) {
                return;
            }

            const options = Array.from(wineResults.querySelectorAll('.inventory-add-wine-option'));
            options.forEach((element, index) => {
                const isActive = index === activeWineOptionIndex;
                element.classList.toggle('is-active', isActive);
                element.setAttribute('aria-selected', isActive ? 'true' : 'false');
            });

            const activeElement = options[activeWineOptionIndex];
            if (activeElement) {
                wineSearch.setAttribute('aria-activedescendant', activeElement.id);
                activeElement.scrollIntoView({ block: 'nearest' });
            } else {
                wineSearch.removeAttribute('aria-activedescendant');
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

            if (wineResults?.dataset?.visible !== 'true') {
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

            if (option.isCreateWine) {
                closeWineResults();
                const queryValue = (option.query ?? currentWineQuery ?? wineSearch?.value ?? '').trim();
                openCreateWinePopover(queryValue);
                return;
            }

            setSelectedWine(option);
            showError('');
            closeWineResults();
        }

        function setSelectedWine(option, { preserveSearchValue = false } = {}) {
            const isActionOption = option?.isCreateWine === true;
            selectedWineOption = isActionOption ? null : option ?? null;
            if (wineIdInput) {
                wineIdInput.value = selectedWineOption?.id ?? '';
            }
            if (wineSearch) {
                if (selectedWineOption) {
                    const label = selectedWineOption?.label ?? selectedWineOption?.name ?? '';
                    wineSearch.value = label;
                    currentWineQuery = label.trim();
                } else if (preserveSearchValue) {
                    currentWineQuery = wineSearch.value.trim();
                } else {
                    wineSearch.value = '';
                    currentWineQuery = '';
                }
            }
            updateSummary(selectedWineOption);
            updateHint(selectedWineOption);
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
                if (wineResults?.dataset?.visible === 'true') {
                    event.preventDefault();
                    closeWineResults();
                    return;
                }

                if (wineSearch?.value) {
                    event.preventDefault();
                    wineSearch.value = '';
                    handleWineSearchInput({ target: wineSearch });
                }
            }
        }

        function handleWineSearchFocus() {
            if (!wineResults) {
                return;
            }

            if (wineOptions.length > 0 || wineSearchError || wineSearchLoading || wineSearchTimeoutId !== null) {
                renderWineSearchResults();
            }
        }

        function handleWinePointerDown(event) {
            if (!overlay || overlay.hidden) {
                return;
            }

            if (!wineCombobox) {
                return;
            }

            if (!wineCombobox.contains(event.target)) {
                closeWineResults();
            }
        }

        async function ensureBottleLocations() {
            if (!locationSelect) {
                return;
            }

            if (bottleLocations.length > 0) {
                populateLocationSelect(locationSelect.value ?? '');
                return;
            }

            if (!bottleLocationsPromise) {
                bottleLocationsPromise = sendJson('/wine-manager/options', { method: 'GET' })
                    .then(data => {
                        const locations = Array.isArray(data?.bottleLocations)
                            ? data.bottleLocations
                            : Array.isArray(data?.BottleLocations)
                                ? data.BottleLocations
                                : [];
                        bottleLocations = sortLocations(locations);
                    })
                    .finally(() => {
                        bottleLocationsPromise = null;
                    });
            }

            await bottleLocationsPromise;
            populateLocationSelect(locationSelect.value ?? '');
        }

        function populateLocationSelect(selectedId) {
            if (!locationSelect) {
                return;
            }

            const previous = selectedId ?? locationSelect.value ?? '';
            locationSelect.innerHTML = '';

            const placeholder = document.createElement('option');
            placeholder.value = '';
            placeholder.textContent = 'No location';
            locationSelect.appendChild(placeholder);

            bottleLocations.forEach(option => {
                if (!option?.id) {
                    return;
                }

                const element = document.createElement('option');
                element.value = option.id;
                const label = option.name ?? option.id;
                const capacity = normalizeCapacityValue(option.capacity);
                const suffix = capacity != null ? ` (${capacity} capacity)` : '';
                element.textContent = `${label}${suffix}`;
                locationSelect.appendChild(element);
            });

            if (previous) {
                locationSelect.value = previous;
            }
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
                const nameA = (a?.name ?? '').toString().toLowerCase();
                const nameB = (b?.name ?? '').toString().toLowerCase();
                if (nameA === nameB) {
                    const idA = (a?.id ?? '').toString();
                    const idB = (b?.id ?? '').toString();
                    return idA.localeCompare(idB);
                }

                return nameA.localeCompare(nameB);
            });
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
            const capacityValue = raw.capacity ?? raw.Capacity ?? raw.maxCapacity ?? raw.MaxCapacity;
            const normalized = {
                id: String(id),
                name: typeof nameValue === 'string' ? nameValue : String(nameValue ?? id)
            };

            const normalizedCapacity = normalizeCapacityValue(capacityValue);
            if (normalizedCapacity != null) {
                normalized.capacity = normalizedCapacity;
            }

            return normalized;
        }

        function normalizeCapacityValue(value) {
            if (value == null || value === '') {
                return null;
            }

            const numeric = Number(value);
            if (!Number.isFinite(numeric)) {
                return null;
            }

            const integer = Math.trunc(numeric);
            if (!Number.isFinite(integer) || integer < 0) {
                return null;
            }

            return integer;
        }

        function updateSummary(option = selectedWineOption) {
            if (!summary) {
                return;
            }

            const target = option ?? null;
            if (!target) {
                summary.textContent = 'Search for a wine to see its appellation and color.';
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

            summary.textContent = parts.length > 0
                ? parts.join(' · ')
                : 'No additional details available.';
        }

        function updateHint(option = selectedWineOption) {
            if (!hint) {
                return;
            }

            const target = option ?? null;
            if (!target) {
                hint.textContent = 'Search for a wine to view existing vintages.';
                return;
            }

            if (!Array.isArray(target.vintages) || target.vintages.length === 0) {
                hint.textContent = 'No bottles recorded yet for this wine. Enter any vintage to begin.';
                return;
            }

            const vintages = target.vintages.slice(0, 6);
            const suffix = target.vintages.length > vintages.length ? '…' : '';
            hint.textContent = `Existing vintages: ${vintages.join(', ')}${suffix}`;
        }
    }

    function requestOpenAddWineModal(context = null) {
        return new Promise((resolve, reject) => {
            onReady(() => {
                try {
                    initializeFavoritesModal();
                    if (typeof openModalHandler !== 'function') {
                        throw new Error('Add wine modal is unavailable.');
                    }

                    Promise.resolve(openModalHandler(context)).then(resolve, reject);
                } catch (error) {
                    reject(error);
                }
            });
        });
    }

    function requestCloseAddWineModal() {
        onReady(() => {
            initializeFavoritesModal();
            if (typeof closeModalHandler === 'function') {
                try {
                    closeModalHandler();
                } catch {
                    // no-op
                }
            }
        });
    }

    const inventoryApi = window.InventoryAddModal ?? {};
    inventoryApi.initialize = () => { onReady(initializeFavoritesModal); };
    inventoryApi.open = requestOpenAddWineModal;
    inventoryApi.close = requestCloseAddWineModal;
    inventoryApi.isOpen = () => {
        const overlay = document.getElementById('inventory-add-overlay');
        return Boolean(overlay && overlay.hidden === false);
    };
    window.InventoryAddModal = inventoryApi;

    const inventoryImportApi = window.inventoryImportModal ?? {};
    inventoryImportApi.initialize = inventoryApi.initialize;
    inventoryImportApi.open = requestOpenAddWineModal;
    inventoryImportApi.close = requestCloseAddWineModal;
    inventoryImportApi.isOpen = inventoryApi.isOpen;
    window.inventoryImportModal = inventoryImportApi;

    const favoritesApi = window.wineSurferFavorites ?? {};
    favoritesApi.openAddWineModal = requestOpenAddWineModal;
    window.wineSurferFavorites = favoritesApi;

    onReady(initializeFavoritesModal);
})();
(function () {
    'use strict';

    if (window.WineCreatePopover) {
        return;
    }

    const state = {
        initialized: false,
        hasElements: false,
        overlay: null,
        popover: null,
        form: null,
        nameInput: null,
        colorSelect: null,
        grapeInput: null,
        errorElement: null,
        submitButton: null,
        cancelButton: null,
        closeButton: null,
        parentDialog: null,
        triggerElement: null,
        open: false,
        pending: false,
        onSuccess: null,
        onCancel: null,
        countryField: null,
        regionField: null,
        appellationField: null,
        subAppellationField: null,
        fields: [],
        pointerHandlerRegistered: false,
        keydownHandlerRegistered: false
    };

    const selections = {
        country: { id: null, name: '' },
        region: { id: null, name: '' },
        appellation: { id: null, name: '' },
        subAppellation: { id: null, name: '', isBlank: false }
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
        state.overlay = document.getElementById('create-wine-overlay');
        state.popover = document.getElementById('create-wine-popover');

        if (!state.overlay || !state.popover) {
            state.hasElements = false;
            return;
        }

        state.hasElements = true;
        state.form = state.popover.querySelector('.create-wine-form');
        state.nameInput = state.popover.querySelector('.create-wine-name');
        state.colorSelect = state.popover.querySelector('.create-wine-color');
        state.grapeInput = state.popover.querySelector('.create-wine-grape');
        state.errorElement = state.popover.querySelector('.create-wine-error');
        state.submitButton = state.popover.querySelector('.create-wine-submit');
        state.cancelButton = state.popover.querySelector('.create-wine-cancel');
        state.closeButton = state.popover.querySelector('[data-create-wine-close]');

        state.countryField = createFieldController({
            type: 'country',
            input: state.popover.querySelector('.create-wine-country-input'),
            results: state.popover.querySelector('#create-wine-country-results'),
            combobox: state.popover.querySelector('[data-create-wine-combobox="country"]'),
            minLength: 1,
            createLabel: (query) => query ? `Create “${query}”` : 'Create country',
            createDescription: 'Add a new country to the catalog.',
            shouldIncludeCreate: (query) => query.length > 0,
            emptyMessage: 'No countries found.',
            fetchOptions: (query, signal) => {
                const params = new URLSearchParams({ search: query });
                return sendJson(`/wine-manager/catalog/countries?${params.toString()}`, { method: 'GET', signal });
            },
            normalizeOption: (raw) => {
                const id = pick(raw, ['id', 'Id']);
                const name = toTrimmedString(pick(raw, ['name', 'Name']));
                if (!id || !name) {
                    return null;
                }

                return {
                    id: String(id),
                    name,
                    label: name,
                    description: null
                };
            },
            onValueChanged: (valueChanged) => {
                const trimmed = valueChanged.trim();
                const existing = selections.country;
                const matchesExisting = equalsIgnoreCase(existing.name, trimmed);
                if (matchesExisting && existing.id !== null) {
                    return;
                }

                const changed = !matchesExisting || existing.id !== null;
                selections.country = { id: null, name: trimmed };
                if (changed) {
                    clearRegionSelection();
                }
            },
            onOptionSelected: (option) => {
                selections.country = { id: option?.id ?? null, name: option?.name ?? '' };
                clearRegionSelection();
            },
            onCreateSelected: (query) => {
                const trimmed = query.trim();
                selections.country = { id: null, name: trimmed };
                clearRegionSelection();
            },
            onReset: () => {
                selections.country = { id: null, name: '' };
            }
        });

        state.regionField = createFieldController({
            type: 'region',
            input: state.popover.querySelector('.create-wine-region-input'),
            results: state.popover.querySelector('#create-wine-region-results'),
            combobox: state.popover.querySelector('[data-create-wine-combobox="region"]'),
            minLength: 1,
            createLabel: (query) => query ? `Create “${query}”` : 'Create region',
            createDescription: 'Add a new region to the catalog.',
            shouldIncludeCreate: (query) => query.length > 0,
            emptyMessage: 'No regions found.',
            fetchOptions: (query, signal) => {
                const params = new URLSearchParams({ search: query });
                if (selections.country.id) {
                    params.append('countryId', selections.country.id);
                }
                return sendJson(`/wine-manager/catalog/regions?${params.toString()}`, { method: 'GET', signal });
            },
            normalizeOption: (raw) => {
                const id = pick(raw, ['id', 'Id']);
                const name = toTrimmedString(pick(raw, ['name', 'Name']));
                if (!id || !name) {
                    return null;
                }

                const countryName = toTrimmedString(pick(raw, ['countryName', 'CountryName', 'country', 'Country']));
                return {
                    id: String(id),
                    name,
                    label: name,
                    description: countryName ? `Country: ${countryName}` : null
                };
            },
            onValueChanged: (valueChanged) => {
                const trimmed = valueChanged.trim();
                const existing = selections.region;
                const matchesExisting = equalsIgnoreCase(existing.name, trimmed);
                if (matchesExisting && existing.id !== null) {
                    return;
                }

                const changed = !matchesExisting || existing.id !== null;
                selections.region = { id: null, name: trimmed };
                if (changed) {
                    clearAppellationSelection();
                }
            },
            onOptionSelected: (option) => {
                selections.region = { id: option?.id ?? null, name: option?.name ?? '' };
                clearAppellationSelection();
            },
            onCreateSelected: (query) => {
                const trimmed = query.trim();
                selections.region = { id: null, name: trimmed };
                clearAppellationSelection();
            },
            onReset: () => {
                selections.region = { id: null, name: '' };
            }
        });

        state.appellationField = createFieldController({
            type: 'appellation',
            input: state.popover.querySelector('.create-wine-appellation-input'),
            results: state.popover.querySelector('#create-wine-appellation-results'),
            combobox: state.popover.querySelector('[data-create-wine-combobox="appellation"]'),
            minLength: 1,
            createLabel: (query) => query ? `Create “${query}”` : 'Create appellation',
            createDescription: 'Add a new appellation to the catalog.',
            shouldIncludeCreate: (query) => query.length > 0,
            emptyMessage: selections.region.id
                ? 'No appellations found for this region.'
                : 'Select a region to view suggestions.',
            getShouldFetch: () => Boolean(selections.region.id),
            fetchOptions: (query, signal) => {
                if (!selections.region.id) {
                    return Promise.resolve([]);
                }

                const params = new URLSearchParams({ search: query, regionId: selections.region.id });
                return sendJson(`/wine-manager/catalog/appellations?${params.toString()}`, { method: 'GET', signal });
            },
            normalizeOption: (raw) => {
                const id = pick(raw, ['id', 'Id']);
                const name = toTrimmedString(pick(raw, ['name', 'Name']));
                if (!id || !name) {
                    return null;
                }

                const regionName = toTrimmedString(pick(raw, ['regionName', 'RegionName', 'region', 'Region']));
                const countryName = toTrimmedString(pick(raw, ['countryName', 'CountryName', 'country', 'Country']));
                const metaParts = [];
                if (regionName) {
                    metaParts.push(regionName);
                }
                if (countryName) {
                    metaParts.push(countryName);
                }

                return {
                    id: String(id),
                    name,
                    label: name,
                    description: metaParts.length > 0 ? metaParts.join(' • ') : null
                };
            },
            onValueChanged: (valueChanged) => {
                const trimmed = valueChanged.trim();
                const existing = selections.appellation;
                const matchesExisting = equalsIgnoreCase(existing.name, trimmed);
                if (matchesExisting && existing.id !== null) {
                    return;
                }

                const changed = !matchesExisting || existing.id !== null;
                selections.appellation = { id: null, name: trimmed };
                if (changed) {
                    clearSubAppellationSelection();
                }
            },
            onOptionSelected: (option) => {
                selections.appellation = { id: option?.id ?? null, name: option?.name ?? '' };
                clearSubAppellationSelection();
            },
            onCreateSelected: (query) => {
                const trimmed = query.trim();
                selections.appellation = { id: null, name: trimmed };
                clearSubAppellationSelection();
            },
            onReset: () => {
                selections.appellation = { id: null, name: '' };
            }
        });

        state.subAppellationField = createFieldController({
            type: 'sub-appellation',
            input: state.popover.querySelector('.create-wine-sub-appellation-input'),
            results: state.popover.querySelector('#create-wine-sub-appellation-results'),
            combobox: state.popover.querySelector('[data-create-wine-combobox="sub-appellation"]'),
            minLength: 0,
            includeBlankOption: true,
            blankOptionLabel: 'No sub-appellation (leave blank)',
            blankOptionDescription: 'Save without a sub-appellation.',
            createLabel: (query) => query ? `Create “${query}”` : 'Create sub-appellation',
            createDescription: 'Add a new sub-appellation to the catalog.',
            shouldIncludeCreate: (query) => query.length > 0,
            emptyMessage: selections.appellation.id
                ? 'No sub-appellations found for this appellation.'
                : 'Select an appellation to view suggestions.',
            getShouldFetch: () => Boolean(selections.appellation.id),
            fetchOptions: (query, signal) => {
                if (!selections.appellation.id) {
                    return Promise.resolve([]);
                }

                const params = new URLSearchParams({ search: query, appellationId: selections.appellation.id });
                return sendJson(`/wine-manager/catalog/sub-appellations?${params.toString()}`, { method: 'GET', signal });
            },
            normalizeOption: (raw) => {
                const id = pick(raw, ['id', 'Id']);
                const name = toTrimmedString(pick(raw, ['name', 'Name']));
                if (!id || !name) {
                    return null;
                }

                const appellationName = toTrimmedString(pick(raw, ['appellationName', 'AppellationName', 'appellation', 'Appellation']));
                const regionName = toTrimmedString(pick(raw, ['regionName', 'RegionName', 'region', 'Region']));
                const countryName = toTrimmedString(pick(raw, ['countryName', 'CountryName', 'country', 'Country']));
                const metaParts = [];
                if (appellationName) {
                    metaParts.push(appellationName);
                }
                if (regionName) {
                    metaParts.push(regionName);
                }
                if (countryName) {
                    metaParts.push(countryName);
                }

                return {
                    id: String(id),
                    name,
                    label: name,
                    description: metaParts.length > 0 ? metaParts.join(' • ') : null
                };
            },
            onValueChanged: (valueChanged) => {
                const trimmed = valueChanged.trim();
                const changed = !equalsIgnoreCase(selections.subAppellation.name, trimmed)
                    || selections.subAppellation.id !== null
                    || selections.subAppellation.isBlank;
                selections.subAppellation = { id: null, name: trimmed, isBlank: false };
                if (changed) {
                    // nothing additional to clear
                }
            },
            onOptionSelected: (option) => {
                selections.subAppellation = {
                    id: option?.id ?? null,
                    name: option?.name ?? '',
                    isBlank: option?.isBlank === true
                };
            },
            onBlankSelected: () => {
                selections.subAppellation = { id: null, name: '', isBlank: true };
            },
            onCreateSelected: (query) => {
                const trimmed = query.trim();
                selections.subAppellation = { id: null, name: trimmed, isBlank: false };
            },
            onReset: () => {
                selections.subAppellation = { id: null, name: '', isBlank: false };
            }
        });

        state.fields = [
            state.countryField,
            state.regionField,
            state.appellationField,
            state.subAppellationField
        ];

        if (state.cancelButton) {
            state.cancelButton.addEventListener('click', (event) => {
                event.preventDefault();
                if (state.pending) {
                    return;
                }
                close({ reason: 'cancel', restoreFocus: true });
            });
        }

        if (state.closeButton) {
            state.closeButton.addEventListener('click', () => {
                if (state.pending) {
                    return;
                }
                close({ reason: 'cancel', restoreFocus: true });
            });
        }

        if (state.overlay) {
            state.overlay.addEventListener('click', (event) => {
                if (state.pending) {
                    return;
                }
                if (event.target === state.overlay) {
                    close({ reason: 'cancel', restoreFocus: true });
                }
            });
        }

        if (state.form) {
            state.form.addEventListener('submit', handleSubmit);
        }

        if (!state.pointerHandlerRegistered) {
            document.addEventListener('pointerdown', handleDocumentPointerDown);
            state.pointerHandlerRegistered = true;
        }

        if (!state.keydownHandlerRegistered) {
            document.addEventListener('keydown', handleDocumentKeyDown);
            state.keydownHandlerRegistered = true;
        }
    }

    function createFieldController(config) {
        const input = config.input instanceof HTMLInputElement ? config.input : null;
        const results = config.results instanceof HTMLElement ? config.results : null;
        const combobox = config.combobox instanceof HTMLElement ? config.combobox : input?.closest('.create-wine-combobox') ?? null;
        const minLength = Number.isFinite(config.minLength) ? Math.max(0, Number(config.minLength)) : 1;

        const state = {
            options: [],
            displayedOptions: [],
            controller: null,
            timeoutId: null,
            query: '',
            loading: false,
            error: '',
            activeIndex: -1,
            selectedOption: null,
            selectedLabel: ''
        };

        function getValue() {
            return input ? input.value.trim() : '';
        }

        function setInputValue(value) {
            if (!input) {
                return;
            }

            input.value = value;
            state.query = input.value.trim();
        }

        function showResults() {
            if (!results || !input) {
                return;
            }

            results.dataset.visible = 'true';
            results.removeAttribute('hidden');
            input.setAttribute('aria-expanded', 'true');
        }

        function hideResults() {
            if (!results || !input) {
                return;
            }

            results.dataset.visible = 'false';
            results.setAttribute('hidden', '');
            input.setAttribute('aria-expanded', 'false');
            input.removeAttribute('aria-activedescendant');
            state.activeIndex = -1;
        }

        function closeResultsOnly() {
            hideResults();
            if (results) {
                results.innerHTML = '';
            }
            state.displayedOptions = [];
        }

        function cancelPendingRequest() {
            if (state.controller) {
                state.controller.abort();
                state.controller = null;
            }
        }

        function cancelSearchTimeout() {
            if (state.timeoutId !== null) {
                window.clearTimeout(state.timeoutId);
                state.timeoutId = null;
            }
        }

        function shouldFetchSuggestions() {
            if (typeof config.getShouldFetch === 'function') {
                try {
                    return config.getShouldFetch();
                } catch {
                    return true;
                }
            }

            return true;
        }

        function scheduleSearch(query) {
            cancelSearchTimeout();

            if (!shouldFetchSuggestions()) {
                render();
                return;
            }

            if (query.length < minLength) {
                render();
                return;
            }

            state.timeoutId = window.setTimeout(() => {
                performSearch(query).catch(() => {
                    // handled via state
                });
            }, 300);
        }

        async function performSearch(query) {
            cancelSearchTimeout();

            if (!shouldFetchSuggestions()) {
                state.options = [];
                state.loading = false;
                state.error = '';
                render();
                return;
            }

            if (!query || query.length < minLength) {
                state.options = [];
                state.loading = false;
                state.error = '';
                render();
                return;
            }

            cancelPendingRequest();
            const controller = new AbortController();
            state.controller = controller;
            state.loading = true;
            state.error = '';
            render();

            try {
                const rawOptions = await config.fetchOptions(query, controller.signal);
                if (state.controller !== controller) {
                    return;
                }

                const normalized = Array.isArray(rawOptions)
                    ? rawOptions
                        .map((option) => {
                            try {
                                return config.normalizeOption(option);
                            } catch {
                                return null;
                            }
                        })
                        .filter(Boolean)
                    : [];

                state.options = normalized;
                state.loading = false;
                state.error = '';
                state.activeIndex = -1;
                render();
            } catch (error) {
                if (controller.signal.aborted) {
                    return;
                }

                state.options = [];
                state.loading = false;
                state.error = error?.message ?? 'Unable to load suggestions.';
                state.activeIndex = -1;
                render();
            } finally {
                if (state.controller === controller) {
                    state.controller = null;
                }
            }
        }

        function buildDisplayedOptions(query) {
            const list = [];

            if (config.includeBlankOption) {
                list.push({
                    isBlank: true,
                    label: config.blankOptionLabel ?? 'No sub-appellation (leave blank)',
                    description: config.blankOptionDescription ?? null
                });
            }

            state.options.forEach((option) => {
                list.push({ ...option, isAction: false, isBlank: false });
            });

            const shouldIncludeCreate = typeof config.shouldIncludeCreate === 'function'
                ? config.shouldIncludeCreate(query, list)
                : query.length >= minLength;

            if (shouldIncludeCreate && (config.createLabel || query.length > 0)) {
                const label = config.createLabel ? config.createLabel(query) : `Create “${query}”`;
                list.push({
                    isAction: true,
                    isCreate: true,
                    label,
                    name: query,
                    description: config.createDescription ?? 'Add a new entry to the catalog.',
                    query
                });
            }

            return list;
        }

        function render() {
            if (!results || !input) {
                return;
            }

            const trimmedQuery = getValue();
            const shouldDisplay = state.loading
                || Boolean(state.error)
                || (trimmedQuery.length >= minLength)
                || (config.includeBlankOption && shouldFetchSuggestions())
                || (typeof config.shouldIncludeCreate === 'function'
                    ? config.shouldIncludeCreate(trimmedQuery, state.options)
                    : trimmedQuery.length > 0)
                || (Array.isArray(state.options) && state.options.length > 0)
                || (!shouldFetchSuggestions() && trimmedQuery.length > 0);

            if (!shouldDisplay) {
                hideResults();
                results.innerHTML = '';
                state.displayedOptions = [];
                return;
            }

            showResults();
            results.innerHTML = '';

            if (!shouldFetchSuggestions()) {
                const message = config.emptyMessage
                    ?? 'Complete the previous field to view suggestions.';
                results.appendChild(buildStatusElement(message));
                state.displayedOptions = [];
                return;
            }

            if (state.loading) {
                results.appendChild(buildStatusElement('Searching…'));
                state.displayedOptions = [];
                return;
            }

            if (state.error) {
                const errorStatus = buildStatusElement(state.error, true);
                results.appendChild(errorStatus);
                state.displayedOptions = [];
                return;
            }

            const options = buildDisplayedOptions(trimmedQuery);

            if (options.length === 0) {
                if (trimmedQuery.length >= minLength) {
                    const message = config.emptyMessage
                        ?? 'No matches found.';
                    results.appendChild(buildStatusElement(message));
                }
                state.displayedOptions = [];
                return;
            }

            const list = document.createElement('div');
            list.className = 'inventory-add-wine-options create-wine-options';

            state.displayedOptions = options;

            options.forEach((option, index) => {
                const element = document.createElement('button');
                element.type = 'button';
                element.className = 'inventory-add-wine-option create-wine-option';
                element.dataset.index = String(index);
                element.setAttribute('role', 'option');
                element.id = `create-wine-option-${config.type}-${index}`;

                if (option.isAction) {
                    element.classList.add('inventory-add-wine-option--action', 'create-wine-option--action');
                }

                if (option.isBlank) {
                    element.classList.add('create-wine-option--blank');
                }

                const nameSpan = document.createElement('span');
                nameSpan.className = 'inventory-add-wine-option__name';
                nameSpan.textContent = option.label ?? option.name ?? '';
                element.appendChild(nameSpan);

                if (option.description) {
                    const metaSpan = document.createElement('span');
                    metaSpan.className = 'inventory-add-wine-option__meta';
                    metaSpan.textContent = option.description;
                    element.appendChild(metaSpan);
                }

                element.addEventListener('mousedown', (event) => {
                    event.preventDefault();
                });

                element.addEventListener('click', () => {
                    selectOption(option);
                });

                list.appendChild(element);
            });

            results.appendChild(list);
            highlightActive();
        }

        function highlightActive() {
            if (!results || !input) {
                return;
            }

            const optionElements = Array.from(results.querySelectorAll('.inventory-add-wine-option'));
            optionElements.forEach((element, index) => {
                const isActive = index === state.activeIndex;
                element.classList.toggle('is-active', isActive);
                element.setAttribute('aria-selected', isActive ? 'true' : 'false');
                if (isActive) {
                    input.setAttribute('aria-activedescendant', element.id);
                    element.scrollIntoView({ block: 'nearest' });
                }
            });

            if (state.activeIndex < 0) {
                input.removeAttribute('aria-activedescendant');
            }
        }

        function moveActive(step) {
            if (state.displayedOptions.length === 0) {
                return;
            }

            if (state.activeIndex < 0) {
                state.activeIndex = step > 0 ? 0 : state.displayedOptions.length - 1;
            } else {
                state.activeIndex += step;
                if (state.activeIndex >= state.displayedOptions.length) {
                    state.activeIndex = 0;
                } else if (state.activeIndex < 0) {
                    state.activeIndex = state.displayedOptions.length - 1;
                }
            }

            highlightActive();
        }

        function selectOption(option) {
            if (!option) {
                return;
            }

            if (option.isAction) {
                state.selectedOption = null;
                state.selectedLabel = '';
                setInputValue(option.query ?? getValue());
                hideResults();
                if (typeof config.onCreateSelected === 'function') {
                    config.onCreateSelected(option.query ?? '');
                }
                if (typeof config.onValueChanged === 'function') {
                    config.onValueChanged(input ? input.value : '');
                }
                return;
            }

            if (option.isBlank) {
                state.selectedOption = { isBlank: true };
                state.selectedLabel = option.label ?? '';
                setInputValue(option.label ?? '');
                hideResults();
                if (typeof config.onBlankSelected === 'function') {
                    config.onBlankSelected();
                }
                return;
            }

            state.selectedOption = option;
            state.selectedLabel = option.label ?? option.name ?? '';
            setInputValue(state.selectedLabel);
            hideResults();
            if (typeof config.onOptionSelected === 'function') {
                config.onOptionSelected(option);
            }
            if (typeof config.onValueChanged === 'function') {
                config.onValueChanged(state.selectedLabel);
            }
        }

        function handleInput(event) {
            const value = event?.target?.value ?? '';
            const trimmed = value.trim();
            state.query = trimmed;

            const matchesSelected = state.selectedLabel
                && trimmed.toLowerCase() === state.selectedLabel.toLowerCase();

            if (!matchesSelected) {
                state.selectedOption = null;
                state.selectedLabel = '';
                if (typeof config.onOptionSelected === 'function' && typeof config.onSelectionCleared === 'function') {
                    config.onSelectionCleared();
                }
            }

            if (typeof config.onValueChanged === 'function') {
                config.onValueChanged(value);
            }

            if (trimmed.length === 0) {
                cancelSearchTimeout();
                cancelPendingRequest();
                state.options = [];
                state.loading = false;
                state.error = '';
                render();
                return;
            }

            scheduleSearch(trimmed);
            render();
        }

        function handleKeyDown(event) {
            if (event.key === 'ArrowDown') {
                event.preventDefault();
                moveActive(1);
                return;
            }

            if (event.key === 'ArrowUp') {
                event.preventDefault();
                moveActive(-1);
                return;
            }

            if (event.key === 'Enter') {
                if (state.activeIndex >= 0 && state.displayedOptions[state.activeIndex]) {
                    event.preventDefault();
                    selectOption(state.displayedOptions[state.activeIndex]);
                }
                return;
            }

            if (event.key === 'Escape') {
                if (results?.dataset?.visible === 'true') {
                    event.preventDefault();
                    hideResults();
                }
            }
        }

        function handleFocus() {
            if (state.displayedOptions.length > 0 || state.loading || state.error) {
                render();
            }
        }

        function handleBlur() {
            window.setTimeout(() => {
                if (combobox && !combobox.contains(document.activeElement)) {
                    hideResults();
                }
            }, 100);
        }

        function reset(options = {}) {
            const notify = options.notify !== false;
            cancelSearchTimeout();
            cancelPendingRequest();
            state.options = [];
            state.displayedOptions = [];
            state.loading = false;
            state.error = '';
            state.activeIndex = -1;
            state.selectedOption = null;
            state.selectedLabel = '';
            if (!options.keepValue) {
                setInputValue('');
            }
            hideResults();
            results && (results.innerHTML = '');
            if (notify && typeof config.onReset === 'function') {
                config.onReset();
            }
        }

        if (input) {
            input.addEventListener('input', handleInput);
            input.addEventListener('keydown', handleKeyDown);
            input.addEventListener('focus', handleFocus);
            input.addEventListener('blur', handleBlur);
        }

        return {
            combobox,
            reset,
            getValue,
            setValue: setInputValue,
            closeResults: closeResultsOnly,
            setDisabled: (disabled) => {
                if (!input) {
                    return;
                }

                input.disabled = disabled;
                if (disabled) {
                    input.setAttribute('aria-disabled', 'true');
                    hideResults();
                } else {
                    input.removeAttribute('aria-disabled');
                }
            }
        };
    }

    function buildStatusElement(text, isError = false) {
        const status = document.createElement('div');
        status.className = 'inventory-add-wine-result-status create-wine-result-status';
        if (isError) {
            status.classList.add('inventory-add-wine-result-status--error');
        }
        status.textContent = text;
        status.setAttribute('role', 'status');
        return status;
    }

    function handleDocumentPointerDown(event) {
        if (!state.open) {
            return;
        }

        const target = event.target;
        if (!(target instanceof Node)) {
            return;
        }

        const insideCombobox = state.fields.some((field) => field?.combobox?.contains(target));
        if (!insideCombobox) {
            state.fields.forEach((field) => field?.closeResults && field.closeResults());
        }
    }

    function handleDocumentKeyDown(event) {
        if (!state.open || event.key !== 'Escape') {
            return;
        }

        event.preventDefault();
        close({ reason: 'cancel', restoreFocus: true });
    }

    function resetForm() {
        if (state.form) {
            state.form.reset();
        }

        selections.country = { id: null, name: '' };
        selections.region = { id: null, name: '' };
        selections.appellation = { id: null, name: '' };
        selections.subAppellation = { id: null, name: '', isBlank: false };

        state.fields.forEach((field) => field?.reset && field.reset({ notify: true }));

        if (state.colorSelect) {
            const defaultValue = state.colorSelect.dataset?.defaultValue ?? state.colorSelect.options?.[0]?.value ?? 'Red';
            state.colorSelect.value = defaultValue;
        }

        if (state.grapeInput) {
            state.grapeInput.value = '';
        }

        showError('');
        setPending(false);
    }

    function showError(message) {
        if (!state.errorElement) {
            return;
        }

        const text = message ?? '';
        state.errorElement.textContent = text;
        state.errorElement.setAttribute('aria-hidden', text ? 'false' : 'true');
    }

    function setPending(pending) {
        state.pending = pending;
        if (state.submitButton) {
            state.submitButton.disabled = pending;
        }
        if (state.cancelButton) {
            state.cancelButton.disabled = pending;
        }
        if (state.closeButton) {
            state.closeButton.disabled = pending;
        }
        state.fields.forEach((field) => field?.setDisabled && field.setDisabled(pending));
        if (state.form) {
            if (pending) {
                state.form.setAttribute('aria-busy', 'true');
            } else {
                state.form.removeAttribute('aria-busy');
            }
        }
    }

    function open(options = {}) {
        initialize();
        if (!state.hasElements || !state.overlay || !state.popover) {
            return false;
        }

        if (state.open) {
            return true;
        }

        resetForm();
        state.open = true;
        state.onSuccess = typeof options.onSuccess === 'function' ? options.onSuccess : null;
        state.onCancel = typeof options.onCancel === 'function' ? options.onCancel : null;
        if (Object.prototype.hasOwnProperty.call(options, 'parentDialog')) {
            state.parentDialog = options.parentDialog instanceof HTMLElement ? options.parentDialog : null;
        } else {
            state.parentDialog = document.getElementById('inventory-add-popover');
        }
        state.triggerElement = options.triggerElement instanceof HTMLElement
            ? options.triggerElement
            : (document.activeElement instanceof HTMLElement ? document.activeElement : null);

        state.overlay.hidden = false;
        state.overlay.setAttribute('aria-hidden', 'false');
        state.overlay.classList.add('is-open');

        if (state.parentDialog) {
            state.parentDialog.setAttribute('aria-hidden', 'true');
            state.parentDialog.setAttribute('inert', '');
        }

        const initialName = typeof options.initialName === 'string'
            ? options.initialName.trim()
            : '';

        if (state.nameInput) {
            state.nameInput.value = initialName;
            window.setTimeout(() => {
                state.nameInput?.focus();
                const length = state.nameInput?.value?.length ?? 0;
                try {
                    state.nameInput?.setSelectionRange(length, length);
                } catch {
                    // ignore selection errors
                }
            }, 0);
        }

        const initialColor = typeof options.initialColor === 'string'
            ? options.initialColor.trim()
            : '';
        if (state.colorSelect && initialColor) {
            const normalizedColor = initialColor.toLowerCase();
            const matchedOption = Array.from(state.colorSelect.options || []).find(option =>
                option.value && option.value.toLowerCase() === normalizedColor);
            if (matchedOption) {
                state.colorSelect.value = matchedOption.value;
            }
        }

        const initialCountry = typeof options.initialCountry === 'string'
            ? options.initialCountry.trim()
            : '';
        if (initialCountry) {
            state.countryField?.setValue?.(initialCountry);
            selections.country = { id: null, name: initialCountry };
        }

        const initialRegion = typeof options.initialRegion === 'string'
            ? options.initialRegion.trim()
            : '';
        if (initialRegion) {
            state.regionField?.setValue?.(initialRegion);
            selections.region = { id: null, name: initialRegion };
        }

        const initialAppellation = typeof options.initialAppellation === 'string'
            ? options.initialAppellation.trim()
            : '';
        if (initialAppellation) {
            state.appellationField?.setValue?.(initialAppellation);
            selections.appellation = { id: null, name: initialAppellation };
        }

        const initialSubAppellation = typeof options.initialSubAppellation === 'string'
            ? options.initialSubAppellation.trim()
            : '';
        if (initialSubAppellation) {
            state.subAppellationField?.setValue?.(initialSubAppellation);
            selections.subAppellation = { id: null, name: initialSubAppellation, isBlank: false };
        }

        return true;
    }

    function close({ reason = 'cancel', restoreFocus = true } = {}) {
        if (!state.open || !state.overlay || !state.popover) {
            return;
        }

        const wasPending = state.pending;
        if (wasPending) {
            setPending(false);
        }

        state.open = false;
        state.overlay.classList.remove('is-open');
        state.overlay.hidden = true;
        state.overlay.setAttribute('aria-hidden', 'true');

        if (state.parentDialog) {
            state.parentDialog.removeAttribute('aria-hidden');
            state.parentDialog.removeAttribute('inert');
        }

        const cancelCallback = state.onCancel;
        state.onCancel = null;

        state.onSuccess = null;
        state.parentDialog = null;

        if (restoreFocus && state.triggerElement && typeof state.triggerElement.focus === 'function') {
            try {
                state.triggerElement.focus();
            } catch {
                // ignore focus errors
            }
        }

        state.triggerElement = null;

        if (reason !== 'success' && typeof cancelCallback === 'function') {
            cancelCallback();
        }

        resetForm();
    }

    async function handleSubmit(event) {
        event.preventDefault();
        if (state.pending) {
            return;
        }

        const name = state.nameInput?.value?.trim() ?? '';
        const color = state.colorSelect?.value?.trim() ?? '';
        const grape = state.grapeInput?.value?.trim() ?? '';
        const country = selections.country.name?.trim() ?? '';
        const region = selections.region.name?.trim() ?? '';
        const appellation = selections.appellation.name?.trim() ?? '';
        const subAppellation = selections.subAppellation.isBlank
            ? ''
            : (selections.subAppellation.name?.trim() ?? '');

        if (!name) {
            showError('Enter a wine name.');
            state.nameInput?.focus();
            return;
        }

        if (!color) {
            showError('Select a color.');
            state.colorSelect?.focus();
            return;
        }

        if (!region) {
            showError('Enter a region.');
            state.regionField?.setValue?.('');
            state.popover.querySelector('.create-wine-region-input')?.focus();
            return;
        }

        if (!appellation) {
            showError('Enter an appellation.');
            state.popover.querySelector('.create-wine-appellation-input')?.focus();
            return;
        }

        const payload = {
            name,
            color,
            country: country || null,
            region,
            appellation,
            subAppellation: subAppellation ? subAppellation : null,
            grapeVariety: grape || null
        };

        showError('');

        try {
            setPending(true);
            const response = await sendJson('/wine-manager/catalog/wines', {
                method: 'POST',
                body: JSON.stringify(payload)
            });

            const callback = state.onSuccess;
            close({ reason: 'success', restoreFocus: false });
            if (typeof callback === 'function') {
                callback(response);
            }
        } catch (error) {
            showError(error?.message ?? 'Unable to create wine right now.');
        } finally {
            setPending(false);
        }
    }

    function clearRegionSelection() {
        const shouldReset = selections.region.id !== null || selections.region.name !== '';
        selections.region = { id: null, name: '' };
        selections.appellation = { id: null, name: '' };
        selections.subAppellation = { id: null, name: '', isBlank: false };
        if (shouldReset) {
            state.regionField?.reset({ notify: false });
            state.appellationField?.reset({ notify: false });
            state.subAppellationField?.reset({ notify: false });
        }
    }

    function clearAppellationSelection() {
        const shouldReset = selections.appellation.id !== null || selections.appellation.name !== '';
        selections.appellation = { id: null, name: '' };
        selections.subAppellation = { id: null, name: '', isBlank: false };
        if (shouldReset) {
            state.appellationField?.reset({ notify: false });
            state.subAppellationField?.reset({ notify: false });
        }
    }

    function clearSubAppellationSelection() {
        if (selections.subAppellation.id !== null || selections.subAppellation.name !== '' || selections.subAppellation.isBlank) {
            selections.subAppellation = { id: null, name: '', isBlank: false };
            state.subAppellationField?.reset({ notify: false });
        }
    }

    function toTrimmedString(value) {
        if (typeof value === 'string') {
            return value.trim();
        }

        if (value == null) {
            return '';
        }

        return String(value).trim();
    }

    function pick(obj, keys) {
        if (!Array.isArray(keys)) {
            return undefined;
        }

        for (const key of keys) {
            if (obj && Object.prototype.hasOwnProperty.call(obj, key)) {
                const value = obj[key];
                if (value !== undefined && value !== null) {
                    return value;
                }
            }
        }

        return undefined;
    }

    function equalsIgnoreCase(a, b) {
        if (a == null && b == null) {
            return true;
        }

        if (a == null || b == null) {
            return false;
        }

        return String(a).trim().toLowerCase() === String(b).trim().toLowerCase();
    }

    async function sendJson(url, options) {
        const init = {
            headers: {
                'Content-Type': 'application/json',
                'Accept': 'application/json'
            },
            credentials: 'same-origin',
            ...options
        };

        const response = await fetch(url, init);
        if (!response.ok) {
            let message = `${response.status} ${response.statusText}`;
            try {
                const problem = await response.json();
                if (typeof problem === 'string') {
                    message = problem;
                } else if (problem?.message) {
                    message = problem.message;
                } else if (problem?.title) {
                    message = problem.title;
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

    window.WineCreatePopover = {
        initialize,
        open,
        close,
        isOpen: () => state.open
    };

    onReady(initialize);
})();
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
