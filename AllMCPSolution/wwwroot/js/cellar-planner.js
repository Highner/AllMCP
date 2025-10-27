(function () {
    function onReady(callback) {
        if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', callback, { once: true });
        } else {
            callback();
        }
    }

    function getToken(form) {
        const tokenInput = form.querySelector('input[name="__RequestVerificationToken"]');
        return tokenInput?.value ?? null;
    }

    function setStatus(statusElement, message, isError) {
        if (!statusElement) {
            return;
        }

        statusElement.textContent = message ?? '';
        statusElement.classList.toggle('cellar-planner__status--error', Boolean(isError && message));
    }

    async function requestPlan(container) {
        const form = container.querySelector('[data-cellar-planner-form]');
        if (!form) {
            return;
        }

        const focusSelect = form.querySelector('[data-cellar-planner-focus]');
        const submitButton = form.querySelector('[data-cellar-planner-submit]');
        const output = form.querySelector('[data-cellar-planner-output]');
        const status = form.querySelector('[data-cellar-planner-status]');
        const focusValue = (focusSelect?.value ?? 'aging').trim().toLowerCase() || 'aging';
        const token = getToken(form);

        if (!submitButton || !output) {
            return;
        }

        setStatus(status, 'Planning your cellar layout…', false);
        submitButton.disabled = true;
        submitButton.setAttribute('aria-busy', 'true');

        try {
            const response = await fetch('/wine-manager/cellar-plan', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'Accept': 'application/json',
                    ...(token ? { 'RequestVerificationToken': token } : {})
                },
                credentials: 'same-origin',
                body: JSON.stringify({ focus: focusValue })
            });

            if (!response.ok) {
                let message = `${response.status} ${response.statusText}`;
                try {
                    const problem = await response.json();
                    if (typeof problem === 'string') {
                        message = problem;
                    } else if (problem?.message) {
                        message = problem.message;
                    } else if (problem?.title) {
                        message = problem.title;
                    }
                } catch (error) {
                    const text = await response.text();
                    if (text) {
                        message = text;
                    }
                }

                throw new Error(message);
            }

            const payload = await response.json();
            const plan = typeof payload?.plan === 'string' ? payload.plan : '';
            output.value = plan;
            setStatus(status, plan ? 'Plan ready! Review the recommendations below.' : 'The planning assistant did not return any guidance.', !plan);
        } catch (error) {
            output.value = '';
            setStatus(status, error?.message ?? 'We could not generate a cellar plan. Please try again.', true);
        } finally {
            submitButton.disabled = false;
            submitButton.removeAttribute('aria-busy');
        }
    }

    function initializePlanner(container) {
        const form = container.querySelector('[data-cellar-planner-form]');
        const submitButton = form?.querySelector('[data-cellar-planner-submit]');
        if (!form || !submitButton) {
            return;
        }

        submitButton.addEventListener('click', (event) => {
            event.preventDefault();
            requestPlan(container);
        });
    }

    onReady(() => {
        const planners = document.querySelectorAll('[data-cellar-planner]');
        planners.forEach((planner) => initializePlanner(planner));
    });
})();
