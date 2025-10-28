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
        row.dataset.importPreviewCanInventory = 'false';
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
        const addInventoryButton = document.querySelector('[data-import-preview-add-inventory]');
        const addInventoryStatus = document.querySelector('[data-import-preview-inventory-status]');

        if (bulkButton && typeof bulkButton.dataset.originalLabel !== 'string') {
            bulkButton.dataset.originalLabel = bulkButton.textContent?.trim() || 'Import all that do not exist yet';
        }

        if (addInventoryButton && typeof addInventoryButton.dataset.originalLabel !== 'string') {
            addInventoryButton.dataset.originalLabel = addInventoryButton.textContent?.trim() || 'Add to inventory';
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

        const getInventoryRows = () => {
            const rows = table.querySelectorAll('[data-import-preview-row]');
            return Array.from(rows).filter(row => {
                const canInventory = row.dataset.importPreviewCanInventory === 'true';
                const state = row.dataset.importPreviewState;
                return canInventory && state !== 'added';
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

        const updateInventoryButtonState = () => {
            if (!addInventoryButton || addInventoryButton.dataset.state === 'busy') {
                return;
            }

            const inventoryRows = getInventoryRows();
            const hasRows = inventoryRows.length > 0;
            addInventoryButton.disabled = !hasRows;
            addInventoryButton.setAttribute('aria-disabled', hasRows ? 'false' : 'true');
            addInventoryButton.dataset.readyCount = inventoryRows.length.toString();
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

        const setInventoryStatus = (message, state) => {
            if (!addInventoryStatus) {
                return;
            }

            const text = typeof message === 'string' ? message.trim() : '';
            addInventoryStatus.textContent = text;

            if (!text) {
                delete addInventoryStatus.dataset.state;
                return;
            }

            const normalizedState = typeof state === 'string' && state.trim().length > 0
                ? state.trim().toLowerCase()
                : 'info';
            addInventoryStatus.dataset.state = normalizedState;
        };

        const setInventoryButtonBusy = (isBusy) => {
            if (!addInventoryButton) {
                return;
            }

            if (isBusy) {
                addInventoryButton.dataset.state = 'busy';
                addInventoryButton.disabled = true;
                addInventoryButton.setAttribute('aria-disabled', 'true');
                const original = addInventoryButton.dataset.originalLabel || addInventoryButton.textContent || '';
                addInventoryButton.dataset.originalLabel = original;
                addInventoryButton.textContent = 'Adding…';
            } else {
                addInventoryButton.dataset.state = 'idle';
                const original = addInventoryButton.dataset.originalLabel || 'Add to inventory';
                addInventoryButton.textContent = original;
                updateInventoryButtonState();
            }
        };

        const parseOptionalInt = (value) => {
            const text = typeof value === 'string' ? value.trim() : '';
            if (!text) {
                return null;
            }

            const number = Number.parseInt(text, 10);
            return Number.isNaN(number) ? null : number;
        };

        const parseQuantity = (value) => {
            const text = typeof value === 'string' ? value.trim() : '';
            const number = Number.parseInt(text, 10);
            if (Number.isNaN(number) || number < 0) {
                return 0;
            }

            return number;
        };

        const parseOptionalDecimal = (value) => {
            const text = typeof value === 'string' ? value.trim() : '';
            if (!text) {
                return null;
            }

            const number = Number.parseFloat(text);
            return Number.isNaN(number) ? null : number;
        };

        const parseOptionalDateString = (value) => {
            const text = typeof value === 'string' ? value.trim() : '';
            return text.length > 0 ? text : null;
        };

        const parseOptionalText = (value) => {
            const text = typeof value === 'string' ? value.trim() : '';
            return text.length > 0 ? text : null;
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
                color: row.dataset.importPreviewColor || '',
                grapeVariety: row.dataset.importPreviewGrapeVariety || ''
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

                    if (entry?.countryCreated) {
                        const countryCell = row.querySelector('.import-preview__cell--country');
                        if (countryCell) {
                            countryCell.classList.add('import-preview__cell--match');
                            let srOnly = countryCell.querySelector('.sr-only');
                            if (!srOnly) {
                                srOnly = document.createElement('span');
                                srOnly.classList.add('sr-only');
                                srOnly.textContent = 'Country added to catalog.';
                                countryCell.appendChild(srOnly);
                            } else {
                                srOnly.textContent = 'Country added to catalog.';
                            }
                        }
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

                const countriesCreated = Number.parseInt(result?.createdCountries, 10) || 0;
                if (countriesCreated > 0) {
                    parts.push(`${countriesCreated} ${countriesCreated === 1 ? 'country' : 'countries'} added`);
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
                updateInventoryButtonState();
            }
        };

        if (bulkButton) {
            bulkButton.addEventListener('click', handleBulkImport);
        }

        const handleInventoryAdd = async () => {
            if (!addInventoryButton || addInventoryButton.disabled || addInventoryButton.dataset.state === 'busy') {
                return;
            }

            const inventoryRows = getInventoryRows();
            if (inventoryRows.length === 0) {
                setInventoryStatus('No rows with bottle details are ready to add.', 'info');
                updateInventoryButtonState();
                return;
            }

            const payloadRows = inventoryRows.map(row => ({
                rowId: row.dataset.importPreviewRow || '',
                rowNumber: Number.parseInt(row.dataset.importPreviewRowNumber || '0', 10) || 0,
                name: row.dataset.importPreviewName || '',
                country: row.dataset.importPreviewCountry || '',
                region: row.dataset.importPreviewRegion || '',
                appellation: row.dataset.importPreviewAppellation || '',
                subAppellation: row.dataset.importPreviewSubAppellation || '',
                color: row.dataset.importPreviewColor || '',
                grapeVariety: row.dataset.importPreviewGrapeVariety || '',
                vintage: parseOptionalInt(row.dataset.importPreviewVintage),
                quantity: parseQuantity(row.dataset.importPreviewAmount),
                isConsumed: row.dataset.importPreviewConsumed === 'true',
                consumptionDate: parseOptionalDateString(row.dataset.importPreviewConsumptionDate),
                consumptionScore: parseOptionalDecimal(row.dataset.importPreviewConsumptionScore),
                consumptionNote: parseOptionalText(row.dataset.importPreviewConsumptionNote)
            }));

            setInventoryButtonBusy(true);
            setInventoryStatus('Adding bottles to inventory…', 'info');

            try {
                const response = await fetch('/wine-manager/import/cellartracker/inventory', {
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
                let bottlesAdded = 0;
                let createdCount = 0;
                let existedCount = 0;
                let failedCount = 0;
                let countriesCreated = 0;

                rows.forEach(entry => {
                    const selector = buildRowSelector(entry?.rowId || '');
                    if (!selector) {
                        return;
                    }

                    const row = table.querySelector(selector);
                    if (!row) {
                        return;
                    }

                    if (entry?.error) {
                        failedCount += 1;
                        markRowAsError(row, entry.error);
                        return;
                    }

                    const entryBottles = Number.parseInt(entry?.bottlesAdded, 10);
                    if (!Number.isNaN(entryBottles) && entryBottles > 0) {
                        bottlesAdded += entryBottles;
                    }

                    if (entry?.wineCreated) {
                        createdCount += 1;
                    } else if (entry?.wineAlreadyExisted) {
                        existedCount += 1;
                    }

                    if (entry?.countryCreated) {
                        countriesCreated += 1;
                        const countryCell = row.querySelector('.import-preview__cell--country');
                        if (countryCell) {
                            countryCell.classList.add('import-preview__cell--match');
                            let srOnly = countryCell.querySelector('.sr-only');
                            if (!srOnly) {
                                srOnly = document.createElement('span');
                                srOnly.classList.add('sr-only');
                                srOnly.textContent = 'Country added to catalog.';
                                countryCell.appendChild(srOnly);
                            } else {
                                srOnly.textContent = 'Country added to catalog.';
                            }
                        }
                    }

                    markRowAsAdded(row, {
                        statusText: 'Inventory added',
                        buttonText: 'Added',
                        ariaLabel: 'Wine bottles added to inventory'
                    });
                });

                if (Number.parseInt(result?.bottlesAdded, 10) > bottlesAdded) {
                    bottlesAdded = Number.parseInt(result.bottlesAdded, 10);
                }

                if (Number.parseInt(result?.winesCreated, 10) > createdCount) {
                    createdCount = Number.parseInt(result.winesCreated, 10);
                }

                if (Number.parseInt(result?.winesAlreadyExisting, 10) > existedCount) {
                    existedCount = Number.parseInt(result.winesAlreadyExisting, 10);
                }

                if (Number.parseInt(result?.failed, 10) > failedCount) {
                    failedCount = Number.parseInt(result.failed, 10);
                }

                if (Number.parseInt(result?.createdCountries, 10) > countriesCreated) {
                    countriesCreated = Number.parseInt(result.createdCountries, 10);
                }

                const parts = [];
                if (bottlesAdded > 0) {
                    parts.push(`${bottlesAdded} ${bottlesAdded === 1 ? 'bottle added' : 'bottles added'}`);
                }

                if (createdCount > 0) {
                    parts.push(`${createdCount} ${createdCount === 1 ? 'wine created' : 'wines created'}`);
                }

                if (existedCount > 0) {
                    parts.push(`${existedCount} already existed`);
                }

                if (countriesCreated > 0) {
                    parts.push(`${countriesCreated} ${countriesCreated === 1 ? 'country' : 'countries'} added`);
                }

                if (failedCount > 0) {
                    parts.push(`${failedCount} ${failedCount === 1 ? 'error' : 'errors'}`);
                }

                const summary = parts.length > 0
                    ? `${parts.join('. ')}.`
                    : 'No bottles were added.';

                const additionalErrors = Array.isArray(result?.errors)
                    ? result.errors.filter(message => typeof message === 'string' && message.trim().length > 0)
                    : [];

                const combinedMessage = additionalErrors.length > 0
                    ? `${summary} ${additionalErrors.join(' ')}`
                    : summary;

                const statusState = failedCount > 0
                    ? 'error'
                    : bottlesAdded > 0 ? 'success' : 'info';

                setInventoryStatus(combinedMessage, statusState);
            } catch (error) {
                const message = error?.message
                    ? `Unable to add bottles: ${error.message}`
                    : 'Unable to add bottles right now.';
                setInventoryStatus(message, 'error');
            } finally {
                setInventoryButtonBusy(false);
                updateInventoryButtonState();
                updateBulkButtonState();
            }
        };

        if (addInventoryButton) {
            addInventoryButton.addEventListener('click', handleInventoryAdd);
        }

        updateBulkButtonState();
        updateInventoryButtonState();

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
            updateInventoryButtonState();
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
            updateInventoryButtonState();
        });
    });
})();
