(() => {
    const NOTE_ROW_SELECTOR = '[data-note-row]';
    const INTERACTIVE_SELECTOR = 'a, button, input, select, textarea, [role="button"]';
    const NOT_TASTED_MESSAGE = 'You marked this bottle as not tasted.';
    const EMPTY_NOTE_MESSAGE = 'No tasting note recorded yet.';

    const rowsByNoteId = new Map();

    const formatScoreDisplay = (value) => {
        if (value == null || value === '') {
            return '—';
        }

        const number = Number(value);
        if (!Number.isFinite(number)) {
            return '—';
        }

        return number.toFixed(1);
    };

    const extractProblemMessage = (data, fallback) => {
        if (!data || typeof data !== 'object') {
            return fallback;
        }

        const messageCandidates = [data.message, data.detail, data.title];
        for (const candidate of messageCandidates) {
            if (typeof candidate === 'string' && candidate.trim()) {
                return candidate.trim();
            }
        }

        const errors = data.errors;
        if (errors && typeof errors === 'object') {
            for (const key of Object.keys(errors)) {
                const value = errors[key];
                if (Array.isArray(value) && value.length > 0) {
                    const message = value.find(item => typeof item === 'string' && item.trim());
                    if (message) {
                        return message.trim();
                    }
                } else if (typeof value === 'string' && value.trim()) {
                    return value.trim();
                }
            }
        }

        return fallback;
    };

    const updateRowDisplay = (row, { note, score, notTasted }) => {
        if (!(row instanceof HTMLElement)) {
            return;
        }

        const normalizedNote = typeof note === 'string' ? note : '';
        const trimmedNote = normalizedNote.trim();
        const hasScore = score != null && score !== '' && Number.isFinite(Number(score));

        row.dataset.noteText = notTasted ? '' : normalizedNote;
        row.dataset.noteScore = hasScore && !notTasted ? String(Number(score)) : '';
        row.dataset.noteNotTasted = notTasted ? 'true' : 'false';

        const noteElement = row.querySelector('.notes-table__note');
        if (noteElement) {
            if (notTasted) {
                noteElement.textContent = NOT_TASTED_MESSAGE;
            } else if (trimmedNote) {
                noteElement.textContent = normalizedNote;
            } else {
                noteElement.textContent = EMPTY_NOTE_MESSAGE;
            }
        }

        const scoreCell = row.querySelector('.summary-cell--score');
        if (scoreCell) {
            scoreCell.textContent = notTasted ? '—' : formatScoreDisplay(hasScore ? Number(score) : null);
        }
    };

    const getRowByNoteId = (noteId) => {
        if (!noteId) {
            return null;
        }

        if (rowsByNoteId.has(noteId)) {
            return rowsByNoteId.get(noteId) ?? null;
        }

        const escapedId = typeof CSS !== 'undefined' && typeof CSS.escape === 'function'
            ? CSS.escape(noteId)
            : noteId.replace(/"/g, '\\"');

        const found = document.querySelector(`[data-note-row][data-note-id="${escapedId}"]`);
        if (found) {
            rowsByNoteId.set(noteId, found);
        }

        return found ?? null;
    };

    const openModalForRow = (row) => {
        if (!(row instanceof HTMLElement)) {
            return;
        }

        const dataset = row.dataset ?? {};
        const bottleId = dataset.bottleId ?? '';
        const noteId = dataset.noteId ?? '';
        const label = (dataset.noteLabel ?? '').trim();
        const notTasted = (dataset.noteNotTasted ?? '').toLowerCase() === 'true';
        const noteValue = notTasted ? '' : (dataset.noteText ?? '');
        const scoreRaw = (dataset.noteScore ?? '').trim();
        const scoreValue = !notTasted && scoreRaw ? Number.parseFloat(scoreRaw) : '';
        const dateValue = dataset.noteDate ?? '';

        const detail = {
            context: 'notes',
            bottleId,
            noteId,
            label: label || 'Bottle',
            note: noteValue,
            score: scoreValue,
            date: dateValue,
            mode: noteId ? 'edit' : 'create',
            requireDate: false,
            noteOnly: true,
            notTasted,
            successMessage: 'Tasting note updated.',
            initialFocus: notTasted ? 'score' : 'note'
        };

        window.dispatchEvent(new CustomEvent('drinkmodal:open', { detail }));
    };

    document.addEventListener('DOMContentLoaded', () => {
        const rows = Array.from(document.querySelectorAll(NOTE_ROW_SELECTOR));
        if (rows.length === 0) {
            return;
        }

        rows.forEach((row) => {
            if (!(row instanceof HTMLElement)) {
                return;
            }

            const noteId = (row.dataset.noteId ?? '').trim();
            if (noteId) {
                rowsByNoteId.set(noteId, row);
            }

            row.addEventListener('click', (event) => {
                if (event.target instanceof Element) {
                    const interactiveAncestor = event.target.closest(INTERACTIVE_SELECTOR);
                    if (interactiveAncestor && interactiveAncestor !== row) {
                        return; // Allow inner links/buttons to handle the click, but not the row itself
                    }
                }

                event.preventDefault();
                openModalForRow(row);
            });

            row.addEventListener('keydown', (event) => {
                if (event.key === 'Enter' || event.key === ' ') {
                    event.preventDefault();
                    openModalForRow(row);
                }
            });
        });
    });

    window.addEventListener('drinkmodal:submit', (event) => {
        const detail = event?.detail ?? {};
        if ((detail.context ?? 'external') !== 'notes') {
            return;
        }

        event.preventDefault();

        const noteId = typeof detail.noteId === 'string' ? detail.noteId.trim() : '';
        if (!noteId) {
            if (typeof detail.showError === 'function') {
                detail.showError('We could not determine which tasting note to update.');
            }
            return;
        }

        const row = getRowByNoteId(noteId);
        if (!row) {
            if (typeof detail.showError === 'function') {
                detail.showError('We could not find that tasting note in the table.');
            }
            return;
        }

        const notTasted = Boolean(detail.notTasted);
        const normalizedNote = notTasted
            ? ''
            : (typeof detail.note === 'string' ? detail.note.trim() : '');
        const normalizedScore = !notTasted && Number.isFinite(detail.score)
            ? Number(detail.score)
            : null;

        const payload = {
            note: normalizedNote,
            score: normalizedScore,
            notTasted
        };

        const submitPromise = (async () => {
            const response = await fetch(`/wine-manager/notes/${noteId}`, {
                method: 'PUT',
                headers: {
                    'Content-Type': 'application/json',
                    'Accept': 'application/json'
                },
                body: JSON.stringify(payload)
            });

            const raw = await response.text();
            const contentType = response.headers.get('Content-Type') ?? '';
            const isJson = raw && contentType.toLowerCase().includes('application/json');
            let data = null;

            if (isJson) {
                try {
                    data = JSON.parse(raw);
                } catch (error) {
                    console.error('Unable to parse tasting note update response', error);
                }
            }

            if (!response.ok) {
                const fallback = raw?.trim()
                    ? raw.trim()
                    : 'We could not update that tasting note. Please try again.';
                const message = extractProblemMessage(data, fallback);
                throw new Error(message);
            }

            updateRowDisplay(row, {
                note: normalizedNote,
                score: normalizedScore,
                notTasted
            });

            return { message: detail.successMessage ?? 'Tasting note updated.' };
        })();

        if (typeof detail.setSuccessMessage === 'function') {
            detail.setSuccessMessage('Tasting note updated.');
        }

        if (typeof detail.setSubmitPromise === 'function') {
            detail.setSubmitPromise(submitPromise);
        }
    });
    // --- Sorting ---
    const SORTABLE_TABLE_SELECTOR = '[data-crud-table="notes-table"]';

    const getCellText = (row, selector) => {
        const cell = row.querySelector(selector);
        if (!cell) return '';
        return (cell.textContent || '').trim();
    };

    const parseNumber = (value) => {
        if (value == null || value === '') return NaN;
        const n = Number(String(value).replace(/[^\d.\-]/g, ''));
        return Number.isFinite(n) ? n : NaN;
    };

    const parseDate = (row) => {
        // Prefer machine-friendly dataset value when available
        const iso = row.dataset.noteDate || '';
        if (iso) {
            const t = Date.parse(iso);
            if (!Number.isNaN(t)) return t;
        }
        // Fallback to visible cell content
        const text = getCellText(row, '.summary-cell--date');
        const t2 = Date.parse(text);
        return Number.isNaN(t2) ? NaN : t2;
    };

    const getSortValue = (row, key) => {
        switch (key) {
            case 'wine':
                return getCellText(row, '.summary-cell--wine').toLowerCase();
            case 'origin':
                return getCellText(row, '.summary-cell--appellation').toLowerCase();
            case 'vintage': {
                const text = getCellText(row, '.summary-cell--vintage');
                const n = parseNumber(text);
                return Number.isNaN(n) ? -Infinity : n;
            }
            case 'date': {
                const t = parseDate(row);
                return Number.isNaN(t) ? -Infinity : t;
            }
            case 'score': {
                const scoreAttr = row.dataset.noteScore || '';
                const n = parseNumber(scoreAttr);
                return Number.isNaN(n) ? -Infinity : n;
            }
            case 'note':
                return getCellText(row, '.summary-cell--note').toLowerCase();
            default:
                return '';
        }
    };

    const compareValues = (a, b) => {
        if (a === b) return 0;
        if (a === '' || a === -Infinity) return 1; // empty goes last
        if (b === '' || b === -Infinity) return -1;
        if (typeof a === 'number' && typeof b === 'number') return a < b ? -1 : 1;
        return a < b ? -1 : 1;
    };

    const applyAriaSort = (headers, active, dir) => {
        headers.forEach(h => {
            const sortHeader = h.querySelector('.sort-header');
            if (h === active) {
                h.setAttribute('aria-sort', dir === 'asc' ? 'ascending' : 'descending');
                if (sortHeader) {
                    sortHeader.classList.remove('sorted-asc', 'sorted-desc');
                    sortHeader.classList.add(dir === 'asc' ? 'sorted-asc' : 'sorted-desc');
                }
            } else {
                h.setAttribute('aria-sort', 'none');
                if (sortHeader) {
                    sortHeader.classList.remove('sorted-asc', 'sorted-desc');
                }
            }
        });
    };

    const sortTable = (table, key, direction) => {
        const tbody = table.querySelector('tbody');
        if (!tbody) return;
        const rows = Array.from(tbody.querySelectorAll(NOTE_ROW_SELECTOR));
        const dir = direction === 'desc' ? -1 : 1;
        rows.sort((r1, r2) => compareValues(getSortValue(r1, key), getSortValue(r2, key)) * dir);
        rows.forEach(r => tbody.appendChild(r));
    };

    document.addEventListener('DOMContentLoaded', () => {
        const table = document.querySelector(SORTABLE_TABLE_SELECTOR);
        if (!table) return;
        const headers = Array.from(table.querySelectorAll('thead th[data-sort-key]'));
        let currentKey = null;
        let currentDir = 'asc';

        const handleActivate = (th) => {
            const key = th.getAttribute('data-sort-key');
            if (!key) return;
            if (currentKey === key) {
                currentDir = currentDir === 'asc' ? 'desc' : 'asc';
            } else {
                currentKey = key;
                currentDir = key === 'date' ? 'desc' : 'asc'; // default newest first for date
            }
            applyAriaSort(headers, th, currentDir);
            sortTable(table, currentKey, currentDir);
        };

        headers.forEach(th => {
            th.addEventListener('click', (e) => {
                e.preventDefault();
                handleActivate(th);
            });
            th.addEventListener('keydown', (e) => {
                if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    handleActivate(th);
                }
            });
        });
    });
})();
