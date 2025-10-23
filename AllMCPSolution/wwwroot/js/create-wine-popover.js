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
        state.parentDialog = options.parentDialog instanceof HTMLElement ? options.parentDialog : document.getElementById('inventory-add-popover');
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
