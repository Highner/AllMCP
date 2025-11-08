(function () {
    'use strict';

    const SELECTORS = {
        overlay: '[data-accept-deliveries-overlay]',
        dialog: '[data-accept-deliveries-modal]',
        trigger: '[data-accept-deliveries-trigger]',
        form: '[data-accept-deliveries-form]',
        close: '[data-accept-deliveries-close]',
        cancel: '[data-accept-deliveries-cancel]',
        submit: '[data-accept-deliveries-submit]',
        location: '[data-accept-deliveries-location]',
        error: '[data-accept-deliveries-error]',
        root: '[data-accept-deliveries-root]',
        filter: '[data-accept-deliveries-filter]',
        tbody: '[data-accept-deliveries-tbody]',
        row: '[data-accept-deliveries-row]',
        noResults: '[data-accept-deliveries-no-results]'
    };

    function onReady(callback) {
        if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', callback, { once: true });
        } else {
            callback();
        }
    }

    function unique(values) {
        return Array.from(new Set(values));
    }

    async function sendAcceptRequest(payload) {
        const response = await fetch('/wine-manager/deliveries/accept', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'Accept': 'application/json'
            },
            credentials: 'same-origin',
            body: JSON.stringify(payload)
        });

        if (response.ok) {
            return;
        }

        let message = 'Unable to accept deliveries.';

        try {
            const problem = await response.json();
            if (typeof problem === 'string' && problem.trim().length > 0) {
                message = problem;
            } else if (problem && typeof problem === 'object') {
                if (typeof problem.title === 'string' && problem.title.trim().length > 0) {
                    message = problem.title;
                } else if (typeof problem.detail === 'string' && problem.detail.trim().length > 0) {
                    message = problem.detail;
                } else if (typeof problem.message === 'string' && problem.message.trim().length > 0) {
                    message = problem.message;
                } else if (problem.errors && typeof problem.errors === 'object') {
                    const firstKey = Object.keys(problem.errors)[0];
                    const firstError = firstKey ? problem.errors[firstKey] : undefined;
                    if (Array.isArray(firstError) && firstError.length > 0) {
                        message = firstError[0];
                    }
                }
            }
        } catch (error) {
            try {
                const text = await response.text();
                if (text && text.trim().length > 0) {
                    message = text;
                }
            } catch (innerError) {
                console.error(innerError);
            }
        }

        throw new Error(message);
    }

    onReady(() => {
        const overlay = document.querySelector(SELECTORS.overlay);
        const dialog = document.querySelector(SELECTORS.dialog);
        if (!overlay || !dialog) {
            return;
        }

        const triggers = Array.from(document.querySelectorAll(SELECTORS.trigger));
        if (triggers.length === 0) {
            return;
        }

        const form = dialog.querySelector(SELECTORS.form);
        const closeButtons = Array.from(dialog.querySelectorAll(SELECTORS.close));
        const cancelButton = dialog.querySelector(SELECTORS.cancel);
        const submitButton = dialog.querySelector(SELECTORS.submit);
        const locationSelect = dialog.querySelector(SELECTORS.location);
        const errorNode = dialog.querySelector(SELECTORS.error);
        const root = dialog.querySelector(SELECTORS.root);
        const filterInput = dialog.querySelector(SELECTORS.filter);
        const tableBody = dialog.querySelector(SELECTORS.tbody);
        const noResultsRow = dialog.querySelector(SELECTORS.noResults);
        const checkboxSelector = 'input[type="checkbox"][data-accept-deliveries-bottle-id]';
        const submitLabel = submitButton ? (submitButton.textContent || '').trim() || 'Accept delivery' : 'Accept delivery';
        const initialLocationDisabled = locationSelect ? locationSelect.disabled : false;
        const initialCancelDisabled = cancelButton ? cancelButton.disabled : false;
        const checkboxInitialStates = new Map();
        const hasPending = root?.getAttribute('data-has-pending') === 'true';
        const hasLocations = root?.getAttribute('data-has-locations') === 'true';
        let isSubmitting = false;

        checkboxNodes().forEach(cb => {
            checkboxInitialStates.set(cb, cb.disabled);
            cb.addEventListener('change', updateSubmitState);
        });

        if (locationSelect) {
            locationSelect.addEventListener('change', updateSubmitState);
        }

        if (form) {
            form.addEventListener('submit', onSubmit);
        }

        if (filterInput) {
            filterInput.addEventListener('input', () => {
                applyFilter();
                updateSubmitState();
            });
        }

        closeButtons.forEach(button => button.addEventListener('click', closeModal));

        if (cancelButton) {
            cancelButton.addEventListener('click', closeModal);
        }

        overlay.addEventListener('click', event => {
            if (event.target === overlay) {
                closeModal();
            }
        });

        triggers.forEach(trigger => {
            trigger.addEventListener('click', event => {
                const element = event.currentTarget;
                if (element instanceof HTMLButtonElement && element.disabled) {
                    return;
                }

                openModal();
            });
        });

        applyFilter();
        updateSubmitState();

        function checkboxNodes() {
            return Array.from(dialog.querySelectorAll(checkboxSelector));
        }

        function rowNodes() {
            if (!tableBody) {
                return [];
            }

            return Array.from(tableBody.querySelectorAll(SELECTORS.row));
        }

        function applyFilter() {
            if (!tableBody) {
                return;
            }

            const query = (filterInput?.value || '').trim().toLowerCase();
            let visibleCount = 0;

            rowNodes().forEach(row => {
                const text = row.getAttribute('data-filter-text') || '';
                const isMatch = !query || text.includes(query);
                row.hidden = !isMatch;
                if (isMatch) {
                    visibleCount++;
                }
            });

            if (noResultsRow) {
                noResultsRow.hidden = visibleCount !== 0;
            }
        }

        function openModal() {
            if (isSubmitting) {
                return;
            }

            overlay.removeAttribute('hidden');
            overlay.setAttribute('aria-hidden', 'false');
            document.addEventListener('keydown', handleKeydown);
            clearError();
            applyFilter();
            updateSubmitState();
            focusInitialElement();
        }

        function closeModal() {
            if (isSubmitting) {
                return;
            }

            overlay.setAttribute('hidden', '');
            overlay.setAttribute('aria-hidden', 'true');
            document.removeEventListener('keydown', handleKeydown);
            restoreControls();
            clearError();
            resetFilter();
            updateSubmitState();
        }

        function handleKeydown(event) {
            if (event.key === 'Escape') {
                closeModal();
            }
        }

        function focusInitialElement() {
            if (filterInput && isElementVisible(filterInput) && typeof filterInput.focus === 'function') {
                filterInput.focus();
                return;
            }

            const checkbox = checkboxNodes().find(cb => !cb.disabled && isElementVisible(cb));
            if (checkbox && typeof checkbox.focus === 'function') {
                checkbox.focus();
                return;
            }

            if (locationSelect && !locationSelect.disabled) {
                locationSelect.focus();
                return;
            }

            if (cancelButton && typeof cancelButton.focus === 'function') {
                cancelButton.focus();
            }
        }

        function updateSubmitState() {
            if (!submitButton) {
                return;
            }

            if (!hasPending || !hasLocations) {
                submitButton.disabled = true;
                return;
            }

            const selectedCount = checkboxNodes().filter(cb => cb.checked && !cb.disabled).length;
            const hasLocationSelection = !!(locationSelect && locationSelect.value);
            const shouldEnable = !isSubmitting && selectedCount > 0 && hasLocationSelection;
            submitButton.disabled = !shouldEnable;
        }

        function clearError() {
            if (errorNode) {
                errorNode.textContent = '';
                errorNode.hidden = true;
            }
        }

        function setError(message) {
            if (errorNode) {
                errorNode.textContent = message || 'Unable to accept deliveries.';
                errorNode.hidden = false;
            }
        }

        function setLoadingState(isLoading) {
            if (!submitButton) {
                return;
            }

            if (isLoading) {
                submitButton.textContent = 'Accepting…';
            } else {
                submitButton.textContent = submitLabel;
            }
        }

        function disableControlsForSubmit() {
            checkboxNodes().forEach(cb => {
                cb.disabled = true;
            });

            if (locationSelect) {
                locationSelect.disabled = true;
            }

            if (cancelButton) {
                cancelButton.disabled = true;
            }
        }

        function restoreControls() {
            checkboxNodes().forEach(cb => {
                const originalState = checkboxInitialStates.get(cb);
                cb.disabled = originalState ?? false;
            });

            if (locationSelect) {
                locationSelect.disabled = initialLocationDisabled;
            }

            if (cancelButton) {
                cancelButton.disabled = initialCancelDisabled;
            }

            applyFilter();
        }

        async function onSubmit(event) {
            event.preventDefault();
            if (!form || isSubmitting) {
                return;
            }

            const selectedIds = unique(
                checkboxNodes()
                    .filter(cb => cb.checked && !cb.disabled)
                    .map(cb => cb.getAttribute('data-accept-deliveries-bottle-id'))
                    .filter(id => typeof id === 'string' && id.length > 0)
            );

            const locationId = (locationSelect?.value || '').trim();
            if (selectedIds.length === 0 || !locationId) {
                updateSubmitState();
                return;
            }

            isSubmitting = true;
            updateSubmitState();
            clearError();
            setLoadingState(true);
            disableControlsForSubmit();

            let shouldRestore = true;
            try {
                await sendAcceptRequest({
                    bottleIds: selectedIds,
                    locationId
                });
                shouldRestore = false;
                window.location.reload();
            } catch (error) {
                const message = error instanceof Error ? error.message : 'Unable to accept deliveries.';
                setError(message);
            } finally {
                if (shouldRestore) {
                    isSubmitting = false;
                    restoreControls();
                    setLoadingState(false);
                    updateSubmitState();
                }
            }
        }

        function resetFilter() {
            if (!filterInput) {
                return;
            }

            filterInput.value = '';
            applyFilter();
        }

        function isElementVisible(element) {
            if (!element) {
                return false;
            }

            if (element instanceof HTMLElement) {
                if (element.hidden) {
                    return false;
                }

                if (element.offsetParent === null && window.getComputedStyle(element).position !== 'fixed') {
                    return false;
                }
            }

            return true;
        }
    });
})();
