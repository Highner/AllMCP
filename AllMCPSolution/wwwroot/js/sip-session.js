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
        const allCards = Array.from(document.querySelectorAll('[data-sip-session-bottle-card]'));
        const revealButtons = Array.from(document.querySelectorAll('[data-sip-session-reveal-button]'));
        const revealForms = Array.from(document.querySelectorAll('[data-sip-session-reveal-form]'));
        const interactiveCards = allCards.filter(card => card.dataset.bottleInteractive !== 'false');

        revealButtons.forEach(button => {
            button.addEventListener('click', (event) => {
                event.stopPropagation();
            });

            button.addEventListener('keydown', (event) => {
                event.stopPropagation();
            });
        });

        revealForms.forEach(revealForm => {
            revealForm.addEventListener('click', (event) => {
                event.stopPropagation();
            });
        });

        if (!overlay || !popover || !form || interactiveCards.length === 0) {
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
        const baseCreateTitle = titleElement?.textContent?.trim() || 'Drink Bottle';
        const createTitleUsesDrink = /\bdrink\b/i.test(baseCreateTitle);
        const baseSubmitLabel = submitButton?.textContent?.trim() || 'Drink Bottle';
        const editTitleBase = 'Update tasting note';
        const editSubmitLabel = 'Update note';
        const modalModeAttribute = 'data-form-mode';
        let activeCard = null;
        let loading = false;
        let closeTimerId = null;

        const applyModalMode = (mode, label) => {
            const normalized = mode === 'edit' ? 'edit' : 'create';
            const trimmedLabel = (label ?? '').trim();

            if (form) {
                form.setAttribute(modalModeAttribute, normalized);
            }

            if (popover) {
                popover.setAttribute(modalModeAttribute, normalized);
            }

            if (submitButton) {
                submitButton.textContent = normalized === 'edit'
                    ? editSubmitLabel
                    : baseSubmitLabel;
            }

            if (titleElement) {
                if (normalized === 'edit') {
                    const base = editTitleBase;
                    titleElement.textContent = trimmedLabel
                        ? `${base} · ${trimmedLabel}`
                        : base;
                } else if (trimmedLabel) {
                    titleElement.textContent = createTitleUsesDrink
                        ? `Drink ${trimmedLabel}`
                        : `${baseCreateTitle} · ${trimmedLabel}`;
                } else {
                    titleElement.textContent = baseCreateTitle;
                }
            }
        };

        applyModalMode('create');

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

            applyModalMode('create');

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
            const bottleId = card.dataset.bottleId ?? card.getAttribute('data-bottle-id') ?? '';
            const label = card.dataset.bottleLabel ?? card.getAttribute('data-bottle-label') ?? '';
            const note = card.dataset.bottleNote ?? card.getAttribute('data-bottle-note') ?? '';
            const score = card.dataset.bottleScore ?? card.getAttribute('data-bottle-score') ?? '';
            const noteId = card.dataset.bottleNoteId ?? card.getAttribute('data-bottle-note-id') ?? '';
            const normalizedNoteId = (noteId ?? '').trim();
            const noteText = typeof note === 'string' ? note : '';
            const normalizedScore = (score ?? '').trim();
            const trimmedLabel = (label ?? '').trim();
            const hasExisting = normalizedNoteId.length > 0
                || noteText.trim().length > 0
                || normalizedScore.length > 0;

            applyModalMode(hasExisting ? 'edit' : 'create', trimmedLabel);

            if (hiddenBottle) {
                hiddenBottle.value = bottleId ?? '';
            }

            if (hiddenNoteId) {
                hiddenNoteId.value = normalizedNoteId;
            }

            if (noteInput) {
                noteInput.value = noteText;
            }

            if (scoreInput) {
                scoreInput.value = normalizedScore;
            }

            if (dateInput) {
                dateInput.value = '';
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
            const sisterhoodAverageDisplay = card.querySelector('[data-bottle-sisterhood-average-display]');
            const noteText = (payload.note ?? '').toString();
            const trimmedNote = noteText.trim();
            const scoreValue = payload.score ?? null;
            const averageValue = payload.averageScore ?? null;
            const sisterhoodAverageValue = payload.sisterhoodAverageScore ?? null;
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

            if (sisterhoodAverageDisplay) {
                sisterhoodAverageDisplay.textContent = formatScore(sisterhoodAverageValue);
            }

            card.setAttribute('data-bottle-note', trimmedNote);
            card.setAttribute('data-bottle-score', scoreValue != null ? String(scoreValue) : '');
            card.setAttribute('data-bottle-note-id', noteId ? String(noteId) : '');
            card.setAttribute('data-bottle-sisterhood-average', sisterhoodAverageValue != null ? String(sisterhoodAverageValue) : '');

            if (hiddenNoteId) {
                hiddenNoteId.value = noteId ? String(noteId) : '';
            }
        };

        const handleCardActivate = (card) => {
            if (!card || loading) {
                return;
            }

            if (card.dataset.bottleInteractive === 'false') {
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

        interactiveCards.forEach(card => {
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
                const action = form.getAttribute('action');
                let endpoint = window.location.href;

                if (action) {
                    try {
                        endpoint = new URL(action, window.location.origin).toString();
                    } catch (error) {
                        endpoint = action;
                    }
                }

                const response = await fetch(endpoint, requestOptions);
                const contentType = response.headers.get('content-type') ?? '';
                const rawBody = await response.text();
                let payload = null;

                if (rawBody && contentType.includes('application/json')) {
                    try {
                        payload = JSON.parse(rawBody);
                    } catch (error) {
                        payload = null;
                    }
                }

                if (!response.ok) {
                    const message = (payload && typeof payload.message === 'string')
                        ? payload.message
                        : (rawBody || 'Unable to save tasting note.');
                    throw new Error(message);
                }

                if (!payload || payload.success !== true) {
                    throw new Error(payload?.message ?? 'Unable to save tasting note.');
                }

                updateCardFromResponse(activeCard, payload);
                showFeedback(payload.message ?? 'Tasting note saved.', 'success');

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
