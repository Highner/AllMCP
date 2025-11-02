(function () {
    'use strict';

    const global = window;
    const namespace = global.WineSurferRealtime = global.WineSurferRealtime || {};

    const state = {
        toastContainer: null,
        activeToasts: new Set(),
        fetchPromise: null,
        offlineBanner: null,
        defaultOfflineMessage: null
    };

    function ensureToastContainer() {
        if (state.toastContainer && document.body.contains(state.toastContainer)) {
            return state.toastContainer;
        }

        const container = document.createElement('div');
        container.className = 'realtime-toast-container';
        container.setAttribute('role', 'region');
        container.setAttribute('aria-live', 'polite');
        container.setAttribute('aria-label', 'Wine Surfer updates');
        document.body.appendChild(container);
        state.toastContainer = container;
        return container;
    }

    function removeToast(toast) {
        if (!toast) {
            return;
        }

        state.activeToasts.delete(toast);
        if (toast.parentElement) {
            toast.parentElement.removeChild(toast);
        }
    }

    function highlightElement(element) {
        if (!element) {
            return;
        }

        element.classList.add('realtime-highlight');
        window.setTimeout(() => {
            element.classList.remove('realtime-highlight');
        }, 6000);
    }

    function getOfflineBanner() {
        if (state.offlineBanner && document.body.contains(state.offlineBanner)) {
            return state.offlineBanner;
        }

        state.offlineBanner = document.querySelector('[data-realtime-offline]');
        if (state.offlineBanner && !state.defaultOfflineMessage) {
            state.defaultOfflineMessage = state.offlineBanner.textContent?.trim() || null;
        }

        return state.offlineBanner;
    }

    function markUnavailable(message) {
        const banner = getOfflineBanner();
        if (!banner) {
            return;
        }

        const fallback = state.defaultOfflineMessage || 'Real-time updates are paused. Refresh to catch up on the latest activity.';
        if (typeof message === 'string' && message.trim().length > 0) {
            banner.textContent = message.trim();
        } else {
            banner.textContent = fallback;
        }

        banner.removeAttribute('hidden');
        banner.setAttribute('aria-hidden', 'false');
    }

    function markAvailable() {
        const banner = getOfflineBanner();
        if (!banner) {
            return;
        }

        banner.setAttribute('hidden', '');
        banner.setAttribute('aria-hidden', 'true');
        if (state.defaultOfflineMessage) {
            banner.textContent = state.defaultOfflineMessage;
        }
    }

    function showToast(options) {
        if (!options || typeof options !== 'object') {
            return null;
        }

        const container = ensureToastContainer();
        const toast = document.createElement('div');
        toast.className = 'realtime-toast wine-surface wine-surface-border';
        toast.setAttribute('role', 'status');

        const content = document.createElement('div');
        content.className = 'realtime-toast-body';

        const titleText = typeof options.title === 'string' ? options.title.trim() : '';
        if (titleText) {
            const title = document.createElement('p');
            title.className = 'realtime-toast-title';
            title.textContent = titleText;
            content.appendChild(title);
        }

        const descriptionText = typeof options.description === 'string' ? options.description.trim() : '';
        if (descriptionText) {
            const description = document.createElement('p');
            description.className = 'realtime-toast-description';
            description.textContent = descriptionText;
            content.appendChild(description);
        }

        toast.appendChild(content);

        const actions = document.createElement('div');
        actions.className = 'realtime-toast-actions';

        if (options.action && typeof options.action === 'object' && typeof options.action.label === 'string') {
            const actionButton = document.createElement('button');
            actionButton.type = 'button';
            actionButton.className = 'realtime-toast-action';
            actionButton.textContent = options.action.label.trim() || 'View';
            actionButton.addEventListener('click', () => {
                try {
                    if (typeof options.action.handler === 'function') {
                        options.action.handler();
                    }
                } finally {
                    removeToast(toast);
                }
            });
            actions.appendChild(actionButton);
        }

        const closeButton = document.createElement('button');
        closeButton.type = 'button';
        closeButton.className = 'realtime-toast-close';
        closeButton.setAttribute('aria-label', 'Dismiss notification');
        closeButton.innerHTML = '<span aria-hidden="true">&times;</span>';
        closeButton.addEventListener('click', () => {
            removeToast(toast);
        });
        actions.appendChild(closeButton);

        toast.appendChild(actions);

        container.appendChild(toast);
        state.activeToasts.add(toast);

        while (state.activeToasts.size > 3) {
            const first = state.activeToasts.values().next().value;
            removeToast(first);
        }

        const duration = Number.isFinite(options.duration) ? Number(options.duration) : 9000;
        if (duration > 0) {
            window.setTimeout(() => removeToast(toast), duration);
        }

        return toast;
    }

    function requestLatestDocument() {
        if (state.fetchPromise) {
            return state.fetchPromise;
        }

        const request = fetch(window.location.href, {
            method: 'GET',
            credentials: 'same-origin',
            headers: {
                'X-Requested-With': 'XMLHttpRequest',
                'Accept': 'text/html'
            }
        })
            .then(response => {
                if (!response.ok) {
                    throw new Error('Failed to fetch updated page content.');
                }

                return response.text();
            })
            .then(html => {
                const parser = new DOMParser();
                return parser.parseFromString(html, 'text/html');
            })
            .finally(() => {
                state.fetchPromise = null;
            });

        state.fetchPromise = request;
        return request;
    }

    async function refreshSection(selector, options) {
        if (typeof selector !== 'string' || selector.trim().length === 0) {
            return null;
        }

        const section = document.querySelector(selector);
        if (!section) {
            return null;
        }

        let documentFragment;
        try {
            documentFragment = await requestLatestDocument();
        } catch (error) {
            console.error('Failed to retrieve refreshed content', error);
            return null;
        }

        const freshSection = documentFragment.querySelector(selector);
        if (!freshSection) {
            return null;
        }

        const imported = document.importNode(freshSection, true);
        section.replaceWith(imported);

        const event = new CustomEvent('wine-surfer:section-refreshed', {
            detail: { section: imported, selector }
        });
        document.dispatchEvent(event);

        const settings = options || {};
        let highlightTargets = [];

        if (typeof settings.findHighlight === 'function') {
            const result = settings.findHighlight(imported, documentFragment);
            if (Array.isArray(result)) {
                highlightTargets = result.filter(Boolean);
            } else if (result) {
                highlightTargets = [result];
            }
        } else if (typeof settings.highlightSelector === 'string' && settings.highlightSelector.trim().length > 0) {
            highlightTargets = Array.from(imported.querySelectorAll(settings.highlightSelector));
        }

        highlightTargets.forEach(highlightElement);

        return imported;
    }

    function normalizeSectionObject(section) {
        if (!section || typeof section !== 'object') {
            return null;
        }

        const key = (section.key ?? section.Key ?? '').toString();
        const heading = (section.heading ?? section.Heading ?? '').toString();
        const ariaLabel = (section.ariaLabel ?? section.AriaLabel ?? '').toString();
        const notifications = section.notifications ?? section.Notifications ?? [];
        return { key, heading, ariaLabel, notifications };
    }

    function normalizeNotification(notification) {
        if (!notification || typeof notification !== 'object') {
            return null;
        }

        return {
            category: notification.category ?? notification.Category ?? '',
            stamp: notification.stamp ?? notification.Stamp ?? '',
            title: notification.title ?? notification.Title ?? '',
            body: notification.body ?? notification.Body ?? [],
            meta: notification.meta ?? notification.Meta ?? [],
            tag: notification.tag ?? notification.Tag ?? '',
            url: notification.url ?? notification.Url ?? '',
            occurredAtUtc: notification.occurredAtUtc ?? notification.OccurredAtUtc ?? null,
            dismissLabel: notification.dismissLabel ?? notification.DismissLabel ?? 'Dismiss notification'
        };
    }

    function createNotificationItem(notification) {
        const normalized = normalizeNotification(notification);
        if (!normalized) {
            return null;
        }

        const stamp = normalized.stamp ? normalized.stamp.toString() : `realtime|${Date.now()}|${Math.random().toString(36).slice(2)}`;
        const category = normalized.category ? normalized.category.toString() : 'sisterhood.pending';

        const item = document.createElement('li');
        item.className = 'notification-item';
        item.dataset.notificationStamp = stamp;
        item.dataset.notificationCategory = category;
        item.dataset.sectionKey = 'sisterhood.pending';

        const link = document.createElement('a');
        link.className = 'notification-link';
        const href = normalized.url ? normalized.url.toString() : '';
        if (href) {
            link.href = href;
        } else {
            link.href = '#';
            link.setAttribute('aria-disabled', 'true');
            link.tabIndex = -1;
        }

        const titleText = normalized.title ? normalized.title.toString().trim() : '';
        const title = document.createElement('span');
        title.className = 'notification-title';
        title.textContent = titleText || 'Sisterhood invitation';
        link.appendChild(title);

        const bodySegments = Array.isArray(normalized.body) ? normalized.body : [];
        if (bodySegments.length > 0) {
            const bodySpan = document.createElement('span');
            bodySpan.className = 'notification-meta';
            bodySegments.forEach(segment => {
                if (!segment) {
                    return;
                }

                const text = segment.text ?? segment.Text;
                const emphasize = segment.emphasize ?? segment.Emphasize;
                if (typeof text !== 'string' || text.length === 0) {
                    return;
                }

                if (emphasize) {
                    const strong = document.createElement('strong');
                    strong.textContent = text;
                    bodySpan.appendChild(strong);
                } else {
                    bodySpan.appendChild(document.createTextNode(text));
                }
            });

            if (bodySpan.childNodes.length > 0) {
                link.appendChild(bodySpan);
            }
        }

        const metaSegments = Array.isArray(normalized.meta) ? normalized.meta : [];
        const occurredLabel = normalized.occurredAtUtc ? new Date(normalized.occurredAtUtc).toLocaleString() : '';
        if (metaSegments.length > 0 || occurredLabel) {
            const metaSpan = document.createElement('span');
            metaSpan.className = 'notification-meta notification-meta-secondary';

            let hasMeta = false;
            metaSegments.forEach(segment => {
                if (!segment) {
                    return;
                }

                const text = segment.text ?? segment.Text;
                const emphasize = segment.emphasize ?? segment.Emphasize;
                if (typeof text !== 'string' || text.length === 0) {
                    return;
                }

                hasMeta = true;
                if (emphasize) {
                    const strong = document.createElement('strong');
                    strong.textContent = text;
                    metaSpan.appendChild(strong);
                } else {
                    metaSpan.appendChild(document.createTextNode(text));
                }
            });

            if (occurredLabel) {
                if (hasMeta) {
                    metaSpan.appendChild(document.createTextNode(' · '));
                }

                metaSpan.appendChild(document.createTextNode(occurredLabel));
            }

            if (metaSpan.childNodes.length > 0) {
                link.appendChild(metaSpan);
            }
        }

        if (normalized.tag) {
            const tagSpan = document.createElement('span');
            tagSpan.className = 'notification-tag';
            tagSpan.textContent = normalized.tag.toString();
            link.appendChild(tagSpan);
        }

        item.appendChild(link);

        const dismissButton = document.createElement('button');
        dismissButton.type = 'button';
        dismissButton.className = 'notification-dismiss';
        dismissButton.dataset.notificationStamp = stamp;
        dismissButton.dataset.notificationCategory = category;
        dismissButton.setAttribute('aria-label', normalized.dismissLabel || 'Dismiss notification');
        dismissButton.innerHTML = '<span aria-hidden="true">&times;</span>';

        item.appendChild(dismissButton);
        return item;
    }

    async function refreshSisterhoodInvitations() {
        const panel = document.getElementById('notificationPanel');
        if (!panel) {
            return null;
        }

        const sectionKey = 'sisterhood.pending';
        const sectionSelector = `.notification-section[data-section-key="${sectionKey}"]`;
        const existingSection = panel.querySelector(sectionSelector);
        const existingStamps = new Set();
        if (existingSection) {
            existingSection.querySelectorAll('.notification-item').forEach(item => {
                const stamp = item.dataset.notificationStamp;
                if (stamp) {
                    existingStamps.add(stamp);
                }
            });
        }

        let data;
        try {
            const response = await fetch('/wine-surfer/notifications/sisterhoods/pending', {
                method: 'GET',
                credentials: 'same-origin',
                headers: {
                    'Accept': 'application/json'
                }
            });

            if (!response.ok) {
                throw new Error(`Failed to refresh invitations (${response.status})`);
            }

            data = await response.json();
        } catch (error) {
            console.error('Failed to load sisterhood invitation notifications', error);
            return null;
        }

        const rawSections = Array.isArray(data?.sections)
            ? data.sections
            : Array.isArray(data?.Sections)
                ? data.Sections
                : [];

        const normalizedSections = rawSections
            .map(normalizeSectionObject)
            .filter(section => section && (section.key || section.notifications.length > 0));

        const pendingSection = normalizedSections.find(section => section && section.key.toLowerCase() === sectionKey);

        const notifications = Array.isArray(pendingSection?.notifications)
            ? pendingSection.notifications
            : [];

        let section = existingSection;
        if (!section) {
            section = document.createElement('div');
            section.className = 'notification-section';
            section.dataset.sectionKey = sectionKey;

            const heading = document.createElement('p');
            heading.className = 'notification-subheading';
            heading.textContent = pendingSection?.heading || 'Sisterhood invitations';
            section.appendChild(heading);

            const list = document.createElement('ul');
            list.className = 'notification-list';
            section.appendChild(list);

            const footer = panel.querySelector('.notification-footer');
            const emptyState = panel.querySelector('.notification-empty');
            if (emptyState) {
                panel.insertBefore(section, emptyState);
            } else if (footer) {
                panel.insertBefore(section, footer);
            } else {
                panel.appendChild(section);
            }
        }

        const headingElement = section.querySelector('.notification-subheading');
        if (headingElement) {
            headingElement.textContent = pendingSection?.heading || 'Sisterhood invitations';
        }

        const list = section.querySelector('.notification-list');
        if (!list) {
            return null;
        }

        list.innerHTML = '';

        const createdItems = [];
        notifications.map(normalizeNotification).forEach(notification => {
            const item = createNotificationItem(notification);
            if (item) {
                list.appendChild(item);
                createdItems.push(item);
            }
        });

        const hasNotifications = createdItems.length > 0;
        section.toggleAttribute('hidden', !hasNotifications);
        section.setAttribute('aria-hidden', String(!hasNotifications));

        if (hasNotifications) {
            createdItems.forEach(item => {
                const stamp = item.dataset.notificationStamp;
                if (!stamp || existingStamps.has(stamp)) {
                    return;
                }

                highlightElement(item);
            });
        }

        document.dispatchEvent(new CustomEvent('wine-surfer:notification-panel-refreshed'));

        return section;
    }

    namespace.showToast = showToast;
    namespace.refreshSection = refreshSection;
    namespace.refreshSisterhoodInvitations = refreshSisterhoodInvitations;
    namespace.markUnavailable = markUnavailable;
    namespace.markAvailable = markAvailable;
})();
