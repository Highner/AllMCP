(function(){
  'use strict';

  const qs = (sel, root=document) => root.querySelector(sel);
  const qsa = (sel, root=document) => Array.from(root.querySelectorAll(sel));

  const createBtn = qs('#createWishlistBtn');
  const renameBtn = qs('#renameWishlistBtn');
  const deleteBtn = qs('#deleteWishlistBtn');
  const selectEl = qs('#wishlistSelect');

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

  function currentWishlistId(){
    return selectEl && selectEl.value ? selectEl.value : null;
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
})();
