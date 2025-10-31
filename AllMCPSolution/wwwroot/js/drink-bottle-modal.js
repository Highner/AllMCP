(() => {
    const NOT_RATED_LABEL = 'Not rated';

    const getTodayDateString = () => {
        const now = new Date();
        const utcDate = new Date(Date.UTC(
            now.getFullYear(),
            now.getMonth(),
            now.getDate()
        ));

        return utcDate.toISOString().slice(0, 10);
    };

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

        const number = Number.parseFloat(trimmed);
        if (!Number.isFinite(number) || number < 0 || number > 10) {
            return undefined;
        }

        return number;
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

    const createScoreController = ({
        input,
        display,
        clearButton,
        defaultValue,
        notRatedLabel = NOT_RATED_LABEL
    } = {}) => {
        if (!input) {
            if (clearButton) {
                clearButton.disabled = true;
            }

            return {
                input: null,
                isRange: false,
                setValue: () => { },
                getRawValue: () => '',
                reset: () => { },
                updateDisplay: () => { }
            };
        }

        const isRange = (input.type ?? '').toLowerCase() === 'range';
        const resolvedDefault = defaultValue
            ?? input.dataset?.defaultValue
            ?? input.getAttribute?.('data-default-value')
            ?? input.getAttribute?.('min')
            ?? '0';

        const controller = {
            input,
            isRange,
            setValue(value) {
                if (!isRange) {
                    input.value = value != null && value !== '' ? String(value) : '';
                    controller.updateDisplay();
                    return;
                }

                const hasValue = value != null && value !== '';
                input.dataset.hasValue = hasValue ? 'true' : 'false';
                input.value = hasValue ? String(value) : resolvedDefault;
                controller.updateDisplay();
            },
            getRawValue() {
                if (!isRange) {
                    return input.value ?? '';
                }

                return input.dataset.hasValue === 'true'
                    ? (input.value ?? '')
                    : '';
            },
            reset() {
                if (!isRange) {
                    input.value = '';
                } else {
                    input.dataset.hasValue = 'false';
                    input.value = resolvedDefault;
                }

                controller.updateDisplay();
            },
            updateDisplay() {
                if (!display) {
                    return;
                }

                if (isRange && input.dataset.hasValue !== 'true') {
                    display.textContent = notRatedLabel;
                    input.setAttribute('aria-valuetext', notRatedLabel);
                    return;
                }

                const numeric = Number.parseFloat(input.value ?? '');
                if (Number.isFinite(numeric)) {
                    const formatted = numeric.toFixed(1);
                    display.textContent = `${formatted} / 10`;
                    input.setAttribute('aria-valuetext', `${formatted} out of 10`);
                } else {
                    display.textContent = notRatedLabel;
                    input.setAttribute('aria-valuetext', notRatedLabel);
                }
            }
        };

        if (!isRange) {
            if (clearButton) {
                clearButton.disabled = true;
            }

            controller.updateDisplay();
            return controller;
        }

        controller.reset();

        input.addEventListener('input', () => {
            input.dataset.hasValue = 'true';
            controller.updateDisplay();
        });

        if (clearButton) {
            clearButton.addEventListener('click', (event) => {
                event.preventDefault();
                controller.setValue('');
                if (typeof input.focus === 'function') {
                    input.focus();
                }
            });
        }

        return controller;
    };

    const parseDateOnly = (value) => {
        if (!value) {
            return null;
        }

        const trimmed = value.trim();
        if (!trimmed) {
            return null;
        }

        const date = new Date(trimmed);
        if (Number.isNaN(date.getTime())) {
            return null;
        }

        const year = date.getUTCFullYear();
        const month = date.getUTCMonth();
        const day = date.getUTCDate();

        if (!Number.isFinite(year) || !Number.isFinite(month) || !Number.isFinite(day)) {
            return null;
        }

        const normalized = new Date(Date.UTC(year, month, day));
        if (Number.isNaN(normalized.getTime())) {
            return null;
        }

        return normalized.toISOString().slice(0, 10);
    };

    document.addEventListener('DOMContentLoaded', () => {
        const overlay = document.getElementById('drink-bottle-overlay');
        const popover = document.getElementById('drink-bottle-popover');
        const form = popover?.querySelector('.drink-bottle-form');

        if (!overlay || !popover || !form) {
            return;
        }

        const noteInput = popover.querySelector('.drink-bottle-note');
        const scoreInput = popover.querySelector('.drink-bottle-score');
        const scoreDisplay = popover.querySelector('.drink-bottle-score-display');
        const scoreClearButton = popover.querySelector('.drink-bottle-score-clear');
        const scoreDefaultValue = scoreInput?.dataset?.defaultValue
            ?? scoreInput?.getAttribute('data-default-value')
            ?? scoreInput?.getAttribute('min')
            ?? '5';
        const dateInput = popover.querySelector('.drink-bottle-date');
        const dateField = form.querySelector('.drink-bottle-date-field');
        const noteOnlyMessage = form.querySelector('.drink-bottle-note-only-message');
        const notTastedCheckbox = form.querySelector('.drink-bottle-not-tasted');
        const errorElement = popover.querySelector('.drink-bottle-error');
        const cancelButton = popover.querySelector('.drink-bottle-cancel');
        const headerCloseButton = popover.querySelector('[data-drink-bottle-close]');
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
        const dateInputHadRequired = dateInput?.hasAttribute('required');

        const scoreControl = createScoreController({
            input: scoreInput,
            display: scoreDisplay,
            clearButton: scoreClearButton,
            defaultValue: scoreDefaultValue
        });

        const resolveDefaultDateValue = () => getTodayDateString();

        const setNoteOnlyState = (state) => {
            const isNoteOnly = Boolean(state);

            if (form) {
                if (isNoteOnly) {
                    form.dataset.noteOnly = 'true';
                } else {
                    delete form.dataset.noteOnly;
                    form.removeAttribute('data-note-only');
                }
            }

            if (noteOnlyMessage) {
                noteOnlyMessage.hidden = !isNoteOnly;
                noteOnlyMessage.classList.toggle('is-active', isNoteOnly);
            }

            if (dateInput) {
                if (isNoteOnly) {
                    if (dateInputHadRequired) {
                        dateInput.removeAttribute('required');
                    }
                    dateInput.value = '';
                    dateInput.setAttribute('aria-disabled', 'true');
                    dateInput.setAttribute('disabled', 'disabled');
                } else {
                    dateInput.removeAttribute('disabled');
                    dateInput.removeAttribute('aria-disabled');
                    if (dateInputHadRequired) {
                        dateInput.setAttribute('required', 'required');
                    }
                    if (!dateInput.value) {
                        dateInput.value = resolveDefaultDateValue();
                    }
                }
            }

            if (dateField) {
                if (isNoteOnly) {
                    dateField.hidden = true;
                    dateField.setAttribute('aria-hidden', 'true');
                } else {
                    dateField.hidden = false;
                    dateField.removeAttribute('aria-hidden');
                }

                dateField.classList.toggle('is-disabled', isNoteOnly);
            }
        };

        setNoteOnlyState(false);

        if (dateInput && !dateInput.value) {
            dateInput.value = resolveDefaultDateValue();
        }

        scoreControl.setValue(scoreDefaultValue);

        const setNotTastedState = (checked) => {
            const isChecked = Boolean(checked);
            contextState.notTasted = isChecked;
            if (noteInput) {
                noteInput.value = isChecked ? '' : noteInput.value;
                setElementDisabled(noteInput, isChecked);
            }
            if (scoreControl && scoreControl.input) {
                scoreControl.setValue('');
                setElementDisabled(scoreControl.input, isChecked);
                if (scoreClearButton) {
                    setElementDisabled(scoreClearButton, isChecked);
                }
            }
        };

        if (notTastedCheckbox) {
            notTastedCheckbox.addEventListener('change', () => {
                setNotTastedState(notTastedCheckbox.checked);
            });
        }

        const contextState = {
            context: null,
            card: null,
            data: null,
            external: null,
            requireDate: false,
            notTasted: false,
            noteOnly: false,
            successMessage: '',
            initialFocus: null
        };

        let loading = false;
        let closeTimerId = null;

        const suggestFoodForm = document.querySelector('[data-suggest-food-form]');
        if (suggestFoodForm) {
            const suggestFoodButton = suggestFoodForm.querySelector('[data-suggest-food-button]');
            if (suggestFoodButton) {
                suggestFoodForm.addEventListener('submit', () => {
                    if (suggestFoodButton.disabled || suggestFoodButton.dataset.state === 'loading') {
                        return;
                    }

                    suggestFoodButton.disabled = true;
                    suggestFoodButton.dataset.state = 'loading';
                    suggestFoodButton.setAttribute('aria-busy', 'true');
                });
            }
        }

        const formatModalTitle = (mode, label) => {
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

            if (!titleElement) {
                return;
            }

            if (normalized === 'edit') {
                const base = editTitleBase;
                titleElement.textContent = trimmedLabel
                    ? `${base} · ${trimmedLabel}`
                    : base;
                return;
            }

            if (trimmedLabel) {
                titleElement.textContent = createTitleUsesDrink
                    ? `Drink ${trimmedLabel}`
                    : `${baseCreateTitle} · ${trimmedLabel}`;
                return;
            }

            titleElement.textContent = baseCreateTitle;
        };

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

        const focusField = (field) => {
            if (field === 'score') {
                if (scoreInput) {
                    scoreInput.focus();
                }
                return;
            }

            if (field === 'date') {
                if (contextState.noteOnly && noteInput) {
                    noteInput.focus();
                    return;
                }

                if (dateInput) {
                    dateInput.focus();
                }
                return;
            }

            if (noteInput) {
                noteInput.focus();
            }
        };

        const setLoading = (state) => {
            loading = state;
            setElementDisabled(submitButton, state);
            setElementDisabled(cancelButton, state);
            setElementDisabled(headerCloseButton, state);
            setElementDisabled(noteInput, state);
            setElementDisabled(scoreInput, state);
            setElementDisabled(scoreClearButton, state);
            if (dateInput) {
                if (state) {
                    setElementDisabled(dateInput, true);
                } else if (!contextState.noteOnly) {
                    setElementDisabled(dateInput, false);
                }
            }

            if (state) {
                form.setAttribute('aria-busy', 'true');
            } else {
                form.removeAttribute('aria-busy');
            }
        };

        const clearContext = () => {
            contextState.context = null;
            contextState.card = null;
            contextState.data = null;
            contextState.external = null;
            contextState.requireDate = false;
            contextState.noteOnly = false;
            contextState.successMessage = '';
            contextState.initialFocus = null;
        };

        const closeModal = (options = {}) => {
            if (overlay.hidden) {
                return;
            }

            resetCloseTimer();

            const restoreFocus = options.restoreFocus !== false;
            const sourceContext = contextState.context;
            const previousCard = contextState.card;
            const externalDetail = contextState.external;

            overlay.hidden = true;
            overlay.classList.remove('is-open');
            overlay.setAttribute('aria-hidden', 'true');
            document.body.style.overflow = '';

            form.reset();
            scoreControl.reset();
            setNoteOnlyState(false);

            if (dateInput) {
                dateInput.value = resolveDefaultDateValue();
            }
            showFeedback('');

            if (hiddenBottle) {
                hiddenBottle.value = '';
            }

            if (hiddenNoteId) {
                hiddenNoteId.value = '';
            }

            clearContext();

            if (restoreFocus) {
                const target = options.focusElement
                    || (previousCard instanceof HTMLElement ? previousCard : null)
                    || externalDetail?.restoreFocusElement;

                if (target instanceof HTMLElement) {
                    window.requestAnimationFrame(() => {
                        target.focus();
                    });
                }
            }

            window.dispatchEvent(new CustomEvent('drinkmodal:closed', {
                detail: {
                    context: sourceContext,
                    data: externalDetail
                }
            }));
        };

        const scheduleClose = () => {
            resetCloseTimer();
            closeTimerId = window.setTimeout(() => {
                closeModal();
            }, 800);
        };

        const openModal = (openOptions = {}) => {
            const {
                context = 'external',
                label = '',
                bottleId = '',
                noteId = '',
                note = '',
                score = '',
                date = '',
                mode = 'create',
                requireDate = false,
                successMessage = '',
                external = null,
                initialFocus = null,
                noteOnly = false
            } = openOptions;

            resetCloseTimer();
            contextState.context = context;
            contextState.card = openOptions.card ?? null;
            contextState.data = { bottleId, noteId };
            contextState.external = external;
            contextState.requireDate = Boolean(requireDate);
            contextState.noteOnly = Boolean(noteOnly);
            contextState.successMessage = successMessage || '';
            contextState.initialFocus = initialFocus;

            formatModalTitle(mode, label);

            if (hiddenBottle) {
                hiddenBottle.value = bottleId ?? '';
            }

            if (hiddenNoteId) {
                hiddenNoteId.value = noteId ?? '';
            }

            const normalizedMode = (mode ?? '').toLowerCase() === 'edit' ? 'edit' : 'create';

            if (noteInput) {
                noteInput.value = typeof note === 'string' ? note : note != null ? String(note) : '';
            }

            if (dateInput) {
                const normalizedDate = parseDateOnly(date);
                const fallbackDateValue = resolveDefaultDateValue();
                dateInput.value = normalizedDate ?? fallbackDateValue;
            }

            setNoteOnlyState(contextState.noteOnly);

            if (score != null && score !== '') {
                scoreControl.setValue(score);
            } else if (normalizedMode === 'edit') {
                scoreControl.setValue('');
            } else {
                scoreControl.setValue(scoreDefaultValue);
            }

            showFeedback('');
            setLoading(false);

            overlay.hidden = false;
            overlay.setAttribute('aria-hidden', 'false');
            overlay.classList.add('is-open');
            document.body.style.overflow = 'hidden';

            window.dispatchEvent(new CustomEvent('drinkmodal:opened', {
                detail: {
                    context,
                    data: external
                }
            }));

            const focusTarget = initialFocus || (noteInput ? 'note' : 'score');
            window.requestAnimationFrame(() => {
                focusField(focusTarget);
            });
        };

        const handleExternalOpenEvent = (event) => {
            const detail = event?.detail ?? {};
            const normalizedScore = detail.score ?? (detail.score === 0 ? 0 : '');

            openModal({
                context: detail.context ?? 'external',
                label: detail.label ?? detail.title ?? '',
                bottleId: detail.bottleId ?? '',
                noteId: detail.noteId ?? '',
                note: detail.note ?? '',
                score: normalizedScore,
                date: detail.date ?? '',
                mode: detail.mode ?? (detail.noteId ? 'edit' : 'create'),
                requireDate: Boolean(detail.requireDate),
                successMessage: detail.successMessage ?? '',
                external: detail,
                initialFocus: detail.initialFocus ?? null,
                noteOnly: Boolean(detail.noteOnly)
            });
        };

        const handleOpenFromCard = (card) => {
            if (!card) {
                return;
            }

            const bottleId = card.dataset.bottleId ?? card.getAttribute('data-bottle-id') ?? '';
            const label = card.dataset.bottleLabel ?? card.getAttribute('data-bottle-label') ?? '';
            const note = card.dataset.bottleNote ?? card.getAttribute('data-bottle-note') ?? '';
            const score = card.dataset.bottleScore ?? card.getAttribute('data-bottle-score') ?? '';
            const noteId = card.dataset.bottleNoteId ?? card.getAttribute('data-bottle-note-id') ?? '';
            const sisterhoodContext = card.dataset.sisterhoodId ?? card.getAttribute('data-sisterhood-id') ?? '';
            const sessionContext = card.dataset.sipSessionId ?? card.getAttribute('data-sip-session-id') ?? '';
            const normalizedNoteId = (noteId ?? '').trim();
            const noteText = typeof note === 'string' ? note : '';
            const normalizedScore = (score ?? '').trim();
            const trimmedLabel = (label ?? '').trim();
            const hasExisting = normalizedNoteId.length > 0
                || noteText.trim().length > 0
                || normalizedScore.length > 0;
            const ownedByCurrentUser = card.dataset.bottleOwnedByCurrentUser ?? card.getAttribute('data-bottle-owned-by-current-user') ?? '';
            const isNoteOnly = (ownedByCurrentUser || '').toLowerCase() === 'false';

            if (hiddenSisterhood) {
                hiddenSisterhood.value = sisterhoodContext ?? '';
            }

            if (hiddenSession) {
                hiddenSession.value = sessionContext ?? '';
            }

            openModal({
                context: 'sip-session',
                label: trimmedLabel,
                bottleId: bottleId ?? '',
                noteId: normalizedNoteId,
                note: noteText,
                score: normalizedScore,
                mode: hasExisting ? 'edit' : 'create',
                card,
                noteOnly: isNoteOnly
            });
        };

        const updatePrompt = (card, hasNote) => {
            const prompt = card?.querySelector('[data-bottle-prompt]');
            if (!prompt) {
                return;
            }

            if (hasNote) {
                prompt.textContent = 'Click to update your tasting note.';
            } else {
                // Remove the prompt entirely when there is no note
                prompt.remove();
            }
        };

        const updateCardFromResponse = (card, payload) => {
            if (!(card instanceof HTMLElement)) {
                return;
            }

            const noteDisplay = card.querySelector('[data-bottle-note-display]');
            const scoreDisplayElement = card.querySelector('[data-bottle-score-display]');
            const averageDisplay = card.querySelector('[data-bottle-average-display]');
            const sisterhoodAverageDisplay = card.querySelector('[data-bottle-sisterhood-average-display]');
            const noteText = (payload.note ?? '').toString();
            const trimmedNote = noteText.trim();
            const scoreValue = payload.score ?? null;
            const averageValue = payload.averageScore ?? null;
            const sisterhoodAverageValue = payload.sisterhoodAverageScore ?? null;
            const noteIdValue = payload.noteId ?? payload.noteID ?? payload.noteID ?? payload.noteIdValue ?? null;
            const normalizedScore = scoreValue == null ? null : Number(scoreValue);

            card.dataset.bottleNote = noteText;
            card.setAttribute('data-bottle-note', noteText);
            card.dataset.bottleScore = normalizedScore == null ? '' : String(normalizedScore);
            card.setAttribute('data-bottle-score', normalizedScore == null ? '' : String(normalizedScore));
            card.dataset.bottleNoteId = noteIdValue ? String(noteIdValue) : '';
            card.setAttribute('data-bottle-note-id', noteIdValue ? String(noteIdValue) : '');

            if (noteDisplay) {
                if (trimmedNote) {
                    noteDisplay.textContent = trimmedNote;
                    noteDisplay.style.display = '';
                } else {
                    // Hide the note display entirely when empty
                    noteDisplay.textContent = '';
                    noteDisplay.style.display = 'none';
                }
            }

            if (scoreDisplayElement) {
                scoreDisplayElement.textContent = normalizedScore == null
                    ? 'Not rated'
                    : `${normalizedScore.toFixed(1)} / 10`;
            }

            if (averageDisplay) {
                if (averageValue == null || averageValue === '') {
                    averageDisplay.textContent = '—';
                } else {
                    const numericAverage = Number(averageValue);
                    averageDisplay.textContent = Number.isFinite(numericAverage)
                        ? `${numericAverage.toFixed(1)} / 10`
                        : '—';
                }
            }

            if (sisterhoodAverageDisplay) {
                if (sisterhoodAverageValue == null || sisterhoodAverageValue === '') {
                    sisterhoodAverageDisplay.textContent = '—';
                } else {
                    const numericAverage = Number(sisterhoodAverageValue);
                    sisterhoodAverageDisplay.textContent = Number.isFinite(numericAverage)
                        ? `${numericAverage.toFixed(1)} / 10`
                        : '—';
                }
            }

            updatePrompt(card, Boolean(trimmedNote || normalizedScore != null));
        };

        const ensureSipSessionContext = () => {
            const card = contextState.card;
            if (!(card instanceof HTMLElement)) {
                return;
            }

            const assignIfMissing = (input, value) => {
                if (!input || input.value) {
                    return;
                }

                const trimmed = typeof value === 'string' ? value.trim() : '';
                if (trimmed) {
                    input.value = trimmed;
                }
            };

            const fallback = (primary, attributeName) => {
                if (primary) {
                    return primary;
                }

                if (typeof attributeName !== 'string' || !attributeName) {
                    return '';
                }

                return card.getAttribute(attributeName) ?? '';
            };

            const dataset = card.dataset ?? {};
            assignIfMissing(hiddenSisterhood, fallback(dataset.sisterhoodId, 'data-sisterhood-id'));
            assignIfMissing(hiddenSession, fallback(dataset.sipSessionId, 'data-sip-session-id'));
            assignIfMissing(hiddenBottle, fallback(dataset.bottleId, 'data-bottle-id'));
        };

        const submitSipSession = async (noteValue, scoreValue) => {
            const card = contextState.card;
            ensureSipSessionContext();
            const sisterhoodId = hiddenSisterhood?.value ?? '';
            const sessionId = hiddenSession?.value ?? '';
            const bottleId = hiddenBottle?.value ?? '';
            const token = tokenInput?.value ?? '';

            if (!card || !sisterhoodId || !sessionId || !bottleId) {
                showFeedback('Missing sip session context. Please refresh and try again.');
                return;
            }

            const payload = new URLSearchParams();
            payload.set('SisterhoodId', sisterhoodId);
            payload.set('SipSessionId', sessionId);
            payload.set('BottleId', bottleId);
            if (contextState.notTasted) {
                payload.set('NotTasted', 'true');
                payload.set('Note', '');
            } else {
                payload.set('Note', noteValue);
                if (scoreValue != null) {
                    payload.set('Score', String(scoreValue));
                }
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
                const rawAction = form.getAttribute('action');
                let endpoint = window.location.href;

                if (rawAction) {
                    const action = rawAction.trim().replace(/^['\"]/,'').replace(/['\"]$/,'');
                    try {
                        endpoint = new URL(action, window.location.origin).toString();
                    } catch (error) {
                        endpoint = action;
                    }
                }

                const response = await fetch(endpoint, requestOptions);
                const contentType = response.headers.get('content-type') ?? '';
                const rawBody = await response.text();
                let parsedPayload = null;

                if (rawBody && contentType.includes('application/json')) {
                    try {
                        parsedPayload = JSON.parse(rawBody);
                    } catch (error) {
                        parsedPayload = null;
                    }
                }

                if (!response.ok) {
                    const message = (parsedPayload && typeof parsedPayload.message === 'string')
                        ? parsedPayload.message
                        : (rawBody || 'Unable to save tasting note.');
                    throw new Error(message);
                }

                if (!parsedPayload || parsedPayload.success !== true) {
                    throw new Error(parsedPayload?.message ?? 'Unable to save tasting note.');
                }

                updateCardFromResponse(card, parsedPayload);
                showFeedback(parsedPayload.message ?? 'Tasting note saved.', 'success');
                scheduleClose();
            } catch (error) {
                const message = error instanceof Error ? error.message : String(error);
                showFeedback(message);
            } finally {
                setLoading(false);
            }
        };

        form.addEventListener('submit', async (event) => {
            event.preventDefault();

            if (loading) {
                return;
            }

            const noteValue = noteInput?.value?.trim() ?? '';
            const scoreRawValue = scoreControl.getRawValue();
            const parsedScore = parseScoreInput(scoreRawValue);

            if (parsedScore === undefined) {
                showFeedback('Score must be between 0 and 10.');
                focusField('score');
                return;
            }

            if (!contextState.notTasted && !noteValue && parsedScore == null) {
                showFeedback('Add a tasting note or score.');
                focusField('note');
                return;
            }

            const isNoteOnly = Boolean(contextState.noteOnly);
            const requireDate = contextState.requireDate && !isNoteOnly;
            let dateValue = dateInput?.value ?? '';

            if (requireDate) {
                if (!dateValue) {
                    showFeedback('Choose when you drank this bottle.');
                    focusField('date');
                    return;
                }

                if (!parseDateOnly(dateValue)) {
                    showFeedback('Choose a valid drinking date.');
                    focusField('date');
                    return;
                }
            } else if (isNoteOnly) {
                dateValue = '';
            }

            if (contextState.context === 'sip-session') {
                await submitSipSession(noteValue, parsedScore);
                return;
            }

            const detail = contextState.data ?? {};
            const external = contextState.external ?? {};

            let submitPromise = null;
            let successMessage = contextState.successMessage;

            const submitDetail = {
                context: contextState.context ?? 'external',
                bottleId: detail.bottleId ?? external.bottleId ?? hiddenBottle?.value ?? '',
                noteId: detail.noteId ?? external.noteId ?? hiddenNoteId?.value ?? '',
                note: noteValue,
                score: parsedScore,
                date: dateValue,
                mode: external.mode ?? form.getAttribute(modalModeAttribute) ?? 'create',
                extras: external,
                requireDate,
                noteOnly: isNoteOnly,
                notTasted: Boolean(contextState.notTasted),
                showError: (message) => {
                    showFeedback(message);
                },
                focusField,
                setSubmitPromise: (promise) => {
                    if (promise && typeof promise.then === 'function') {
                        submitPromise = Promise.resolve(promise);
                    }
                },
                setSuccessMessage: (message) => {
                    if (typeof message === 'string') {
                        successMessage = message;
                    }
                },
                closeModal
            };

            setLoading(true);
            showFeedback('');

            const dispatchSucceeded = window.dispatchEvent(new CustomEvent('drinkmodal:submit', {
                cancelable: true,
                detail: submitDetail
            }));
            const hasSubmitPromise = Boolean(submitPromise);

            if (dispatchSucceeded && !hasSubmitPromise) {
                setLoading(false);
                // No handler configured: silently abort without user-facing error.
                return;
            }

            if (!hasSubmitPromise) {
                setLoading(false);
                showFeedback('Submission handler did not provide a completion promise.');
                return;
            }

            try {
                const result = await submitPromise;
                const message = result?.message ?? successMessage ?? 'Saved.';
                showFeedback(message, 'success');
                scheduleClose();
            } catch (error) {
                const message = error instanceof Error ? error.message : String(error);
                showFeedback(message);
            } finally {
                setLoading(false);
            }
        });

        cancelButton?.addEventListener('click', (event) => {
            event.preventDefault();
            if (!loading) {
                closeModal();
            }
        });

        headerCloseButton?.addEventListener('click', (event) => {
            event.preventDefault();
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

        window.addEventListener('drinkmodal:open', handleExternalOpenEvent);
        window.addEventListener('drinkmodal:close', (event) => {
            const detail = event?.detail ?? {};
            if (!overlay.hidden && (!detail.context || detail.context === contextState.context)) {
                closeModal({ restoreFocus: detail.restoreFocus !== false });
            }
        });

        const allCards = Array.from(document.querySelectorAll('[data-sip-session-bottle-card]'));
        const revealButtons = Array.from(document.querySelectorAll('[data-sip-session-reveal-button]'));
        const revealForms = Array.from(document.querySelectorAll('[data-sip-session-reveal-form]'));
        const removeButtons = Array.from(document.querySelectorAll('[data-sip-session-remove-button]'));
        const removeForms = Array.from(document.querySelectorAll('[data-sip-session-remove-form]'));
        const interactiveCards = allCards.filter(card => card.dataset.bottleInteractive !== 'false');

        const attachBottleActionGuards = (buttons, forms) => {
            buttons.forEach(button => {
                button.addEventListener('click', (event) => {
                    event.stopPropagation();
                });

                button.addEventListener('keydown', (event) => {
                    event.stopPropagation();
                });
            });

            forms.forEach(formElement => {
                formElement.addEventListener('click', (event) => {
                    event.stopPropagation();
                });
            });
        };

        attachBottleActionGuards(revealButtons, revealForms);
        attachBottleActionGuards(removeButtons, removeForms);

        interactiveCards.forEach((card) => {
            card.addEventListener('click', () => {
                if (loading) {
                    return;
                }

                contextState.card = card;
                handleOpenFromCard(card);
            });

            card.addEventListener('keydown', (event) => {
                if (event.key === 'Enter' || event.key === ' ') {
                    event.preventDefault();
                    if (!loading) {
                        contextState.card = card;
                        handleOpenFromCard(card);
                    }
                }
            });
        });
    });
})();
