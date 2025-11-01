(function(){
  'use strict';

  const SELECTORS = {
    overlay: '#choose-bottles-overlay',
    dialog: '#choose-bottles-modal',
    closeBtn: '[data-choose-bottles-close]',
    cancelBtn: '.choose-bottles-cancel',
    selectBtn: '.choose-bottles-select',
    tbody: '#choose-bottles-tbody',
    filter: '.choose-bottles-filter-input'
  };

  const state = {
    bottles: [],
    filtered: [],
    selected: new Set(),
    mounted: false,
    focusTrapEl: null
  };

  function qs(root, sel){ return (root||document).querySelector(sel); }
  function qsa(root, sel){ return Array.from((root||document).querySelectorAll(sel)); }

  function open(){
    const overlay = qs(document, SELECTORS.overlay);
    const dialog = qs(document, SELECTORS.dialog);
    if(!overlay || !dialog){ return; }
    overlay.removeAttribute('hidden');
    overlay.setAttribute('aria-hidden','false');
    // initial fetch on first open
    if(!state.mounted){
      mount(dialog);
      state.mounted = true;
    }
    fetchAndRender();
    // focus the filter
    const filter = qs(dialog, SELECTORS.filter);
    if(filter){ filter.focus(); }
    document.addEventListener('keydown', handleKeydown);
  }

  function close(reason = 'dismissed'){
    const overlay = qs(document, SELECTORS.overlay);
    const dialog = qs(document, SELECTORS.dialog);
    if(!overlay){ return; }
    overlay.setAttribute('aria-hidden','true');
    overlay.setAttribute('hidden','');
    document.removeEventListener('keydown', handleKeydown);
    if(dialog){
      const detail = {
        reason,
        selectedIds: Array.from(state.selected)
      };
      dialog.dispatchEvent(new CustomEvent('choose-bottles:closed', { detail, bubbles: true }));
    }
  }

  function mount(dialog){
    // wire close
    qsa(dialog, SELECTORS.closeBtn).forEach(btn => btn.addEventListener('click', () => close('close')));
    const cancel = qs(dialog, SELECTORS.cancelBtn);
    if(cancel){ cancel.addEventListener('click', () => close('cancel')); }

    const filter = qs(dialog, SELECTORS.filter);
    if(filter){ filter.addEventListener('input', applyFilter); }

    const selectBtn = qs(dialog, SELECTORS.selectBtn);
    if(selectBtn){ selectBtn.addEventListener('click', emitSelection); }
  }

  function handleKeydown(e){
    if(e.key === 'Escape'){ close('escape'); }
  }

  async function fetchAndRender(){
    const tbody = qs(document, SELECTORS.tbody);
    const selectBtn = qs(document, SELECTORS.selectBtn);
    if(tbody){ tbody.innerHTML = '<tr class="empty-row"><td colspan="5">Loading available bottles…</td></tr>'; }
    if(selectBtn){ selectBtn.disabled = true; }
    state.selected.clear();

    try{
      const res = await fetch('/wine-manager/available-bottles', { headers: { 'Accept':'application/json' } });
      if(!res.ok){ throw new Error('Failed to load bottles'); }
      const data = await res.json();
      state.bottles = Array.isArray(data) ? data : [];
      applyFilter();
    }catch(err){
      if(tbody){ tbody.innerHTML = '<tr class="empty-row"><td colspan="5">Unable to load bottles.</td></tr>'; }
      console.error(err);
    }
  }

  function applyFilter(){
    const filter = qs(document, SELECTORS.filter);
    const q = (filter?.value || '').trim().toLowerCase();
    if(!q){
      state.filtered = state.bottles.slice();
    }else{
      state.filtered = state.bottles.filter(b => {
        const name = (b.wine || b.Wine || '').toString().toLowerCase();
        const app = (b.appellation || b.Appellation || '').toString().toLowerCase();
        return name.includes(q) || app.includes(q);
      });
    }
    renderRows();
  }

  function renderRows(){
    const tbody = qs(document, SELECTORS.tbody);
    if(!tbody){ return; }
    if(state.filtered.length === 0){
      tbody.innerHTML = '<tr class="empty-row"><td colspan="5">No bottles match your filter.</td></tr>';
      return;
    }
    const rows = state.filtered.map(b => buildRowHtml(b)).join('');
    tbody.innerHTML = rows;
    // wire checkboxes
    qsa(tbody, 'input[type="checkbox"][data-bottle-id]').forEach(cb => {
      cb.addEventListener('change', onRowToggle);
    });
  }

  function buildRowHtml(b){
    const id = (b.id || b.Id);
    const wine = escapeHtml((b.wine || b.Wine || ''));
    const vintage = (b.vintage || b.Vintage) ?? '';
    const appellation = escapeHtml((b.appellation || b.Appellation || ''));
    const location = escapeHtml((b.location || b.Location || ''));
    const checked = state.selected.has(id) ? ' checked' : '';
    return (
      '<tr>'+
        '<td class="choose-col-select"><input type="checkbox" data-bottle-id="'+id+'"'+checked+' aria-label="Select bottle"></td>'+
        '<td class="choose-col-wine">'+wine+'</td>'+
        '<td class="choose-col-vintage">'+vintage+'</td>'+
        '<td class="choose-col-appellation">'+appellation+'</td>'+
        '<td class="choose-col-location">'+location+'</td>'+
      '</tr>'
    );
  }

  function onRowToggle(e){
    const cb = e.currentTarget;
    const idStr = cb.getAttribute('data-bottle-id');
    try{
      const id = idStr; // GUID as string
      if(cb.checked){ state.selected.add(id); }
      else { state.selected.delete(id); }
    }catch{}
    updateSelectButton();
  }

  function updateSelectButton(){
    const selectBtn = qs(document, SELECTORS.selectBtn);
    if(selectBtn){ selectBtn.disabled = state.selected.size === 0; }
  }

  function emitSelection(){
    const dialog = qs(document, SELECTORS.dialog);
    if(!dialog){ return; }
    const ids = Array.from(state.selected);
    const evt = new CustomEvent('choose-bottles:selected', { detail: { ids }, bubbles: true });
    dialog.dispatchEvent(evt);
    close('selected');
  }

  function escapeHtml(s){
    return String(s)
      .replaceAll('&','&amp;')
      .replaceAll('<','&lt;')
      .replaceAll('>','&gt;')
      .replaceAll('"','&quot;')
      .replaceAll("'",'&#39;');
  }

  // Public API: allow external triggers to open the modal
  window.ChooseBottlesModal = {
    open,
    close: reason => close(reason)
  };
})();
