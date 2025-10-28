(function () {
    function onReady(callback) {
        if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', callback, { once: true });
        } else {
            callback();
        }
    }

    function markRowAsAdded(row, options = {}) {
        if (!row || row.dataset.importPreviewState === 'added') {
            return;
        }

        row.dataset.importPreviewState = 'added';
        row.classList.add('import-preview__row--added');

        const status = row.querySelector('[data-import-preview-status]');
        if (status) {
            const statusText = typeof options.statusText === 'string' && options.statusText.trim().length > 0
                ? options.statusText
                : 'Added';
            const ariaLabel = typeof options.ariaLabel === 'string' && options.ariaLabel.trim().length > 0
                ? options.ariaLabel
                : 'Row added to inventory';
            status.textContent = statusText;
            status.dataset.state = 'added';
            status.setAttribute('aria-label', ariaLabel);
        }

        const button = row.querySelector('[data-import-preview-add]');
        if (button) {
            const buttonText = typeof options.buttonText === 'string' && options.buttonText.trim().length > 0
                ? options.buttonText
                : 'Added';
            button.textContent = buttonText;
            button.classList.add('import-preview__action-button--added');
            button.disabled = true;
            button.setAttribute('aria-disabled', 'true');
        }
    }

    onReady(() => {
        const table = document.querySelector('[data-import-preview-table]');
        if (!table) {
            return;
        }

        document.addEventListener('inventoryAddModal:submitted', event => {
            const detail = event?.detail;
            if (!detail) {
                return;
            }

            const rowId = detail?.context?.rowId;
            if (!rowId) {
                return;
            }

            const selector = `[data-import-preview-row="${rowId}"]`;
            const row = table.querySelector(selector);
            if (!row) {
                return;
            }

            markRowAsAdded(row);
        });

        document.addEventListener('wineImportPreview:wineCreated', event => {
            const detail = event?.detail;
            if (!detail) {
                return;
            }

            const rowId = detail?.rowId || detail?.context?.rowId;
            if (!rowId) {
                return;
            }

            const selector = `[data-import-preview-row="${rowId}"]`;
            const row = table.querySelector(selector);
            if (!row) {
                return;
            }

            markRowAsAdded(row, {
                statusText: 'Created',
                buttonText: 'Created',
                ariaLabel: 'Wine created from import preview row'
            });
        });
    });
})();
