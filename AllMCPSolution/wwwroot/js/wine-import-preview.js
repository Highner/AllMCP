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
        row.classList.remove('import-preview__row--error');

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
            status.removeAttribute('title');
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

        const bulkButton = document.querySelector('[data-import-preview-import-all]');
        const bulkStatus = document.querySelector('[data-import-preview-bulk-status]');

        if (bulkButton && typeof bulkButton.dataset.originalLabel !== 'string') {
            bulkButton.dataset.originalLabel = bulkButton.textContent?.trim() || 'Import all that do not exist yet';
        }

        const buildRowSelector = (rowId) => {
            if (typeof rowId !== 'string' || rowId.trim().length === 0) {
                return null;
            }

            if (typeof CSS !== 'undefined' && typeof CSS.escape === 'function') {
                return `[data-import-preview-row="${CSS.escape(rowId)}"]`;
            }

            const safe = rowId.replace(/"/g, '\\"');
            return `[data-import-preview-row="${safe}"]`;
        };

        const markRowAsExisting = (row) => {
            if (!row) {
                return;
            }

            row.dataset.importPreviewState = 'existing';
            row.classList.remove('import-preview__row--added');
            row.classList.remove('import-preview__row--error');

            const status = row.querySelector('[data-import-preview-status]');
            if (status) {
                status.textContent = 'Exists';
                status.dataset.state = 'existing';
                status.setAttribute('aria-label', 'Wine already exists in catalog');
                status.removeAttribute('title');
            }

            const button = row.querySelector('[data-import-preview-add]');
            if (button) {
                button.textContent = 'Create';
                button.classList.remove('import-preview__action-button--added');
                button.disabled = false;
                button.removeAttribute('aria-disabled');
            }
        };

        const markRowAsError = (row, message) => {
            if (!row) {
                return;
            }

            row.dataset.importPreviewState = 'error';
            row.classList.add('import-preview__row--error');

            const status = row.querySelector('[data-import-preview-status]');
            if (status) {
                status.textContent = 'Error';
                status.dataset.state = 'error';
                const errorMessage = typeof message === 'string' && message.trim().length > 0
                    ? message.trim()
                    : 'Unable to import this wine';
                status.setAttribute('aria-label', errorMessage);
                status.setAttribute('title', errorMessage);
            }
        };

        const getReadyRows = () => {
            const rows = table.querySelectorAll('[data-import-preview-row]');
            return Array.from(rows).filter(row => {
                const status = row.querySelector('[data-import-preview-status]');
                return status && status.dataset.state === 'ready';
            });
        };

        const updateBulkButtonState = () => {
            if (!bulkButton || bulkButton.dataset.state === 'busy') {
                return;
            }

            const readyRows = getReadyRows();
            const hasReady = readyRows.length > 0;
            bulkButton.disabled = !hasReady;
            bulkButton.setAttribute('aria-disabled', hasReady ? 'false' : 'true');
            bulkButton.dataset.readyCount = readyRows.length.toString();
        };

        const setBulkStatus = (message, state) => {
            if (!bulkStatus) {
                return;
            }

            const text = typeof message === 'string' ? message.trim() : '';
            bulkStatus.textContent = text;

            if (!text) {
                delete bulkStatus.dataset.state;
                return;
            }

            const normalizedState = typeof state === 'string' && state.trim().length > 0
                ? state.trim().toLowerCase()
                : 'info';
            bulkStatus.dataset.state = normalizedState;
        };

        const setBulkButtonBusy = (isBusy) => {
            if (!bulkButton) {
                return;
            }

            if (isBusy) {
                bulkButton.dataset.state = 'busy';
                bulkButton.disabled = true;
                bulkButton.setAttribute('aria-disabled', 'true');
                const original = bulkButton.dataset.originalLabel || bulkButton.textContent || '';
                bulkButton.dataset.originalLabel = original;
                bulkButton.textContent = 'Importing…';
            } else {
                bulkButton.dataset.state = 'idle';
                const original = bulkButton.dataset.originalLabel || 'Import all that do not exist yet';
                bulkButton.textContent = original;
                updateBulkButtonState();
            }
        };

        const handleBulkImport = async () => {
            if (!bulkButton || bulkButton.disabled || bulkButton.dataset.state === 'busy') {
                return;
            }

            const readyRows = getReadyRows();
            if (readyRows.length === 0) {
                setBulkStatus('All wines already exist in the catalog.', 'info');
                updateBulkButtonState();
                return;
            }

            const payloadRows = readyRows.map(row => ({
                rowId: row.dataset.importPreviewRow || '',
                rowNumber: Number.parseInt(row.dataset.importPreviewRowNumber || '0', 10) || 0,
                name: row.dataset.importPreviewName || '',
                country: row.dataset.importPreviewCountry || '',
                region: row.dataset.importPreviewRegion || '',
                appellation: row.dataset.importPreviewAppellation || '',
                subAppellation: row.dataset.importPreviewSubAppellation || '',
                color: row.dataset.importPreviewColor || ''
            }));

            setBulkButtonBusy(true);
            setBulkStatus('Importing wines…', 'info');

            try {
                const response = await fetch('/wine-manager/import/wines/bulk', {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json'
                    },
                    body: JSON.stringify({ rows: payloadRows })
                });

                if (!response.ok) {
                    let message = `${response.status} ${response.statusText}`;
                    try {
                        const problem = await response.json();
                        if (problem?.message) {
                            message = problem.message;
                        } else if (problem?.title) {
                            message = problem.title;
                        }
                    } catch {
                        const text = await response.text();
                        if (text) {
                            message = text;
                        }
                    }

                    throw new Error(message);
                }

                const result = await response.json();
                const rows = Array.isArray(result?.rows) ? result.rows : [];
                let createdCount = 0;
                let existsCount = 0;
                let failedCount = 0;

                rows.forEach(entry => {
                    const selector = buildRowSelector(entry?.rowId || '');
                    if (!selector) {
                        return;
                    }

                    const row = table.querySelector(selector);
                    if (!row) {
                        return;
                    }

                    if (entry?.created) {
                        createdCount += 1;
                        markRowAsAdded(row, {
                            statusText: 'Created',
                            buttonText: 'Created',
                            ariaLabel: 'Wine created from import preview row'
                        });
                    } else if (entry?.alreadyExists) {
                        existsCount += 1;
                        markRowAsExisting(row);
                    } else if (entry?.error) {
                        failedCount += 1;
                        markRowAsError(row, entry.error);
                    }
                });

                const parts = [];
                if (result?.created) {
                    const count = Number.parseInt(result.created, 10) || createdCount;
                    if (count > 0) {
                        parts.push(`${count} wine${count === 1 ? '' : 's'} imported`);
                    }
                } else if (createdCount > 0) {
                    parts.push(`${createdCount} wine${createdCount === 1 ? '' : 's'} imported`);
                }

                const totalExists = Number.parseInt(result?.alreadyExists, 10) || existsCount;
                if (totalExists > 0) {
                    parts.push(`${totalExists} already existed`);
                }

                const totalFailed = Number.parseInt(result?.failed, 10) || failedCount;
                if (totalFailed > 0) {
                    parts.push(`${totalFailed} ${totalFailed === 1 ? 'error' : 'errors'}`);
                }

                const summary = parts.length > 0
                    ? `${parts.join('. ')}.`
                    : 'No wines were imported.';

                const additionalErrors = Array.isArray(result?.errors)
                    ? result.errors.filter(message => typeof message === 'string' && message.trim().length > 0)
                    : [];

                const combinedMessage = additionalErrors.length > 0
                    ? `${summary} ${additionalErrors.join(' ')}`
                    : summary;

                const statusState = totalFailed > 0
                    ? 'error'
                    : (createdCount > 0 || (Number.parseInt(result?.created, 10) || 0) > 0) ? 'success' : 'info';

                setBulkStatus(combinedMessage, statusState);
            } catch (error) {
                const message = error?.message
                    ? `Unable to import wines: ${error.message}`
                    : 'Unable to import wines right now.';
                setBulkStatus(message, 'error');
            } finally {
                setBulkButtonBusy(false);
                updateBulkButtonState();
            }
        };

        if (bulkButton) {
            bulkButton.addEventListener('click', handleBulkImport);
        }

        updateBulkButtonState();

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
            updateBulkButtonState();
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
            updateBulkButtonState();
        });
    });
})();
