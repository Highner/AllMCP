(function () {
    'use strict';

    const global = window;
    const namespace = global.WineSurfer = global.WineSurfer || {};

    if (namespace.SipSessionModal) {
        return;
    }

    const state = {
        modal: null,
        activeButton: null,
        activeForm: null
    };

    const ORIGINAL_LABEL_KEY = 'sipSessionOriginalLabel';
    const CLOSE_LABEL_KEY = 'sipSessionModalCloseLabel';

    function getModal() {
        if (!state.modal) {
            state.modal = document.querySelector('[data-sip-session-edit-modal]');
        }

        return state.modal;
    }

    function setModalButtonState(button, isOpen, overrides) {
        if (!button) {
            return;
        }

        const appliedOverrides = overrides || {};

        if (!button.dataset[ORIGINAL_LABEL_KEY]) {
            button.dataset[ORIGINAL_LABEL_KEY] = button.textContent ? button.textContent.trim() : '';
        }

        if (typeof appliedOverrides.defaultLabel === 'string') {
            button.dataset[ORIGINAL_LABEL_KEY] = appliedOverrides.defaultLabel;
        }

        if (typeof appliedOverrides.closeLabel === 'string') {
            button.dataset[CLOSE_LABEL_KEY] = appliedOverrides.closeLabel;
        }

        if (isOpen) {
            const closeLabel = typeof appliedOverrides.closeLabel === 'string'
                ? appliedOverrides.closeLabel
                : (button.dataset[CLOSE_LABEL_KEY] || 'Close');
            button.textContent = closeLabel;
            button.setAttribute('aria-expanded', 'true');
        } else {
            const original = typeof appliedOverrides.defaultLabel === 'string'
                ? appliedOverrides.defaultLabel
                : (button.dataset[ORIGINAL_LABEL_KEY] || '');
            button.textContent = original;
            button.setAttribute('aria-expanded', 'false');
        }
    }

    function isOpen() {
        const modal = getModal();
        return Boolean(modal && !modal.hasAttribute('hidden'));
    }

    function close(options) {
        const modal = getModal();
        if (!modal) {
            return;
        }

        const settings = options || {};
        const container = modal.querySelector('[data-sip-session-edit-modal-body]');

        if (state.activeForm) {
            state.activeForm.reset();
            state.activeForm = null;
        }

        if (container) {
            container.innerHTML = '';
        }

        modal.setAttribute('hidden', '');
        modal.setAttribute('aria-hidden', 'true');

        if (state.activeButton) {
            const trigger = state.activeButton;
            setModalButtonState(trigger, false, { defaultLabel: trigger.dataset[ORIGINAL_LABEL_KEY] || undefined });
            if (settings.restoreFocus !== false) {
                window.requestAnimationFrame(() => trigger.focus());
            }
            state.activeButton = null;
        }
    }

    function ensureCloseHandlers() {
        const modal = getModal();
        if (!modal || modal.dataset.sipSessionModalInitialized === 'true') {
            return;
        }

        modal.dataset.sipSessionModalInitialized = 'true';

        const closeButtons = modal.querySelectorAll('[data-sip-session-edit-close]');
        closeButtons.forEach(button => {
            button.addEventListener('click', () => {
                close();
            });
        });

        modal.addEventListener('click', event => {
            if (event.target === modal) {
                close();
            }
        });
    }

    function open(button, template, options) {
        const modal = getModal();
        if (!modal || !template) {
            return;
        }

        const settings = options || {};

        close({ restoreFocus: false });

        const container = modal.querySelector('[data-sip-session-edit-modal-body]');
        const titleElement = modal.querySelector('[data-sip-session-edit-modal-title]');

        if (!container) {
            return;
        }

        const content = template.content
            ? template.content.cloneNode(true)
            : template.cloneNode(true);

        container.innerHTML = '';
        container.appendChild(content);

        const form = container.querySelector('[data-sip-session-edit-form]');
        state.activeForm = form instanceof HTMLFormElement ? form : null;

        if (state.activeForm) {
            const cancelButton = state.activeForm.querySelector('[data-sip-session-edit-cancel]');
            if (cancelButton) {
                cancelButton.addEventListener('click', () => close());
            }

            state.activeForm.addEventListener('submit', () => {
                window.setTimeout(() => {
                    state.activeForm = null;
                }, 0);
            });
        }

        modal.removeAttribute('hidden');
        modal.setAttribute('aria-hidden', 'false');

        state.activeButton = button;
        setModalButtonState(button, true, {
            closeLabel: settings.closeLabel,
            defaultLabel: button.dataset[ORIGINAL_LABEL_KEY] || undefined
        });

        if (titleElement) {
            titleElement.textContent = settings.title || 'Edit sip session';
        }

        window.requestAnimationFrame(() => {
            const focusSelector = settings.focusSelector;
            let focusTarget = null;
            if (focusSelector) {
                focusTarget = container.querySelector(focusSelector);
            }
            if (!focusTarget) {
                focusTarget = container.querySelector('input[name="Name"]');
            }
            if (focusTarget && typeof focusTarget.focus === 'function') {
                focusTarget.focus();
            }
        });
    }

    function resolveTemplate(button) {
        if (!button) {
            return null;
        }

        const controlsId = button.getAttribute('aria-controls');
        if (controlsId) {
            const region = document.getElementById(controlsId);
            if (region) {
                const regionTemplate = region.querySelector('template[data-sip-session-create-template]');
                if (regionTemplate) {
                    return regionTemplate;
                }
            }
        }

        const container = button.closest('[data-sip-session-create-region]')
            || document.querySelector('[data-sip-session-create-region]');
        return container ? container.querySelector('template[data-sip-session-create-template]') : null;
    }

    function wireCreateButtons(root) {
        const context = root || document;
        const buttons = context.querySelectorAll('[data-sip-session-create-toggle]');

        buttons.forEach(button => {
            if (button.dataset.sipSessionCreateBound === 'true') {
                return;
            }

            button.dataset.sipSessionCreateBound = 'true';

            button.addEventListener('click', () => {
                const template = resolveTemplate(button);
                if (!template) {
                    return;
                }

                if (isOpen() && state.activeButton === button) {
                    close();
                    return;
                }

                open(button, template, {
                    title: button.getAttribute('data-sip-session-create-title') || 'Create sip session',
                    closeLabel: button.getAttribute('data-sip-session-create-close-label') || 'Cancel'
                });
            });
        });
    }

    function init() {
        ensureCloseHandlers();
        wireCreateButtons(document);
    }

    namespace.SipSessionModal = {
        init,
        close,
        openFromTemplate: open,
        isOpen,
        wireCreateButtons,
        getActiveButton: () => state.activeButton,
        setModalButtonState,
        ensureCloseHandlers
    };

    document.addEventListener('DOMContentLoaded', init);
})();
