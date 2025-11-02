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
                    if (event.target.closest(INTERACTIVE_SELECTOR)) {
                        return;
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
})();
