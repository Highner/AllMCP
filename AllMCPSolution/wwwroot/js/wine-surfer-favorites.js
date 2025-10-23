(function () {
    let initialized = false;
    let openModalHandler = null;

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

    function createWineSurferOption(query) {
        const trimmed = (query ?? '').trim();
        return {
            id: '__wine_surfer__',
            name: 'Find with Wine Surfer',
            label: 'Find with Wine Surfer',
            isWineSurfer: true,
            query: trimmed
        };
    }

    function appendWineSurferOption(options, query) {
        const baseOptions = Array.isArray(options)
            ? options.filter(option => option && option.isWineSurfer !== true)
            : [];
        const wineSurferOption = createWineSurferOption(query);
        baseOptions.push(wineSurferOption);
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

        const triggerSelector = '[data-add-wine-trigger="favorites"], [data-add-wine-trigger="surf-eye"]';

        const normalizeContext = (context) => {
            if (!context) {
                return null;
            }

            return {
                source: toTrimmedString(context.source),
                name: toTrimmedString(context.name),
                producer: toTrimmedString(context.producer),
                region: toTrimmedString(context.region),
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
                name: dataset.wineName,
                producer: dataset.wineProducer,
                region: dataset.wineRegion,
                variety: dataset.wineVariety,
                vintage: dataset.wineVintage
            });
        }

        const bindClose = (element) => {
            if (!element) {
                return;
            }

            element.addEventListener('click', () => {
                closeModal();
            });
        };

        bindClose(cancel);
        bindClose(headerClose);

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
                closeModal();
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
                closeModal();
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
        }

        async function openModal(context = null) {
            showError('');
            showStatus('', 'info');
            overlay.hidden = false;
            overlay.setAttribute('aria-hidden', 'false');
            overlay.classList.add('is-open');
            document.body.style.overflow = 'hidden';

            const normalizedContext = normalizeContext(context);
            resetWineSelection();
            closeWineSurferPopover({ restoreFocus: false });

            setModalLoading(true);
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
                closeModal();
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

        function findBestWineOptionMatch(context) {
            if (!context) {
                return null;
            }

            const nameKey = createComparisonKey(context.name);
            const regionKey = createComparisonKey(context.region);

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

            if (regionKey) {
                const scopedOptions = candidates.length > 0 ? candidates : wineOptions;
                const regionMatches = filterMatchesByRegion(scopedOptions, regionKey);
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

        function filterMatchesByRegion(options, regionKey) {
            if (!Array.isArray(options) || !regionKey) {
                return [];
            }

            return options.filter(option => matchesRegionKey(option, regionKey));
        }

        function matchesRegionKey(option, regionKey) {
            if (!option || !regionKey) {
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

            return keys.some(key => key === regionKey || key.includes(regionKey) || regionKey.includes(key));
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

        function closeModal() {
            setModalLoading(false);
            closeWineSurferPopover({ restoreFocus: false });
            overlay.classList.remove('is-open');
            overlay.setAttribute('aria-hidden', 'true');
            overlay.hidden = true;
            document.body.style.overflow = '';
            showError('');
            resetWineSelection();
            if (vintage) {
                vintage.value = '';
            }
            if (locationSelect) {
                locationSelect.value = '';
            }
            if (quantity) {
                quantity.value = '1';
            }
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
                await sendJson('/wine-manager/inventory', {
                    method: 'POST',
                    body: JSON.stringify({ wineId, vintage: vintageValue, quantity: quantityValue, bottleLocationId: locationValue || null })
                });
                closeModal();
                const message = quantityValue === 1
                    ? 'Bottle added to your inventory.'
                    : `${quantityValue} bottles added to your inventory.`;
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

                // Close the suggestions popover and show a transient confirmation
                closeWineSurferPopover({ restoreFocus: true });
                if (statusMessage) {
                    statusMessage.textContent = `Added ${option.name} to your catalog.`;
                    statusMessage.hidden = false;
                    setTimeout(() => {
                        if (statusMessage) {
                            statusMessage.textContent = '';
                            statusMessage.hidden = true;
                        }
                    }, 3500);
                }
            } catch (error) {
                wineSurferError = error?.message ?? 'Wine Surfer could not add that wine right now.';
            } finally {
                wineSurferSelectionPending = false;
                renderWineSurferResults();
            }
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

        async function openWineSurferPopover(query) {
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
            wineSurferLoading = true;
            wineSurferError = '';
            wineSurferResults = [];
            wineSurferActiveQuery = trimmedQuery;
            renderWineSurferResults();

            const controller = new AbortController();
            wineSurferController = controller;

            try {
                const response = await sendJson(`/wine-manager/wine-surfer?query=${encodeURIComponent(trimmedQuery)}`, {
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
                wineOptions = appendWineSurferOption(normalized, query);
                lastCompletedQuery = query;
                wineSearchLoading = false;
                wineSearchError = '';
                renderWineSearchResults();
            } catch (error) {
                if (controller.signal.aborted) {
                    return;
                }

                wineOptions = appendWineSurferOption([], query);
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

            const hasWineSurferOption = wineOptions.some(option => option?.isWineSurfer);
            const cellarOptions = wineOptions.filter(option => option && !option.isWineSurfer);
            const hasCellarOptions = cellarOptions.length > 0;

            if (wineSearchError) {
                const errorStatus = buildWineStatusElement(wineSearchError);
                errorStatus.classList.add('inventory-add-wine-result-status--error');
                wineResults.appendChild(errorStatus);
            } else if (!hasCellarOptions) {
                if (trimmedQuery === lastCompletedQuery) {
                    const message = hasWineSurferOption
                        ? 'No wines found in your cellar. Try Wine Surfer for more matches.'
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

                if (!option.isWineSurfer && option.id) {
                    element.dataset.wineId = option.id;
                }

                if (option.isWineSurfer) {
                    element.classList.add('inventory-add-wine-option--action');
                }

                const optionId = option.isWineSurfer
                    ? 'inventory-add-wine-option-wine-surfer'
                    : `inventory-add-wine-option-${option.id}`;
                element.id = optionId;

                const nameSpan = document.createElement('span');
                nameSpan.className = 'inventory-add-wine-option__name';
                nameSpan.textContent = option.label
                    ?? option.name
                    ?? (option.isWineSurfer ? 'Find with Wine Surfer' : option.id ?? 'Wine option');
                element.appendChild(nameSpan);

                if (option.isWineSurfer) {
                    const actionMeta = document.createElement('span');
                    actionMeta.className = 'inventory-add-wine-option__meta';
                    actionMeta.textContent = 'Ask Wine Surfer to suggest wines beyond your cellar.';
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

            if (option.isWineSurfer) {
                closeWineResults();
                const queryValue = (option.query ?? currentWineQuery ?? wineSearch?.value ?? '').trim();
                openWineSurferPopover(queryValue);
                return;
            }

            setSelectedWine(option);
            showError('');
            closeWineResults();
        }

        function setSelectedWine(option, { preserveSearchValue = false } = {}) {
            const isWineSurfer = option?.isWineSurfer === true;
            selectedWineOption = isWineSurfer ? null : option ?? null;
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

    const globalApi = window.wineSurferFavorites ?? {};
    globalApi.openAddWineModal = requestOpenAddWineModal;
    window.wineSurferFavorites = globalApi;

    onReady(initializeFavoritesModal);
})();
