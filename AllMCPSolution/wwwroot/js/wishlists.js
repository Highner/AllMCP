(function(){
  'use strict';

  const qs = (sel, root=document) => root.querySelector(sel);
  const qsa = (sel, root=document) => Array.from(root.querySelectorAll(sel));

  const createBtn = qs('#createWishlistBtn');
  const renameBtn = qs('#renameWishlistBtn');
  const deleteBtn = qs('#deleteWishlistBtn');
  const selectEl = qs('#wishlistSelect');
  const addWineTrigger = qs('[data-wishlist-add-trigger]');

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
    tbody.innerHTML = '';
    const row = document.createElement('tr');
    const cell = document.createElement('td');
    cell.colSpan = 3;
    cell.className = 'empty-state';
    cell.textContent = message;
    row.appendChild(cell);
    tbody.appendChild(row);
  }

  function renderWishlistItems(items){
    const tbody = wishlistTableBody();
    if (!tbody) return;

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
      wineLink.href = wineId ? `/wine/${wineId}` : '#';
      wineLink.textContent = (item?.name ?? '').trim() || 'Wine';
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

      fragment.appendChild(row);
    });

    tbody.appendChild(fragment);
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
