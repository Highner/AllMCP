(function(){
  'use strict';

  const selectEl = document.getElementById('wishlist-select');
  const tbody = document.getElementById('wishlist-tbody');
  const emptyRow = document.getElementById('wishlist-empty');

  if (!selectEl || !tbody || !emptyRow) {
    return;
  }

  /**
   * Fetch helper
   */
  async function getJson(url) {
    const res = await fetch(url, { headers: { 'Accept': 'application/json' } });
    if (!res.ok) {
      throw new Error(`Request failed: ${res.status}`);
    }
    return await res.json();
  }

  function clearTbody() {
    while (tbody.firstChild) tbody.removeChild(tbody.firstChild);
  }

  function renderRows(items) {
    clearTbody();
    if (!items || items.length === 0) {
      emptyRow.hidden = false;
      tbody.appendChild(emptyRow);
      return;
    }
    emptyRow.hidden = true;
    for (const item of items) {
      const tr = document.createElement('tr');
      tr.className = 'inventory-row summary-row';
      tr.innerHTML = `
        <td class="summary-cell">${escapeHtml(item.name)}</td>
        <td class="summary-cell">${escapeHtml(item.region)}</td>
        <td class="summary-cell">${escapeHtml(item.appellation)}</td>
        <td class="summary-cell">${escapeHtml(String(item.vintage ?? ''))}</td>
      `;
      tbody.appendChild(tr);
    }
  }

  function escapeHtml(str) {
    return String(str ?? '')
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#039;');
  }

  async function loadWishesFor(id) {
    if (!id) {
      renderRows([]);
      return;
    }
    try {
      const items = await getJson(`/wine-manager/wishlists/${id}/wishes`);
      renderRows(items);
    } catch (e) {
      console.error(e);
      renderRows([]);
    }
  }

  async function init() {
    try {
      const lists = await getJson('/wine-manager/wishlists');
      // Populate selector
      selectEl.innerHTML = '';
      for (const list of lists) {
        const opt = document.createElement('option');
        opt.value = list.id;
        opt.textContent = `${list.name} (${list.wishCount})`;
        selectEl.appendChild(opt);
      }
      if (lists.length > 0) {
        selectEl.value = lists[0].id;
        await loadWishesFor(lists[0].id);
      } else {
        await loadWishesFor(null);
      }
    } catch (e) {
      console.error(e);
      await loadWishesFor(null);
    }

    selectEl.addEventListener('change', async (e) => {
      const id = e.target.value;
      await loadWishesFor(id);
    });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})();
