(function () {
    'use strict';

    function onReady(callback) {
        if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', callback, { once: true });
        } else {
            callback();
        }
    }

    function getWishlistSelect() {
        return document.getElementById('wishlistSelect');
    }

    function getSelectedWishlistId() {
        const select = getWishlistSelect();
        if (!select) {
            return '';
        }

        const value = typeof select.value === 'string' ? select.value.trim() : '';
        return value;
    }

    function normalizeId(value) {
        return typeof value === 'string' ? value.trim().toLowerCase() : '';
    }

    async function fetchJson(url, options = {}) {
        const requestInit = {
            headers: {
                'Accept': 'application/json'
            },
            credentials: 'same-origin',
            ...options
        };

        const response = await fetch(url, requestInit);
        if (!response.ok) {
            let message = `${response.status} ${response.statusText}`;
            try {
                const payload = await response.json();
                if (typeof payload === 'string') {
                    message = payload;
                } else if (payload?.title) {
                    message = payload.title;
                } else if (payload?.message) {
                    message = payload.message;
                }
            } catch (error) {
                if (error) {
                    // Ignore parsing errors, use the default message instead
                }
            }

            throw new Error(message);
        }

        if (response.status === 204) {
            return null;
        }

        const text = await response.text();
        return text ? JSON.parse(text) : null;
    }

    function buildEmptyRow() {
        const row = document.createElement('tr');
        const cell = document.createElement('td');
        cell.colSpan = 3;
        cell.className = 'empty-state';
        cell.textContent = 'No wines in this wishlist yet.';
        row.appendChild(cell);
        return row;
    }

    function buildWishlistRow(item) {
        const row = document.createElement('tr');
        row.className = 'crud-table__row';

        const wineCell = document.createElement('td');
        wineCell.className = 'summary-cell summary-cell--wine';
        const link = document.createElement('a');
        link.className = 'link';
        const wineId = typeof item?.wineId === 'string' ? item.wineId : '';
        link.href = wineId ? `/wine/${encodeURIComponent(wineId)}` : '#';
        link.textContent = typeof item?.name === 'string' && item.name.trim().length > 0
            ? item.name.trim()
            : 'Unnamed wine';
        wineCell.appendChild(link);
        row.appendChild(wineCell);

        const appellationCell = document.createElement('td');
        appellationCell.className = 'summary-cell summary-cell--appellation';
        const appellationSpan = document.createElement('span');
        appellationSpan.className = 'appellation';
        const appellation = typeof item?.appellation === 'string' ? item.appellation.trim() : '';
        const region = typeof item?.region === 'string' ? item.region.trim() : '';
        if (appellation) {
            appellationSpan.textContent = appellation;
        } else if (!region) {
            appellationSpan.textContent = '—';
        }
        appellationCell.appendChild(appellationSpan);
        if (region) {
            const regionSpan = document.createElement('span');
            regionSpan.className = 'region';
            regionSpan.textContent = ` (${region})`;
            appellationCell.appendChild(regionSpan);
        }
        row.appendChild(appellationCell);

        const vintageCell = document.createElement('td');
        vintageCell.className = 'summary-cell summary-cell--vintage';
        const rawVintage = Number.parseInt(item?.vintage, 10);
        if (Number.isFinite(rawVintage)) {
            vintageCell.textContent = String(rawVintage);
        } else {
            vintageCell.textContent = '—';
        }
        row.appendChild(vintageCell);

        return row;
    }

    function renderWishlistItems(items) {
        const section = document.querySelector('[data-crud-table="wishlist-wines"]');
        if (!section) {
            return;
        }

        const tbody = section.querySelector('tbody');
        if (!tbody) {
            return;
        }

        tbody.innerHTML = '';
        if (!Array.isArray(items) || items.length === 0) {
            tbody.appendChild(buildEmptyRow());
            return;
        }

        items.forEach(item => {
            tbody.appendChild(buildWishlistRow(item));
        });
    }

    async function refreshWishlistItems(wishlistId) {
        if (!wishlistId) {
            return;
        }

        try {
            const response = await fetchJson(`/wine-manager/wishlists/${encodeURIComponent(wishlistId)}/wishes`, { method: 'GET' });
            const items = Array.isArray(response) ? response : [];
            renderWishlistItems(items);
        } catch (error) {
            console.error('Failed to refresh wishlist items', error);
        }
    }

    async function updateWishlistSummary(wishlistId) {
        if (!wishlistId) {
            return;
        }

        const select = getWishlistSelect();
        if (!select) {
            return;
        }

        const options = Array.from(select.options);
        const option = options.find(opt => opt.value === wishlistId);
        if (!option) {
            return;
        }

        try {
            const summary = await fetchJson(`/wine-manager/wishlists/${encodeURIComponent(wishlistId)}`, { method: 'GET' });
            if (!summary) {
                return;
            }

            const name = typeof summary.name === 'string' ? summary.name.trim() : '';
            const count = Number.isFinite(summary.wishCount) ? summary.wishCount : Number.parseInt(summary.wishCount, 10);
            if (name) {
                const formattedCount = Number.isFinite(count) ? count : 0;
                option.textContent = `${name} (${formattedCount})`;
            }
        } catch (error) {
            console.error('Failed to update wishlist summary', error);
        }
    }

    function bindAddButton() {
        const trigger = document.querySelector('[data-wishlist-add-trigger]');
        if (!trigger) {
            return;
        }

        trigger.addEventListener('click', (event) => {
            event.preventDefault();
            const api = window.wishlistPopover;
            if (!api || typeof api.open !== 'function') {
                console.error('Wishlist popover is unavailable.');
                return;
            }

            const selectedId = getSelectedWishlistId();
            const context = selectedId ? { wishlistId: selectedId } : {};
            api.open(context).catch(error => {
                console.error('Failed to open wishlist dialog', error);
            });
        });
    }

    function handleWishlistAdded(event) {
        const detail = event?.detail ?? {};
        const addedId = normalizeId(detail.wishlistId);
        if (!addedId) {
            return;
        }

        updateWishlistSummary(detail.wishlistId);

        const selectedId = normalizeId(getSelectedWishlistId());
        if (selectedId && selectedId === addedId) {
            refreshWishlistItems(detail.wishlistId);
        }
    }

    onReady(() => {
        bindAddButton();
        document.addEventListener('wishlist:added', handleWishlistAdded);
    });
})();
