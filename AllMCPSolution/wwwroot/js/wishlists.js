(function(){
  'use strict';

  const qs = (sel, root=document) => root.querySelector(sel);
  const qsa = (sel, root=document) => Array.from(root.querySelectorAll(sel));

  const createBtn = qs('#createWishlistBtn');
  const renameBtn = qs('#renameWishlistBtn');
  const deleteBtn = qs('#deleteWishlistBtn');
  const selectEl = qs('#wishlistSelect');
  const addWineTrigger = qs('[data-wishlist-add-trigger]');
  const addButtonSelector = '[data-wishlist-add-button]';
  const messageSelector = '[data-wishlist-message]';
  const minVintage = 1900;
  const maxVintage = 2100;

  // Create modal elements
  const createOverlay = qs('#create-wishlist-overlay');
  const createModal = qs('#create-wishlist-modal');
  const createForm = qs('#create-wishlist-form');
  const createName = qs('#create-wishlist-name');
  const createError = qs('#create-wishlist-error');
  const createClose = qs('[data-close-create]');

  // Rename modal elements
  const renameOverlay = qs('#rename-wishlist-overlay');
  const renameModal = qs('#rename-wishlist-modal');
  const renameForm = qs('#rename-wishlist-form');
  const renameName = qs('#rename-wishlist-name');
  const renameError = qs('#rename-wishlist-error');
  const renameClose = qs('[data-close-rename]');

  function openOverlay(overlay){
    if (!overlay) return;
    overlay.removeAttribute('hidden');
    overlay.setAttribute('aria-hidden', 'false');
  }
  function closeOverlay(overlay){
    if (!overlay) return;
    overlay.setAttribute('hidden', '');
    overlay.setAttribute('aria-hidden', 'true');
  }

  function currentWishlistOption(){
    if (!selectEl) return null;
    const index = selectEl.selectedIndex;
    if (index < 0 || index >= selectEl.options.length) return null;
    return selectEl.options[index];
  }

  function currentWishlistId(){
    return selectEl && selectEl.value ? selectEl.value : null;
  }

  function buildWishlistContext(){
    const context = { source: 'wishlists' };
    const id = currentWishlistId();
    if (id) {
      context.wishlistId = id;
    }

    const option = currentWishlistOption();
    if (option && option.textContent) {
      const text = option.textContent.split(' (')[0].trim();
      if (text) {
        context.wishlistName = text;
      }
    }

    return context;
  }

  function toTrimmedString(value){
    if (typeof value === 'string') {
      return value.trim();
    }

    if (value === null || value === undefined) {
      return '';
    }

    return String(value).trim();
  }

  function parseVintage(value){
    const text = toTrimmedString(value);
    if (!text) {
      return null;
    }

    const parsed = Number.parseInt(text, 10);
    if (!Number.isInteger(parsed) || parsed < minVintage || parsed > maxVintage) {
      return null;
    }

    return parsed;
  }

  function showWishlistStatus(text, state){
    const element = qs(messageSelector);
    if (!element) return;

    const message = toTrimmedString(text);
    if (!message) {
      element.textContent = '';
      element.hidden = true;
      element.removeAttribute('data-state');
      return;
    }

    element.textContent = message;
    element.hidden = false;
    if (state) {
      element.setAttribute('data-state', state);
    } else {
      element.removeAttribute('data-state');
    }
  }

  function escapeSelectorValue(value){
    const text = toTrimmedString(value);
    if (!text) {
      return '';
    }

    if (typeof CSS !== 'undefined' && typeof CSS.escape === 'function') {
      return CSS.escape(text);
    }

    return text.replace(/["'\\]/g, '\\$&');
  }

  async function sendJson(url, options){
    const requestInit = {
      headers: {
        'Content-Type': 'application/json',
        'Accept': 'application/json'
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
        } else if (problem?.errors) {
          const firstError = Object.values(problem.errors)[0];
          if (Array.isArray(firstError) && firstError.length > 0) {
            message = firstError[0];
          }
        }
      } catch {
        try {
          const text = await response.text();
          if (text) {
            message = text;
          }
        } catch {
          // ignore
        }
      }

      throw new Error(message);
    }

    if (response.status === 204) {
      return {};
    }

    try {
      return await response.json();
    } catch {
      return {};
    }
  }

  function showError(el, msg){
    if (!el) return;
    el.textContent = msg || '';
    if (msg) {
      el.removeAttribute('aria-hidden');
    } else {
      el.setAttribute('aria-hidden', 'true');
    }
  }

  function jsonHeaders(){
    return { 'Content-Type': 'application/json' };
  }

  function handleFetchError(res){
    if (res.ok) return null;
    return res.json().catch(() => ({})).then(data => {
      const errors = [];
      if (data && data.errors) {
        for (const key in data.errors) {
          const arr = data.errors[key];
          if (Array.isArray(arr)) errors.push(...arr);
        }
      }
      if (data && data.message) errors.push(data.message);
      return errors.length ? errors.join(' ') : 'Something went wrong. Please try again.';
    });
  }

  function wishlistTableBody(){
    return qs('[data-crud-table="wishlist-wines"] tbody');
  }

  function normalizeWishlistName(optionText){
    if (typeof optionText !== 'string') return '';
    const idx = optionText.lastIndexOf(' (');
    const name = idx >= 0 ? optionText.substring(0, idx) : optionText;
    return name.trim();
  }

  function updateWishlistTitle(forcedName){
    const titleEl = qs('[data-crud-table="wishlist-wines"] .crud-table__title');
    if (!titleEl) return;

    let titleText = '';
    if (typeof forcedName === 'string' && forcedName.trim().length > 0) {
      titleText = forcedName.trim();
    } else {
      const option = currentWishlistOption();
      titleText = normalizeWishlistName(option?.textContent ?? '') || 'Wishlist';
    }

    titleEl.textContent = titleText;
  }

  function renderWishlistMessage(message){
    const tbody = wishlistTableBody();
    if (!tbody) return;
    showWishlistStatus('', '');
    tbody.innerHTML = '';
    const row = document.createElement('tr');
    const cell = document.createElement('td');
    cell.colSpan = 4;
    cell.className = 'empty-state';
    cell.textContent = message;
    row.appendChild(cell);
    tbody.appendChild(row);
  }

  function renderWishlistItems(items){
    const tbody = wishlistTableBody();
    if (!tbody) return;

    showWishlistStatus('', '');
    tbody.innerHTML = '';
    if (!Array.isArray(items) || items.length === 0) {
      renderWishlistMessage('No wines in this wishlist yet.');
      return;
    }

    const fragment = document.createDocumentFragment();
    items.forEach(item => {
      const row = document.createElement('tr');
      row.className = 'crud-table__row';

      const wineCell = document.createElement('td');
      wineCell.className = 'summary-cell summary-cell--wine';
      const wineLink = document.createElement('a');
      wineLink.className = 'link';
      const wineId = item?.wineId ? String(item.wineId) : '';
      const wineName = toTrimmedString(item?.name) || 'Wine';
      wineLink.href = wineId ? `/wine/${wineId}` : '#';
      wineLink.textContent = wineName;
      wineCell.appendChild(wineLink);
      row.appendChild(wineCell);

      const appellationCell = document.createElement('td');
      appellationCell.className = 'summary-cell summary-cell--appellation';
      const appellation = document.createElement('span');
      appellation.className = 'appellation';
      appellation.textContent = (item?.appellation ?? '').trim();
      appellationCell.appendChild(appellation);
      const regionText = (item?.region ?? '').trim();
      if (regionText) {
        const region = document.createElement('span');
        region.className = 'region';
        region.textContent = ` (${regionText})`;
        appellationCell.appendChild(region);
      }
      row.appendChild(appellationCell);

      const vintageCell = document.createElement('td');
      vintageCell.className = 'summary-cell summary-cell--vintage';
      const vintage = Number.parseInt(item?.vintage, 10);
      vintageCell.textContent = Number.isFinite(vintage) ? String(vintage) : '';
      row.appendChild(vintageCell);

      const actionsCell = document.createElement('td');
      actionsCell.className = 'summary-cell summary-cell--actions';
      const actionsWrapper = document.createElement('div');
      actionsWrapper.className = 'wishlist-actions';

      const addButton = document.createElement('button');
      addButton.type = 'button';
      addButton.className = 'sisterhood-button wishlist-actions__button';
      addButton.dataset.wishlistAddButton = '';
      if (wineId) {
        addButton.dataset.wineId = wineId;
      }
      addButton.dataset.wineName = wineName;
      addButton.dataset.wineCountry = toTrimmedString(item?.country);
      addButton.dataset.wineRegion = regionText;
      addButton.dataset.wineAppellation = toTrimmedString(item?.appellation);
      addButton.dataset.wineSubAppellation = toTrimmedString(item?.subAppellation);
      addButton.dataset.wineColor = toTrimmedString(item?.color);
      addButton.dataset.wineVariety = toTrimmedString(item?.variety);
      addButton.dataset.wineVintage = Number.isFinite(vintage) ? String(vintage) : '';
      const isInInventory = Boolean(item?.isInInventory);
      const addLabel = isInInventory ? 'In Inventory' : 'add';
      addButton.textContent = addLabel;
      const ariaLabel = isInInventory
        ? `${wineName} is already in your inventory`
        : `Add ${wineName} to your inventory`;
      addButton.setAttribute('aria-label', ariaLabel);
      if (isInInventory) {
        addButton.disabled = true;
      }
      actionsWrapper.appendChild(addButton);

      const buyLink = document.createElement('a');
      buyLink.className = 'sisterhood-button wishlist-actions__button';
      const rawUrl = toTrimmedString(item?.wineSearcherUrl);
      buyLink.href = rawUrl || 'https://www.wine-searcher.com/';
      buyLink.target = '_blank';
      buyLink.rel = 'noopener noreferrer';
      buyLink.textContent = 'buy';
      buyLink.setAttribute('aria-label', `Buy ${wineName} on Wine-Searcher`);
      actionsWrapper.appendChild(buyLink);

      actionsCell.appendChild(actionsWrapper);
      row.appendChild(actionsCell);

      fragment.appendChild(row);
    });

    tbody.appendChild(fragment);
  }

  async function handleWishlistAdd(button){
    if (!button || button.disabled || button.dataset?.loading === 'true') {
      return;
    }

    const dataset = button.dataset ?? {};
    const wineId = toTrimmedString(dataset.wineId);
    if (!wineId) {
      showWishlistStatus('Wine information is missing. Please refresh and try again.', 'error');
      return;
    }

    const wineName = toTrimmedString(dataset.wineName) || 'Wine';
    const vintageValue = parseVintage(dataset.wineVintage);
    if (vintageValue === null) {
      const openAddWineModal = window.wineSurferFavorites?.openAddWineModal;
      if (typeof openAddWineModal === 'function') {
        openAddWineModal({
          source: 'wishlist',
          id: wineId,
          name: dataset.wineName,
          country: dataset.wineCountry,
          region: dataset.wineRegion,
          appellation: dataset.wineAppellation,
          subAppellation: dataset.wineSubAppellation,
          color: dataset.wineColor,
          variety: dataset.wineVariety,
          vintage: dataset.wineVintage
        }).catch(() => {
          // errors handled within modal
        });
      }

      showWishlistStatus('Select a vintage in the add wine dialog to add this wine to your inventory.', 'error');
      return;
    }

    const originalLabel = button.textContent;
    const originalAriaLabel = button.getAttribute('aria-label') || '';
    const successAriaLabel = `${wineName} is already in your inventory`;

    showWishlistStatus('', '');
    button.disabled = true;
    button.dataset.loading = 'true';
    button.textContent = 'Adding…';

    try {
      await sendJson('/wine-manager/inventory', {
        method: 'POST',
        body: JSON.stringify({
          wineId,
          vintage: vintageValue,
          quantity: 1,
          bottleLocationId: null
        })
      });

      showWishlistStatus(`${wineName} added to your inventory.`, 'success');
      const selectorId = escapeSelectorValue(wineId);
      const matchingSelector = selectorId
        ? `${addButtonSelector}[data-wine-id="${selectorId}"]`
        : addButtonSelector;
      qsa(matchingSelector).forEach(element => {
        element.disabled = true;
        element.textContent = 'In Inventory';
        element.setAttribute('aria-label', successAriaLabel);
        if (element.dataset) {
          delete element.dataset.loading;
        }
      });
    } catch (error) {
      const message = error?.message || 'Unable to add this wine to your inventory.';
      showWishlistStatus(message, 'error');
      button.disabled = false;
      button.textContent = originalLabel;
      if (originalAriaLabel) {
        button.setAttribute('aria-label', originalAriaLabel);
      } else {
        button.removeAttribute('aria-label');
      }
    } finally {
      if (button.dataset) {
        delete button.dataset.loading;
      }
    }
  }

  async function fetchWishlistSummaries(){
    const response = await fetch('/wine-manager/wishlists', { headers: { 'Accept': 'application/json' }});
    if (!response.ok) {
      const message = await handleFetchError(response);
      throw new Error(message || 'Unable to load wishlists.');
    }
    const data = await response.json();
    return Array.isArray(data) ? data : [];
  }

  function setWishlistOptions(wishlists, preferredId){
    if (!selectEl) return null;

    const lists = Array.isArray(wishlists) ? wishlists.slice() : [];
    lists.sort((a, b) => {
      const nameA = (a?.name ?? '').toString().toLowerCase();
      const nameB = (b?.name ?? '').toString().toLowerCase();
      return nameA.localeCompare(nameB);
    });

    const previousValue = currentWishlistId();
    const ids = new Set(lists.map(entry => entry?.id ? String(entry.id) : ''));

    let selectedId = null;
    if (previousValue && ids.has(previousValue)) {
      selectedId = previousValue;
    } else if (preferredId && ids.has(preferredId)) {
      selectedId = preferredId;
    } else if (lists.length > 0) {
      const first = lists.find(entry => entry?.id);
      selectedId = first ? String(first.id) : null;
    }

    selectEl.innerHTML = '';

    if (lists.length === 0) {
      const option = document.createElement('option');
      option.value = '';
      option.textContent = 'No wishlists yet';
      option.selected = true;
      selectEl.appendChild(option);
      selectEl.value = '';
      return null;
    }

    const fragment = document.createDocumentFragment();
    lists.forEach(entry => {
      if (!entry?.id) {
        return;
      }
      const option = document.createElement('option');
      const id = String(entry.id);
      option.value = id;
      const name = (entry.name ?? '').toString().trim() || 'Wishlist';
      const count = Number.isFinite(entry.wishCount) ? entry.wishCount : 0;
      option.textContent = `${name} (${count})`;
      if (selectedId && id === selectedId) {
        option.selected = true;
      }
      fragment.appendChild(option);
    });

    selectEl.appendChild(fragment);
    if (selectedId) {
      selectEl.value = selectedId;
    }

    return selectedId;
  }

  async function refreshWishlistOptions(preferredId){
    try {
      const wishlists = await fetchWishlistSummaries();
      const selectedId = setWishlistOptions(wishlists, preferredId);
      updateWishlistTitle();
      return selectedId;
    } catch (error) {
      console.error('Unable to refresh wishlist options', error);
      return currentWishlistId();
    }
  }

  async function refreshWishlistItems(wishlistId){
    if (!wishlistId) {
      return;
    }

    renderWishlistMessage('Updating wishlist…');
    try {
      const response = await fetch(`/wine-manager/wishlists/${encodeURIComponent(wishlistId)}/wishes`, {
        headers: { 'Accept': 'application/json' }
      });
      if (!response.ok) {
        const message = await handleFetchError(response);
        throw new Error(message || 'Unable to update wishlist.');
      }
      const data = await response.json();
      renderWishlistItems(Array.isArray(data) ? data : []);
    } catch (error) {
      console.error('Unable to refresh wishlist items', error);
      renderWishlistMessage(error?.message || 'Unable to update wishlist right now.');
    }
  }

  async function handleWishlistAdded(event){
    if (!selectEl) {
      return;
    }

    const detail = event?.detail ?? {};
    const wishlistId = detail?.wishlistId ? String(detail.wishlistId) : '';
    const wishlistName = typeof detail?.wishlistName === 'string' ? detail.wishlistName : '';

    const selectedId = await refreshWishlistOptions(wishlistId);

    if (wishlistId && selectedId === wishlistId) {
      await refreshWishlistItems(wishlistId);
      updateWishlistTitle(wishlistName);
    } else {
      updateWishlistTitle();
    }
  }

  if (addWineTrigger) {
    addWineTrigger.addEventListener('click', (event) => {
      const openWishlist = window.wishlistPopover?.open;
      if (typeof openWishlist !== 'function') {
        console.warn('Wishlist popover is unavailable.');
        return;
      }

      event.preventDefault();
      event.stopPropagation();

      const context = buildWishlistContext();
      openWishlist(context).catch(error => {
        console.error('Unable to open wishlist popover', error);
        alert('Unable to open the add wine dialog right now.');
      });
    });
  }

  document.addEventListener('click', (event) => {
    const button = event.target.closest(addButtonSelector);
    if (!button) {
      return;
    }

    event.preventDefault();
    handleWishlistAdd(button).catch(() => {
      // feedback handled via showWishlistStatus
    });
  });

  // Create wishlist
  if (createBtn && createOverlay && createForm) {
    createBtn.addEventListener('click', () => {
      showError(createError, '');
      createName && (createName.value = '');
      openOverlay(createOverlay);
      setTimeout(() => createName && createName.focus(), 0);
    });
    createClose && createClose.addEventListener('click', () => closeOverlay(createOverlay));

    createForm.addEventListener('submit', async (e) => {
      e.preventDefault();
      const name = (createName && createName.value || '').trim();
      if (!name) {
        showError(createError, 'Please enter a name.');
        return;
      }

      try {
        const res = await fetch('/wine-manager/wishlists', {
          method: 'POST',
          headers: jsonHeaders(),
          body: JSON.stringify({ name })
        });
        if (!res.ok) {
          const msg = await handleFetchError(res);
          showError(createError, msg);
          return;
        }
        const data = await res.json();
        // Redirect to new wishlist
        window.location = '/wine-surfer/wishlists?wishlistId=' + encodeURIComponent(data.id);
      } catch {
        showError(createError, 'Network error. Please try again.');
      }
    });
  }

  // Rename wishlist
  if (renameBtn && renameOverlay && renameForm) {
    renameBtn.addEventListener('click', () => {
      const id = currentWishlistId();
      if (!id) return;
      showError(renameError, '');
      // preload current name from selected option text (before count and parentheses)
      const opt = selectEl.options[selectEl.selectedIndex];
      let name = '';
      if (opt) {
        const text = opt.textContent || '';
        const idx = text.lastIndexOf(' (');
        name = idx > 0 ? text.substring(0, idx) : text;
      }
      renameName && (renameName.value = name.trim());
      openOverlay(renameOverlay);
      setTimeout(() => renameName && renameName.focus(), 0);
    });
    renameClose && renameClose.addEventListener('click', () => closeOverlay(renameOverlay));

    renameForm.addEventListener('submit', async (e) => {
      e.preventDefault();
      const id = currentWishlistId();
      if (!id) return;
      const name = (renameName && renameName.value || '').trim();
      if (!name) {
        showError(renameError, 'Please enter a new name.');
        return;
      }
      try {
        const res = await fetch(`/wine-manager/wishlists/${encodeURIComponent(id)}`, {
          method: 'PUT',
          headers: jsonHeaders(),
          body: JSON.stringify({ name })
        });
        if (!res.ok) {
          const msg = await handleFetchError(res);
          showError(renameError, msg);
          return;
        }
        // reload to update names and header
        window.location = '/wine-surfer/wishlists?wishlistId=' + encodeURIComponent(id);
      } catch {
        showError(renameError, 'Network error. Please try again.');
      }
    });
  }

  // Delete wishlist
  if (deleteBtn) {
    deleteBtn.addEventListener('click', async () => {
      const id = currentWishlistId();
      if (!id) return;
      const opt = selectEl.options[selectEl.selectedIndex];
      const name = (opt && (opt.textContent || '')).split(' (')[0];
      if (!confirm(`Are you sure you want to delete the wishlist "${name}"? This cannot be undone.`)) {
        return;
      }
      try {
        const res = await fetch(`/wine-manager/wishlists/${encodeURIComponent(id)}`, { method: 'DELETE' });
        if (!res.ok && res.status !== 204) {
          alert('Failed to delete the wishlist.');
          return;
        }
        // After delete, redirect to list without wishlistId to pick default
        window.location = '/wine-surfer/wishlists';
      } catch {
        alert('Network error. Please try again.');
      }
    });
  }

  // Close modals when clicking backdrop
  [createOverlay, renameOverlay].forEach(overlay => {
    if (!overlay) return;
    overlay.addEventListener('click', (e) => {
      if (e.target === overlay) closeOverlay(overlay);
    });
  });

  document.addEventListener('wishlist:added', (event) => {
    handleWishlistAdded(event).catch(error => {
      console.error('Wishlist update failed', error);
    });
  });
})();
