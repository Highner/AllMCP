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
        const select = popover.querySelector('.inventory-add-wine');
        const vintage = popover.querySelector('.inventory-add-vintage');
        const quantity = popover.querySelector('.inventory-add-quantity');
        const summary = popover.querySelector('.inventory-add-summary');
        const hint = popover.querySelector('.inventory-add-vintage-hint');
        const error = popover.querySelector('.inventory-add-error');
        const submit = popover.querySelector('.inventory-add-submit');
        const cancel = popover.querySelector('.inventory-add-cancel');
        const headerClose = popover.querySelector('[data-add-wine-close]');
        const statusMessage = document.querySelector('[data-favorite-message]');

        let wineOptions = [];
        let wineOptionsPromise = null;
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

        overlay.addEventListener('click', event => {
            if (event.target === overlay) {
                closeModal();
            }
        });

        document.addEventListener('keydown', event => {
            if (event.key === 'Escape' && !overlay.hidden) {
                closeModal();
            }
        });

        select?.addEventListener('change', () => {
            updateSummary();
            updateHint();
            showError('');
        });

        form?.addEventListener('submit', handleSubmit);

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
            if (select) {
                select.disabled = state;
            }
            if (vintage) {
                vintage.disabled = state;
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

            setModalLoading(true);
            try {
                await ensureWineOptions();
                const matchedOption = findBestWineOptionMatch(normalizedContext);
                const selectedId = matchedOption?.id ?? '';
                populateSelect(selectedId);
                const normalizedVintage = normalizeVintageValue(normalizedContext?.vintage);
                if (vintage) {
                    vintage.value = normalizedVintage;
                }
                if (quantity) {
                    quantity.value = '1';
                }
                updateSummary();
                updateHint();

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

            select?.focus();
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
            overlay.classList.remove('is-open');
            overlay.setAttribute('aria-hidden', 'true');
            overlay.hidden = true;
            document.body.style.overflow = '';
            showError('');
            if (select) {
                select.value = '';
            }
            if (vintage) {
                vintage.value = '';
            }
            if (quantity) {
                quantity.value = '1';
            }
            updateSummary();
            updateHint();
        }

        async function handleSubmit(event) {
            event.preventDefault();
            if (modalLoading) {
                return;
            }

            const wineId = select?.value ?? '';
            const vintageValue = Number(vintage?.value ?? '');
            const quantityValue = Number(quantity?.value ?? '1');

            if (!wineId) {
                showError('Select a wine to add to your inventory.');
                select?.focus();
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
                    body: JSON.stringify({ wineId, vintage: vintageValue, quantity: quantityValue })
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

        async function ensureWineOptions() {
            if (wineOptions.length > 0) {
                return;
            }

            if (!wineOptionsPromise) {
                wineOptionsPromise = sendJson('/wine-manager/wines', { method: 'GET' })
                    .then(data => {
                        const items = Array.isArray(data) ? data : [];
                        wineOptions = items
                            .map(normalizeWineOption)
                            .filter(Boolean)
                            .sort((a, b) => {
                                const nameA = (a?.name ?? '').toString().toLowerCase();
                                const nameB = (b?.name ?? '').toString().toLowerCase();
                                if (nameA === nameB) {
                                    const regionA = (a?.subAppellation ?? '').toString().toLowerCase();
                                    const regionB = (b?.subAppellation ?? '').toString().toLowerCase();
                                    return regionA.localeCompare(regionB, undefined, { sensitivity: 'base' });
                                }

                                return nameA.localeCompare(nameB, undefined, { sensitivity: 'base' });
                            });
                    })
                    .finally(() => {
                        wineOptionsPromise = null;
                    });
            }

            await wineOptionsPromise;
        }

        function populateSelect(selectedId) {
            if (!select) {
                return;
            }

            const previous = selectedId ?? select.value ?? '';
            select.innerHTML = '';

            const placeholder = document.createElement('option');
            placeholder.value = '';
            placeholder.textContent = 'Select a wine';
            select.appendChild(placeholder);

            wineOptions.forEach(option => {
                if (!option?.id) {
                    return;
                }

                const element = document.createElement('option');
                element.value = option.id;
                element.textContent = option.label ?? option.name ?? option.id;
                select.appendChild(element);
            });

            if (previous) {
                select.value = previous;
            }
        }

        function updateSummary() {
            if (!summary) {
                return;
            }

            const selectedId = select?.value ?? '';
            if (!selectedId) {
                summary.textContent = 'Select a wine to see its appellation and color.';
                return;
            }

            const option = wineOptions.find(item => item?.id === selectedId);
            if (!option) {
                summary.textContent = 'Select a wine to see its appellation and color.';
                return;
            }

            const parts = [];
            if (option.color) {
                parts.push(option.color);
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
                parts.push(regionParts.join(' • '));
            }

            summary.textContent = parts.length > 0
                ? parts.join(' · ')
                : 'No additional details available.';
        }

        function updateHint() {
            if (!hint) {
                return;
            }

            const selectedId = select?.value ?? '';
            if (!selectedId) {
                hint.textContent = 'Pick a wine to view existing vintages.';
                return;
            }

            const option = wineOptions.find(item => item?.id === selectedId);
            if (!option || !Array.isArray(option.vintages) || option.vintages.length === 0) {
                hint.textContent = 'No bottles recorded yet for this wine. Enter any vintage to begin.';
                return;
            }

            const vintages = option.vintages.slice(0, 6);
            const suffix = option.vintages.length > vintages.length ? '…' : '';
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
