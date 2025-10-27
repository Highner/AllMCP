(function () {
    function onReady(callback) {
        if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', callback, { once: true });
        } else {
            callback();
        }
    }

    function markRowAsAdded(row) {
        if (!row || row.dataset.importPreviewState === 'added') {
            return;
        }

        row.dataset.importPreviewState = 'added';
        row.classList.add('import-preview__row--added');

        const status = row.querySelector('[data-import-preview-status]');
        if (status) {
            status.textContent = 'Added';
            status.dataset.state = 'added';
            status.setAttribute('aria-label', 'Row added to inventory');
        }

        const button = row.querySelector('[data-import-preview-add]');
        if (button) {
            button.textContent = 'Added';
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
    });
})();
