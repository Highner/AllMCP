(() => {
    const formatScore = (value) => {
        if (value == null || value === '') {
            return '—';
        }

        const number = typeof value === 'number' ? value : Number(value);
        if (!Number.isFinite(number)) {
            return '—';
        }

        return number.toFixed(1);
    };

    const parseScoreInput = (raw) => {
        const trimmed = (raw ?? '').trim();
        if (!trimmed) {
            return null;
        }

        const number = Number(trimmed);
        if (!Number.isFinite(number) || number < 0 || number > 10) {
            return undefined;
        }

        return Math.round(number * 10) / 10;
    };

    const setElementDisabled = (element, disabled) => {
        if (!element) {
            return;
        }

        if (disabled) {
            element.setAttribute('disabled', 'disabled');
        } else {
            element.removeAttribute('disabled');
        }
    };

    document.addEventListener('DOMContentLoaded', () => {
        const overlay = document.getElementById('drink-bottle-overlay');
        const popover = document.getElementById('drink-bottle-popover');
        const form = popover?.querySelector('.drink-bottle-form');
        const cards = Array.from(document.querySelectorAll('[data-sip-session-bottle-card]'));

        if (!overlay || !popover || !form || cards.length === 0) {
            return;
        }

        const noteInput = popover.querySelector('.drink-bottle-note');
        const scoreInput = popover.querySelector('.drink-bottle-score');
        const dateInput = popover.querySelector('.drink-bottle-date');
        const errorElement = popover.querySelector('.drink-bottle-error');
        const cancelButton = popover.querySelector('.drink-bottle-cancel');
        const submitButton = popover.querySelector('.drink-bottle-submit');
        const titleElement = popover.querySelector('.drink-bottle-title');
        const hiddenSisterhood = form.querySelector('input[name="SisterhoodId"]');
        const hiddenSession = form.querySelector('input[name="SipSessionId"]');
        const hiddenBottle = form.querySelector('input[name="BottleId"]');
        const hiddenNoteId = form.querySelector('input[name="NoteId"]');
        const tokenInput = form.querySelector('input[name="__RequestVerificationToken"]');
        let activeCard = null;
        let loading = false;
        let closeTimerId = null;

        const showFeedback = (message, type = 'error') => {
            if (!errorElement) {
                return;
            }

            const normalized = message ?? '';
            errorElement.textContent = normalized;
            errorElement.setAttribute('aria-hidden', normalized ? 'false' : 'true');
            if (type === 'success') {
                errorElement.classList.add('is-success');
            } else {
                errorElement.classList.remove('is-success');
            }
        };

        const resetCloseTimer = () => {
            if (closeTimerId) {
                window.clearTimeout(closeTimerId);
                closeTimerId = null;
            }
        };

        const closeModal = (restoreFocus = true) => {
            if (!overlay) {
                return;
            }

            resetCloseTimer();

            const cardToFocus = activeCard;
            activeCard = null;
            overlay.hidden = true;
            overlay.classList.remove('is-open');
            document.body.style.overflow = '';
            showFeedback('');

            if (form) {
                form.reset();
            }

            if (hiddenBottle) {
                hiddenBottle.value = '';
            }

            if (hiddenNoteId) {
                hiddenNoteId.value = '';
            }

            if (restoreFocus && cardToFocus instanceof HTMLElement) {
                window.requestAnimationFrame(() => {
                    cardToFocus.focus();
                });
            }
        };

        const openModal = (card) => {
            if (!card) {
                return;
            }

            resetCloseTimer();

            activeCard = card;
            const note = card.getAttribute('data-bottle-note') ?? '';
            const score = card.getAttribute('data-bottle-score') ?? '';
            const noteId = card.getAttribute('data-bottle-note-id') ?? '';
            const label = card.getAttribute('data-bottle-label') ?? '';
            const bottleId = card.getAttribute('data-bottle-id') ?? '';

            if (hiddenBottle) {
                hiddenBottle.value = bottleId ?? '';
            }

            if (hiddenNoteId) {
                hiddenNoteId.value = noteId ?? '';
            }

            if (noteInput) {
                noteInput.value = note ?? '';
            }

            if (scoreInput) {
                scoreInput.value = score ?? '';
            }

            if (dateInput) {
                dateInput.value = '';
            }

            if (titleElement) {
                titleElement.textContent = label
                    ? `Drink ${label}`
                    : 'Drink Bottle';
            }

            showFeedback('');

            overlay.hidden = false;
            overlay.classList.add('is-open');
            document.body.style.overflow = 'hidden';

            window.requestAnimationFrame(() => {
                (noteInput ?? scoreInput)?.focus();
            });
        };

        const setLoading = (state) => {
            loading = state;
            setElementDisabled(submitButton, state);
            setElementDisabled(cancelButton, state);
            setElementDisabled(noteInput, state);
            setElementDisabled(scoreInput, state);
            setElementDisabled(dateInput, state);
        };

        const updatePrompt = (card, hasNote) => {
            const prompt = card.querySelector('[data-bottle-prompt]');
            if (!prompt) {
                return;
            }

            prompt.textContent = hasNote
                ? 'Click to update your tasting note.'
                : 'Click to add your tasting note.';
        };

        const updateCardFromResponse = (card, payload) => {
            if (!(card instanceof HTMLElement)) {
                return;
            }

            const noteDisplay = card.querySelector('[data-bottle-note-display]');
            const scoreDisplay = card.querySelector('[data-bottle-score-display]');
            const averageDisplay = card.querySelector('[data-bottle-average-display]');
            const noteText = (payload.note ?? '').toString();
            const trimmedNote = noteText.trim();
            const scoreValue = payload.score ?? null;
            const averageValue = payload.averageScore ?? null;
            const noteId = payload.noteId ?? '';

            if (noteDisplay) {
                noteDisplay.textContent = trimmedNote || 'No tasting note yet.';
                if (trimmedNote) {
                    noteDisplay.classList.remove('bottle-note--empty');
                } else {
                    noteDisplay.classList.add('bottle-note--empty');
                }
            }

            updatePrompt(card, Boolean(trimmedNote));

            if (scoreDisplay) {
                scoreDisplay.textContent = formatScore(scoreValue);
            }

            if (averageDisplay && averageValue != null) {
                averageDisplay.textContent = formatScore(averageValue);
            }

            card.setAttribute('data-bottle-note', trimmedNote);
            card.setAttribute('data-bottle-score', scoreValue != null ? String(scoreValue) : '');
            card.setAttribute('data-bottle-note-id', noteId ? String(noteId) : '');

            if (hiddenNoteId) {
                hiddenNoteId.value = noteId ? String(noteId) : '';
            }
        };

        const handleCardActivate = (card) => {
            if (!card || loading) {
                return;
            }

            const sisterhoodId = hiddenSisterhood?.value ?? '';
            const sessionId = hiddenSession?.value ?? '';

            if (!sisterhoodId || !sessionId) {
                showFeedback('Unable to determine the sip session.');
                return;
            }

            openModal(card);
        };

        cards.forEach(card => {
            card.addEventListener('click', () => handleCardActivate(card));
            card.addEventListener('keydown', (event) => {
                if (event.key === 'Enter' || event.key === ' ') {
                    event.preventDefault();
                    handleCardActivate(card);
                }
            });
        });

        cancelButton?.addEventListener('click', () => {
            if (!loading) {
                closeModal();
            }
        });

        overlay.addEventListener('click', (event) => {
            if (event.target === overlay && !loading) {
                closeModal();
            }
        });

        document.addEventListener('keydown', (event) => {
            if (event.key === 'Escape' && !overlay.hidden && !loading) {
                closeModal();
            }
        });

        form.addEventListener('submit', async (event) => {
            event.preventDefault();

            if (loading) {
                return;
            }

            if (!activeCard) {
                showFeedback('Select a bottle to add your tasting note.');
                return;
            }

            const sisterhoodId = hiddenSisterhood?.value ?? '';
            const sessionId = hiddenSession?.value ?? '';
            const bottleId = hiddenBottle?.value ?? '';
            const token = tokenInput?.value ?? '';

            if (!sisterhoodId || !sessionId || !bottleId) {
                showFeedback('Missing sip session context. Please refresh and try again.');
                return;
            }

            const noteValue = noteInput?.value?.trim() ?? '';
            if (!noteValue) {
                showFeedback('Enter a tasting note.');
                noteInput?.focus();
                return;
            }

            const scoreValue = parseScoreInput(scoreInput?.value ?? '');
            if (scoreValue === undefined) {
                showFeedback('Score must be between 0 and 10.');
                scoreInput?.focus();
                return;
            }

            const payload = new URLSearchParams();
            payload.set('SisterhoodId', sisterhoodId);
            payload.set('SipSessionId', sessionId);
            payload.set('BottleId', bottleId);
            payload.set('Note', noteValue);
            if (scoreValue != null) {
                payload.set('Score', String(scoreValue));
            }

            const noteIdentifier = hiddenNoteId?.value?.trim();
            if (noteIdentifier) {
                payload.set('NoteId', noteIdentifier);
            }

            const requestOptions = {
                method: (form.getAttribute('method') || 'post').toUpperCase(),
                headers: {
                    'Accept': 'application/json',
                    'X-Requested-With': 'XMLHttpRequest'
                },
                body: payload
            };

            if (token) {
                requestOptions.headers['RequestVerificationToken'] = token;
            }

            setLoading(true);
            showFeedback('');

            try {
                const response = await fetch(form.getAttribute('action') ?? window.location.href, requestOptions);
                if (!response.ok) {
                    const text = await response.text();
                    throw new Error(text || 'Unable to save tasting note.');
                }

                const result = await response.json();
                if (!result || result.success !== true) {
                    throw new Error(result?.message ?? 'Unable to save tasting note.');
                }

                updateCardFromResponse(activeCard, result);
                showFeedback(result.message ?? 'Tasting note saved.', 'success');

                closeTimerId = window.setTimeout(() => {
                    closeModal();
                }, 800);
            } catch (error) {
                const message = error instanceof Error ? error.message : String(error);
                showFeedback(message);
            } finally {
                setLoading(false);
            }
        });
    });
})();
