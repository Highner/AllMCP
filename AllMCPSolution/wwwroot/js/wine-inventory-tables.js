window.WineInventoryTables = window.WineInventoryTables || {};
window.WineInventoryTables.initialize = function () {
            const inventoryTable = document.getElementById('inventory-table');
            const detailsTable = document.getElementById('details-table');
            const detailsBody = detailsTable?.querySelector('tbody');
            const detailAddRow = detailsTable?.querySelector('#detail-add-row');
            const emptyRow = detailsBody?.querySelector('.empty-row');
            const detailsTitle = document.getElementById('details-title');
            const detailsSubtitle = document.getElementById('details-subtitle');
            const messageBanner = document.getElementById('details-message');
            const detailsPanel = document.querySelector('.details-panel');
            const notesPanel = document.getElementById('notes-panel');
            const notesTable = document.getElementById('notes-table');
            const notesBody = notesTable?.querySelector('tbody');
            const notesAddRow = notesTable?.querySelector('#note-add-row');
            const notesEmptyRow = notesBody?.querySelector('.empty-row');
            const notesAddUser = notesAddRow?.querySelector('.note-add-user');
            const notesAddScore = notesAddRow?.querySelector('.note-add-score');
            const notesAddText = notesAddRow?.querySelector('.note-add-text');
            const notesAddButton = notesAddRow?.querySelector('.note-add-submit');
            const notesMessage = document.getElementById('notes-message');
            const notesTitle = document.getElementById('notes-title');
            const notesSubtitle = document.getElementById('notes-subtitle');
            const notesCloseButton = document.getElementById('notes-close');

            if (!inventoryTable || !detailsTable || !detailsBody || !detailAddRow || !emptyRow || !detailsTitle || !detailsSubtitle || !messageBanner || !detailsPanel || !notesPanel || !notesTable || !notesBody || !notesAddRow || !notesEmptyRow || !notesAddUser || !notesAddScore || !notesAddText || !notesAddButton || !notesMessage || !notesTitle || !notesSubtitle || !notesCloseButton) {
                return;
            }

            const summaryAddRow = inventoryTable.querySelector('.add-summary-row');
            const summaryAddName = summaryAddRow?.querySelector('.summary-add-name');
            const summaryAddSubApp = summaryAddRow?.querySelector('.summary-add-sub-app');
            const summaryAddVintage = summaryAddRow?.querySelector('.summary-add-vintage');
            const summaryAddCount = summaryAddRow?.querySelector('.summary-add-count');
            const summaryAddColor = summaryAddRow?.querySelector('.summary-add-color');
            const summaryAddButton = summaryAddRow?.querySelector('.summary-add-submit');

            const detailAddLocation = detailAddRow.querySelector('.detail-add-location');
            const detailAddUser = detailAddRow.querySelector('.detail-add-user');
            const detailAddPrice = detailAddRow.querySelector('.detail-add-price');
            const detailAddIsDrunk = detailAddRow.querySelector('.detail-add-is-drunk');
            const detailAddLabel = detailAddRow.querySelector('.checkbox-row span');
            const detailAddDrunkAt = detailAddRow.querySelector('.detail-add-drunk-at');
            const detailAddButton = detailAddRow.querySelector('.detail-add-submit');

            let selectedGroupId = null;
            let selectedSummary = null;
            let selectedRow = null;
            let notesSelectedBottleId = null;
            let selectedDetailRowElement = null;
            let loading = false;
            let notesLoading = false;

            const referenceData = {
                subAppellations: [],
                bottleLocations: [],
                users: []
            };
            const referenceDataPromise = loadReferenceData();

            initializeSummaryRows();
            bindSummaryAddRow();
            bindDetailAddRow();
            bindNotesPanel();

            function initializeSummaryRows() {
                const rows = Array.from(inventoryTable.querySelectorAll('tbody tr.group-row'));
                rows.forEach(attachSummaryRowHandlers);
            }

            function attachSummaryRowHandlers(row) {
                row.addEventListener('click', (event) => {
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

                    if (target.closest('.summary-actions')) {
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
                        handleRowSelection(row).catch(error => showMessage(error?.message ?? String(error), 'error'));
                    }
                });
            }

            function bindSummaryAddRow() {
                if (!summaryAddButton) {
                    return;
                }

                summaryAddButton.addEventListener('click', async () => {
                    await referenceDataPromise;

                    if (loading) {
                        return;
                    }

                    const name = summaryAddName?.value?.trim() ?? '';
                    const subAppellationId = summaryAddSubApp?.value ?? '';
                    const vintageValue = Number(summaryAddVintage?.value ?? '');
                    const bottleCountValue = Number(summaryAddCount?.value ?? '');
                    const color = summaryAddColor?.value ?? '';

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

                    if (!Number.isInteger(bottleCountValue) || bottleCountValue < 1) {
                        showMessage('Initial bottle count must be at least 1.', 'error');
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
                        color,
                        initialBottleCount: bottleCountValue
                    };

                    try {
                        setLoading(true);
                        const response = await sendJson('/wine-manager/groups', {
                            method: 'POST',
                            body: JSON.stringify(payload)
                        });

                        if (summaryAddName) {
                            summaryAddName.value = '';
                        }

                        if (summaryAddSubApp) {
                            summaryAddSubApp.value = '';
                        }

                        if (summaryAddVintage) {
                            summaryAddVintage.value = '';
                        }

                        if (summaryAddCount) {
                            summaryAddCount.value = '1';
                        }

                        if (summaryAddColor) {
                            summaryAddColor.value = 'Red';
                        }

                        const summary = normalizeSummary(response?.group ?? response?.Group);
                        if (!summary) {
                            showMessage('Wine group created, but it could not be displayed.', 'warning');
                            return;
                        }

                        const newRow = buildSummaryRow(summary);
                        attachSummaryRowHandlers(newRow);
                        inventoryTable.querySelector('tbody')?.appendChild(newRow);
                        showMessage('Wine group created.', 'success');
                        await handleRowSelection(newRow, { force: true, response });
                        newRow.focus();
                    } catch (error) {
                        showMessage(error.message, 'error');
                    } finally {
                        setLoading(false);
                    }
                });
            }

            function bindDetailAddRow() {
                detailAddIsDrunk?.addEventListener('change', () => {
                    if (detailAddIsDrunk.checked) {
                        detailAddDrunkAt.removeAttribute('disabled');
                        if (detailAddLabel) {
                            detailAddLabel.textContent = 'Yes';
                        }
                    } else {
                        detailAddDrunkAt.value = '';
                        detailAddDrunkAt.setAttribute('disabled', 'disabled');
                        if (detailAddLabel) {
                            detailAddLabel.textContent = 'No';
                        }
                    }
                });

                detailAddButton?.addEventListener('click', handleAddBottle);
            }

            function bindNotesPanel() {
                initializeNotesPanel();
                notesCloseButton.addEventListener('click', closeNotesPanel);
                notesAddButton.addEventListener('click', handleAddNote);
            }

            function initializeNotesPanel() {
                if (notesAddUser) {
                    notesAddUser.innerHTML = '';
                    const placeholder = document.createElement('option');
                    placeholder.value = '';
                    placeholder.textContent = 'Select user';
                    notesAddUser.appendChild(placeholder);
                }

                clearNotesAddInputs();
                disableNotesAddRow(true);
                setNotesHeader(null);
                showNotesMessage('', 'info');
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
                    referenceData.bottleLocations = locations;
                    referenceData.users = users;

                    populateSubAppellationSelect(summaryAddSubApp, summaryAddSubApp?.value ?? '');
                    populateLocationSelect(detailAddLocation, detailAddLocation?.value ?? '');
                    populateUserSelect(detailAddUser, detailAddUser?.value ?? '');
                    populateUserSelect(notesAddUser, notesAddUser?.value ?? '');
                    if (notesAddUser.firstElementChild) {
                        notesAddUser.firstElementChild.textContent = 'Select user';
                    }
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
                    const id = option?.id ?? option?.Id;
                    const name = option?.name ?? option?.Name;
                    if (!id) {
                        return;
                    }

                    const opt = document.createElement('option');
                    opt.value = id;
                    opt.textContent = name ?? id;
                    select.appendChild(opt);
                });

                if (previousValue) {
                    select.value = previousValue;
                }
            }

            function populateUserSelect(select, selectedId) {
                if (!select) {
                    return;
                }

                const previousValue = selectedId ?? select.value ?? '';
                select.innerHTML = '';

                const placeholder = document.createElement('option');
                placeholder.value = '';
                placeholder.textContent = 'No user';
                select.appendChild(placeholder);

                referenceData.users.forEach(option => {
                    const id = option?.id ?? option?.Id;
                    const name = option?.name ?? option?.Name;
                    if (!id) {
                        return;
                    }

                    const opt = document.createElement('option');
                    opt.value = id;
                    opt.textContent = name ?? id;
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
                    <td class="summary-score" data-field="score"></td>
                    <td class="summary-actions">
                        <div class="actions">
                            <button type="button" class="crud-table__action-button secondary edit-group">Edit</button>
                            <button type="button" class="crud-table__action-button secondary delete-group">Delete</button>
                        </div>
                    </td>`;
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
                const actionsCell = row.querySelector('.summary-actions .actions');

                if (wineCell) {
                    wineCell.textContent = summary?.wineName ?? summary?.WineName ?? '';
                }

                if (appCell) {
                    appCell.textContent = buildAppellationDisplay(summary);
                }

                if (vintageCell) {
                    const vintageValue = summary?.vintage ?? summary?.Vintage;
                    vintageCell.textContent = vintageValue != null ? String(vintageValue) : '';
                }

                if (bottlesCell) {
                    const count = summary?.bottleCount ?? summary?.BottleCount ?? 0;
                    bottlesCell.textContent = Number(count).toString();
                }

                if (colorCell) {
                    colorCell.textContent = summary?.color ?? summary?.Color ?? '';
                }

                if (statusSpan) {
                    const label = summary?.statusLabel ?? summary?.StatusLabel ?? '';
                    const cssClass = summary?.statusCssClass ?? summary?.StatusCssClass ?? '';
                    statusSpan.textContent = label;
                    statusSpan.className = `status-pill ${cssClass}`;
                }

                if (scoreCell) {
                    const score = summary?.averageScore ?? summary?.AverageScore;
                    scoreCell.textContent = score != null ? Number(score).toFixed(1) : '—';
                }

                if (actionsCell) {
                    actionsCell.innerHTML = `
                        <button type="button" class="crud-table__action-button secondary edit-group">Edit</button>
                        <button type="button" class="crud-table__action-button secondary delete-group">Delete</button>`;
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
                        selectedGroupId = null;
                        selectedSummary = null;
                        selectedRow = null;
                        closeNotesPanel();
                        detailsTitle.textContent = 'Bottle Details';
                        detailsSubtitle.textContent = 'Select a wine group to view individual bottles.';
                        detailAddRow.hidden = true;
                        emptyRow.hidden = false;
                        detailsBody.querySelectorAll('.detail-row').forEach(r => r.remove());
                    }

                    row.remove();
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

            async function handleRowSelection(row, options = {}) {
                if (loading && !options.force) {
                    return;
                }

                const groupId = row.dataset.groupId;
                if (!groupId) {
                    return;
                }

                if (selectedGroupId === groupId && !options.force) {
                    return;
                }

                if (selectedRow && selectedRow !== row) {
                    selectedRow.classList.remove('selected');
                    selectedRow.setAttribute('aria-expanded', 'false');
                }

                selectedRow = row;
                selectedGroupId = groupId;
                row.classList.add('selected');
                row.setAttribute('aria-expanded', 'true');

                if (options.response) {
                    await renderDetails(options.response, true);
                    return;
                }

                await loadDetails(groupId, false);
            }

            async function loadDetails(groupId, updateRow) {
                showMessage('Loading bottle details…', 'info');
                emptyRow.hidden = false;
                detailsBody.querySelectorAll('.detail-row').forEach(r => r.remove());

                try {
                    setLoading(true);
                    const response = await sendJson(`/wine-manager/bottles/${groupId}`, { method: 'GET' });
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
                    detailsTitle.textContent = `${summary.wineName ?? ''} • ${summary.vintage ?? ''}`;
                    detailsSubtitle.textContent = `${summary.bottleCount ?? 0} bottle${summary.bottleCount === 1 ? '' : 's'} · ${summary.statusLabel ?? ''}`;
                    detailAddRow.hidden = false;
                    detailAddPrice.value = '';
                    detailAddIsDrunk.checked = false;
                    if (detailAddLabel) {
                    detailAddLabel.textContent = 'No';
                }
                detailAddDrunkAt.value = '';
                detailAddDrunkAt.setAttribute('disabled', 'disabled');
                populateLocationSelect(detailAddLocation, '');
                populateUserSelect(detailAddUser, '');
                if (detailAddUser) {
                    detailAddUser.value = '';
                }
                detailAddButton.disabled = loading;
                populateUserSelect(notesAddUser, notesAddUser?.value ?? '');
                if (notesAddUser.firstElementChild) {
                    notesAddUser.firstElementChild.textContent = 'Select user';
                }
                disableNotesAddRow(notesLoading || !notesSelectedBottleId);
            } else {
                detailsTitle.textContent = 'Bottle Details';
                detailsSubtitle.textContent = 'No bottles remain for the selected group.';
                detailAddRow.hidden = true;
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

                if (shouldUpdateRow) {
                    updateSummaryRow(summary ?? null);
                }
            }

            function updateSummaryRow(summary) {
                if (!selectedGroupId) {
                    return;
                }

                const row = inventoryTable.querySelector(`tr[data-group-id="${selectedGroupId}"]`);
                if (!row) {
                    return;
                }

                if (!summary) {
                    row.remove();
                    selectedRow = null;
                    selectedGroupId = null;
                    return;
                }

                applySummaryToRow(row, summary);
            }

            function buildDetailRow(detail, summary) {
                const row = document.createElement('tr');
                row.className = 'detail-row';
                row.dataset.bottleId = detail.bottleId ?? detail.BottleId ?? '';

                const formattedDate = formatDateTime(detail.drunkAt ?? detail.DrunkAt);
                const isDrunk = Boolean(detail.isDrunk ?? detail.IsDrunk);

                row.innerHTML = `
                    <td></td>
                    <td></td>
                    <td><input type="number" step="0.01" min="0" class="detail-price" value="${detail.price ?? detail.Price ?? ''}" placeholder="0.00" /></td>
                    <td class="detail-average">${formatScore(detail.averageScore ?? detail.AverageScore)}</td>
                    <td>
                        <label class="checkbox-row">
                            <input type="checkbox" class="detail-is-drunk" ${isDrunk ? 'checked' : ''} />
                            <span>${isDrunk ? 'Yes' : 'No'}</span>
                        </label>
                    </td>
                    <td><input type="datetime-local" class="detail-drunk-at" value="${formattedDate}" ${isDrunk ? '' : 'disabled'} /></td>
                    <td class="actions">
                        <button type="button" class="crud-table__action-button save">Save</button>
                        <button type="button" class="crud-table__action-button secondary delete">Remove</button>
                    </td>`;

                const locationCell = row.children[0];
                const userCell = row.children[1];
                const locationSelect = document.createElement('select');
                locationSelect.className = 'detail-location';
                populateLocationSelect(locationSelect, detail.bottleLocationId ?? detail.BottleLocationId ?? '');
                locationCell.appendChild(locationSelect);

                const userSelect = document.createElement('select');
                userSelect.className = 'detail-user';
                userSelect.setAttribute('aria-label', 'Bottle owner');
                const selectedUserId = detail.userId ?? detail.UserId ?? '';
                const selectedUserName = detail.userName ?? detail.UserName ?? '';
                populateUserSelect(userSelect, selectedUserId || '');
                if (selectedUserId && userSelect.value !== selectedUserId) {
                    const fallbackOption = document.createElement('option');
                    fallbackOption.value = selectedUserId;
                    fallbackOption.textContent = selectedUserName || selectedUserId;
                    userSelect.appendChild(fallbackOption);
                    userSelect.value = selectedUserId;
                }
                userCell.appendChild(userSelect);

                const drunkCheckbox = row.querySelector('.detail-is-drunk');
                const drunkLabel = row.querySelector('.detail-is-drunk')?.nextElementSibling;
                const drunkInput = row.querySelector('.detail-drunk-at');
                const saveButton = row.querySelector('.save');
                const deleteButton = row.querySelector('.delete');

                drunkCheckbox?.addEventListener('change', () => {
                    if (drunkCheckbox.checked) {
                        drunkInput?.removeAttribute('disabled');
                        if (drunkLabel) {
                            drunkLabel.textContent = 'Yes';
                        }
                    } else {
                        drunkInput?.setAttribute('disabled', 'disabled');
                        if (drunkInput) {
                            drunkInput.value = '';
                        }
                        if (drunkLabel) {
                            drunkLabel.textContent = 'No';
                        }
                    }
                });

                saveButton?.addEventListener('click', async () => {
                    if (!selectedSummary || loading) {
                        return;
                    }

                    const payload = {
                        wineVintageId: selectedSummary.wineVintageId,
                        price: parsePrice(row.querySelector('.detail-price')?.value ?? ''),
                        isDrunk: drunkCheckbox?.checked ?? false,
                        drunkAt: parseDateTime(drunkInput?.value ?? ''),
                        bottleLocationId: locationSelect.value || null,
                        userId: userSelect.value || null
                    };

                    try {
                        setRowLoading(row, true);
                        const response = await sendJson(`/wine-manager/bottles/${detail.bottleId ?? detail.BottleId}`, {
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

                return row;
            }

            async function handleAddBottle() {
                if (!selectedSummary || loading) {
                    return;
                }

                const payload = {
                    wineVintageId: selectedSummary.wineVintageId,
                    price: parsePrice(detailAddPrice.value),
                    isDrunk: detailAddIsDrunk.checked,
                    drunkAt: parseDateTime(detailAddDrunkAt.value),
                    bottleLocationId: detailAddLocation.value || null,
                    userId: detailAddUser?.value || null
                };

                try {
                    setLoading(true);
                    const response = await sendJson('/wine-manager/bottles', {
                        method: 'POST',
                        body: JSON.stringify(payload)
                    });

                    detailAddPrice.value = '';
                    detailAddIsDrunk.checked = false;
                    if (detailAddLabel) {
                        detailAddLabel.textContent = 'No';
                    }
                    detailAddDrunkAt.value = '';
                    detailAddDrunkAt.setAttribute('disabled', 'disabled');
                    detailAddLocation.value = '';
                    if (detailAddUser) {
                        detailAddUser.value = '';
                    }

                    await renderDetails(response, true);
                    showMessage('Bottle added successfully.', 'success');
                } catch (error) {
                    showMessage(error.message, 'error');
                } finally {
                    setLoading(false);
                }
            }

            function setLoading(state) {
                loading = state;
                if (summaryAddButton) {
                    summaryAddButton.disabled = state;
                }
                if (detailAddButton) {
                    detailAddButton.disabled = state || detailAddRow.hidden;
                }
                if (detailAddUser) {
                    detailAddUser.disabled = state || detailAddRow.hidden;
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
                if (state) {
                    row.classList.add('loading');
                } else {
                    row.classList.remove('loading');
                }

                row.querySelectorAll('input, button, select').forEach(element => {
                    element.disabled = state;
                });
            }

            function disableNotesAddRow(disabled) {
                if (notesAddUser) {
                    notesAddUser.disabled = disabled;
                }
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
                if (notesAddUser) {
                    notesAddUser.value = '';
                }
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
                    return;
                }

                const wineName = summary.wineName ?? '';
                const vintage = summary.vintage ?? '';
                notesTitle.textContent = wineName && vintage ? `${wineName} • ${vintage}` : wineName || 'Consumption Notes';

                const parts = [];

                if (typeof noteCount === 'number') {
                    parts.push(`${noteCount} note${noteCount === 1 ? '' : 's'}`);
                }

                const owner = summary.userName ?? '';
                if (owner) {
                    parts.push(`Owner: ${owner}`);
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
                if (!row || !detail) {
                    return;
                }

                await referenceDataPromise;
                await openNotesPanel(row, detail, summary);
            }

            async function openNotesPanel(row, detail, summary) {
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
                    userName: detail?.userName ?? detail?.UserName ?? '',
                    isDrunk: Boolean(detail?.isDrunk ?? detail?.IsDrunk),
                    drunkAt: detail?.drunkAt ?? detail?.DrunkAt ?? null
                };

                setNotesHeader(headerSummary);
                showNotesMessage('', 'info');

                populateUserSelect(notesAddUser, notesAddUser?.value ?? '');
                if (notesAddUser.firstElementChild) {
                    notesAddUser.firstElementChild.textContent = 'Select user';
                }
                clearNotesAddInputs();
                disableNotesAddRow(notesLoading);

                await loadNotesForBottle(bottleId);
            }

            async function loadNotesForBottle(bottleId) {
                if (!notesBody) {
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
                if (!notesBody) {
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

                const scoreValue = note.score ?? note.Score ?? '';
                const noteText = note.note ?? note.Note ?? '';

                row.innerHTML = `
                    <td></td>
                    <td><input type="number" class="note-score" min="0" max="10" step="0.1" value="${scoreValue}" placeholder="0-10" /></td>
                    <td><textarea class="note-text" rows="3">${escapeHtml(noteText)}</textarea></td>
                    <td class="actions">
                        <button type="button" class="crud-table__action-button save-note">Save</button>
                        <button type="button" class="crud-table__action-button secondary delete-note">Delete</button>
                    </td>`;

                const userCell = row.children[0];
                const userSelect = document.createElement('select');
                userSelect.className = 'note-user';
                userSelect.setAttribute('aria-label', 'Note author');
                populateUserSelect(userSelect, note.userId ?? note.UserId ?? '');
                if (userSelect.firstElementChild) {
                    userSelect.firstElementChild.textContent = 'Select user';
                }
                const selectedUserId = note.userId ?? note.UserId ?? '';
                if (selectedUserId && userSelect.value !== selectedUserId) {
                    const fallbackOption = document.createElement('option');
                    fallbackOption.value = selectedUserId;
                    fallbackOption.textContent = note.userName ?? note.UserName ?? selectedUserId;
                    userSelect.appendChild(fallbackOption);
                    userSelect.value = selectedUserId;
                }
                userCell.appendChild(userSelect);

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

                    const userId = userSelect.value;
                    if (!userId) {
                        showNotesMessage('Select an author for the note.', 'error');
                        return;
                    }

                    const parsedScore = parseScore(scoreInput?.value ?? '');
                    if (parsedScore === undefined) {
                        showNotesMessage('Score must be between 0 and 10.', 'error');
                        return;
                    }

                    const payload = {
                        note: noteValue,
                        userId,
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

                return row;
            }

            function closeNotesPanel() {
                if (!detailsPanel || !notesPanel) {
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
                if (!notesSelectedBottleId || notesLoading) {
                    return;
                }

                const noteValue = notesAddText?.value?.trim() ?? '';
                if (!noteValue) {
                    showNotesMessage('Note text is required.', 'error');
                    return;
                }

                const userId = notesAddUser?.value ?? '';
                if (!userId) {
                    showNotesMessage('Select an author for the note.', 'error');
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
                    userId,
                    score: parsedScore
                };

                try {
                    setNotesLoading(true);
                    const response = await sendJson('/wine-manager/notes', {
                        method: 'POST',
                        body: JSON.stringify(payload)
                    });
                    renderNotes(response);
                    showNotesMessage('Note added.', 'success');
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
                    wineName: pick(raw, ['wineName', 'WineName']) ?? '',
                    vintage: pick(raw, ['vintage', 'Vintage']),
                    bottleLocation: pick(raw, ['bottleLocation', 'BottleLocation']) ?? '',
                    userName: pick(raw, ['userName', 'UserName']) ?? '',
                    isDrunk: Boolean(pick(raw, ['isDrunk', 'IsDrunk'])),
                    drunkAt: pick(raw, ['drunkAt', 'DrunkAt'])
                };
            }

            function normalizeNote(raw) {
                if (!raw) {
                    return null;
                }

                return {
                    id: pick(raw, ['id', 'Id']),
                    note: pick(raw, ['note', 'Note']) ?? '',
                    score: pick(raw, ['score', 'Score']),
                    userId: pick(raw, ['userId', 'UserId']) ?? '',
                    userName: pick(raw, ['userName', 'UserName']) ?? ''
                };
            }

            function parsePrice(value) {
                if (!value) {
                    return null;
                }

                const parsed = Number.parseFloat(value);
                return Number.isFinite(parsed) ? parsed : null;
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

                return {
                    bottleId: pick(raw, ['bottleId', 'BottleId']),
                    price: pick(raw, ['price', 'Price']),
                    isDrunk: Boolean(pick(raw, ['isDrunk', 'IsDrunk'])),
                    drunkAt: pick(raw, ['drunkAt', 'DrunkAt']),
                    bottleLocationId: pick(raw, ['bottleLocationId', 'BottleLocationId']),
                    bottleLocation: pick(raw, ['bottleLocation', 'BottleLocation']),
                    userId: pick(raw, ['userId', 'UserId']),
                    userName: pick(raw, ['userName', 'UserName']) ?? '',
                    vintage: pick(raw, ['vintage', 'Vintage']),
                    wineName: pick(raw, ['wineName', 'WineName']),
                    averageScore: pick(raw, ['averageScore', 'AverageScore'])
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
        
};

window.addEventListener('DOMContentLoaded', () => {
    window.WineInventoryTables.initialize();
});
