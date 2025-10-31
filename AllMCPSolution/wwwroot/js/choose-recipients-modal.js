(function(){
  'use strict';

  const SELECTORS = {
    overlay: '#choose-recipients-overlay',
    dialog: '#choose-recipients-modal',
    closeBtn: '[data-choose-recipients-close]',
    cancelBtn: '.choose-recipients-cancel',
    selectBtn: '.choose-recipients-select',
    tbody: '#choose-recipients-tbody',
    filter: '.choose-recipients-filter-input'
  };

  const state = {
    users: [],
    filtered: [],
    selected: new Set(),
    mounted: false
  };

  function qs(root, selector){
    return (root || document).querySelector(selector);
  }

  function qsa(root, selector){
    return Array.from((root || document).querySelectorAll(selector));
  }

  function open(options){
    const overlay = qs(document, SELECTORS.overlay);
    const dialog = qs(document, SELECTORS.dialog);
    if(!overlay || !dialog){
      return;
    }

    const preselect = Array.isArray(options?.preselect)
      ? new Set(options.preselect.map(id => id != null ? String(id) : ''))
      : null;

    overlay.removeAttribute('hidden');
    overlay.setAttribute('aria-hidden', 'false');

    if(!state.mounted){
      mount(dialog);
      state.mounted = true;
    }

    if(state.users.length === 0){
      state.users = loadUsers();
    }

    state.selected.clear();
    if(preselect){
      preselect.forEach(id => {
        if(id){
          state.selected.add(id);
        }
      });
    }

    applyFilter();
    updateSelectButton();

    const filter = qs(dialog, SELECTORS.filter);
    if(filter){
      filter.focus();
      filter.select();
    }

    document.addEventListener('keydown', handleKeydown);
  }

  function close(){
    const overlay = qs(document, SELECTORS.overlay);
    if(!overlay){
      return;
    }

    overlay.setAttribute('aria-hidden', 'true');
    overlay.setAttribute('hidden', '');
    document.removeEventListener('keydown', handleKeydown);
  }

  function mount(dialog){
    qsa(dialog, SELECTORS.closeBtn).forEach(btn => btn.addEventListener('click', close));

    const cancel = qs(dialog, SELECTORS.cancelBtn);
    if(cancel){
      cancel.addEventListener('click', close);
    }

    const filter = qs(dialog, SELECTORS.filter);
    if(filter){
      filter.addEventListener('input', applyFilter);
    }

    const selectBtn = qs(dialog, SELECTORS.selectBtn);
    if(selectBtn){
      selectBtn.addEventListener('click', emitSelection);
    }
  }

  function handleKeydown(event){
    if(event.key === 'Escape'){
      close();
    }
  }

  function loadUsers(){
    const script = document.getElementById('shareBottleUserData');
    if(!script){
      return [];
    }

    try{
      const text = script.textContent || script.innerText || '[]';
      const parsed = JSON.parse(text);
      if(!Array.isArray(parsed)){
        return [];
      }

      return parsed
        .map(normalizeUser)
        .filter(user => user !== null);
    }catch(err){
      console.error('Failed to parse connected user data', err);
      return [];
    }
  }

  function normalizeUser(raw){
    if(!raw){
      return null;
    }

    const id = raw.id ?? raw.Id;
    if(!id){
      return null;
    }

    const displayName = toTrimmedString(raw.displayName ?? raw.DisplayName) || 'Member';
    const email = toTrimmedString(raw.email ?? raw.Email) || '';
    const sisterhoods = Array.isArray(raw.sisterhoods ?? raw.Sisterhoods)
      ? (raw.sisterhoods ?? raw.Sisterhoods).map(toTrimmedString).filter(Boolean)
      : [];

    return {
      id: String(id),
      displayName,
      email,
      sisterhoods
    };
  }

  function toTrimmedString(value){
    if(value === null || value === undefined){
      return '';
    }

    return String(value).trim();
  }

  function applyFilter(){
    const filter = qs(document, SELECTORS.filter);
    const query = toTrimmedString(filter?.value).toLowerCase();

    if(!query){
      state.filtered = state.users.slice();
    }else{
      state.filtered = state.users.filter(user => {
        const name = user.displayName.toLowerCase();
        const email = user.email.toLowerCase();
        const sisterhoodText = user.sisterhoods.join(' ').toLowerCase();
        return name.includes(query) || email.includes(query) || sisterhoodText.includes(query);
      });
    }

    renderRows();
    updateSelectButton();
  }

  function renderRows(){
    const tbody = qs(document, SELECTORS.tbody);
    if(!tbody){
      return;
    }

    if(state.users.length === 0){
      tbody.innerHTML = '<tr class="empty-row"><td colspan="4">No connected members available.</td></tr>';
      return;
    }

    if(state.filtered.length === 0){
      tbody.innerHTML = '<tr class="empty-row"><td colspan="4">No members match your filter.</td></tr>';
      return;
    }

    const rows = state.filtered.map(user => buildRowHtml(user)).join('');
    tbody.innerHTML = rows;

    qsa(tbody, 'input[type="checkbox"][data-user-id]').forEach(cb => {
      cb.addEventListener('change', onRowToggle);
    });
  }

  function buildRowHtml(user){
    const checked = state.selected.has(user.id) ? ' checked' : '';
    const email = user.email ? escapeHtml(user.email) : '—';
    const sisterhoods = user.sisterhoods.length > 0
      ? escapeHtml(user.sisterhoods.join(', '))
      : '—';

    return (
      '<tr>' +
        '<td class="choose-col-select"><input type="checkbox" data-user-id="' + escapeAttribute(user.id) + '" aria-label="Select ' + escapeAttribute(user.displayName) + '"' + checked + '></td>' +
        '<td class="choose-col-name">' + escapeHtml(user.displayName) + '</td>' +
        '<td class="choose-col-email">' + email + '</td>' +
        '<td class="choose-col-sisterhoods">' + sisterhoods + '</td>' +
      '</tr>'
    );
  }

  function escapeHtml(value){
    return String(value)
      .replaceAll('&', '&amp;')
      .replaceAll('<', '&lt;')
      .replaceAll('>', '&gt;')
      .replaceAll('"', '&quot;')
      .replaceAll("'", '&#39;');
  }

  function escapeAttribute(value){
    return escapeHtml(value).replaceAll('"', '&quot;');
  }

  function onRowToggle(event){
    const checkbox = event.currentTarget;
    const id = checkbox.getAttribute('data-user-id') || '';
    if(!id){
      return;
    }

    if(checkbox.checked){
      state.selected.add(id);
    }else{
      state.selected.delete(id);
    }

    updateSelectButton();
  }

  function updateSelectButton(){
    const selectBtn = qs(document, SELECTORS.selectBtn);
    if(selectBtn){
      selectBtn.disabled = state.selected.size === 0;
    }
  }

  function emitSelection(){
    const dialog = qs(document, SELECTORS.dialog);
    if(!dialog){
      return;
    }

    const userIds = Array.from(state.selected);
    const event = new CustomEvent('choose-recipients:selected', {
      detail: {
        userIds
      }
    });

    dialog.dispatchEvent(event);
    close();
  }

  window.ChooseRecipientsModal = {
    open
  };
})();
