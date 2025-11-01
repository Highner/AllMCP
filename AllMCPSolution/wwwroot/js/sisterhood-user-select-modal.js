(function(){
  const SELECTORS = {
    overlay: '[data-sisterhood-user-select-overlay]',
    dialog: '[data-sisterhood-user-select-dialog]',
    form: '[data-sisterhood-user-select-form]',
    filter: '[data-sisterhood-user-select-filter]',
    list: '[data-sisterhood-user-select-list]',
    option: '[data-sisterhood-user-select-option]',
    item: '[data-sisterhood-user-select-item]',
    status: '[data-sisterhood-user-select-status]',
    emptyMessage: '[data-sisterhood-user-select-empty]',
    emptyState: '[data-sisterhood-user-select-empty-state]',
    submit: '[data-sisterhood-user-select-submit]',
    close: '[data-sisterhood-user-select-close]',
    cancel: '[data-sisterhood-user-select-cancel]',
    trigger: '[data-sisterhood-user-select-open]'
  };

  const state = {
    overlay: null,
    dialog: null,
    form: null,
    filter: null,
    list: null,
    status: null,
    emptyMessage: null,
    emptyState: null,
    submit: null,
    cancelButtons: [],
    checkboxes: [],
    items: [],
    mounted: false
  };

  function queryElements(){
    state.overlay = document.querySelector(SELECTORS.overlay);
    if(!state.overlay){ return false; }
    state.dialog = document.querySelector(SELECTORS.dialog);
    if(!state.dialog){ return false; }
    state.form = state.dialog.querySelector(SELECTORS.form);
    if(!state.form){ return false; }
    state.filter = state.form.querySelector(SELECTORS.filter);
    state.list = state.form.querySelector(SELECTORS.list);
    state.status = state.form.querySelector(SELECTORS.status);
    state.emptyMessage = state.form.querySelector(SELECTORS.emptyMessage);
    state.emptyState = state.form.querySelector(SELECTORS.emptyState);
    state.submit = state.form.querySelector(SELECTORS.submit);
    state.cancelButtons = Array.from(state.dialog.querySelectorAll(`${SELECTORS.close}, ${SELECTORS.cancel}`));
    refreshCollections();
    return true;
  }

  function refreshCollections(){
    if(!state.form){ state.checkboxes = []; state.items = []; return; }
    state.checkboxes = Array.from(state.form.querySelectorAll(SELECTORS.option));
    state.items = state.checkboxes
      .map(cb => cb.closest(SELECTORS.item))
      .filter(Boolean);
  }

  function mount(){
    if(state.mounted){ return; }
    if(!queryElements()){ return; }

    state.cancelButtons.forEach(btn => {
      btn.addEventListener('click', onCancelClick);
    });

    if(state.filter){
      state.filter.addEventListener('input', onFilterInput);
    }

    state.checkboxes.forEach(cb => {
      cb.addEventListener('change', onCheckboxChange);
    });

    state.form.addEventListener('submit', onFormSubmit);

    state.mounted = true;
    applyFilter();
    updateSelectionState();
  }

  function open(options){
    mount();
    if(!state.overlay || !state.dialog){ return; }
    refreshCollections();

    const preselected = options && Array.isArray(options.preselectedIds)
      ? options.preselectedIds
      : null;
    if(preselected){
      setSelections(preselected);
    }else{
      updateSelectionState();
    }

    applyFilter();

    state.overlay.removeAttribute('hidden');
    state.overlay.setAttribute('aria-hidden', 'false');
    state.overlay.classList.add('open');

    const focusTarget = resolveInitialFocusTarget();
    if(focusTarget){
      focusTarget.focus();
    }

    document.addEventListener('keydown', onKeydown);
  }

  function close(reason = 'dismissed'){
    if(!state.overlay){ return; }
    state.overlay.setAttribute('aria-hidden', 'true');
    state.overlay.setAttribute('hidden', '');
    state.overlay.classList.remove('open');
    document.removeEventListener('keydown', onKeydown);
    document.dispatchEvent(new CustomEvent('sisterhood-user-select:closed', { detail: { reason } }));
  }

  function resolveInitialFocusTarget(){
    if(state.filter && !state.filter.disabled){
      return state.filter;
    }
    if(state.checkboxes.length > 0){
      return state.checkboxes[0];
    }
    if(state.submit){
      return state.submit;
    }
    return null;
  }

  function onCancelClick(event){
    event.preventDefault();
    close('cancel');
  }

  function onKeydown(event){
    if(event.key === 'Escape'){
      close('escape');
    }
  }

  function onFilterInput(){
    applyFilter();
  }

  function applyFilter(){
    if(!state.items || state.items.length === 0){
      if(state.emptyMessage){ state.emptyMessage.setAttribute('hidden', ''); }
      if(state.emptyState){ state.emptyState.removeAttribute('hidden'); }
      return;
    }

    const query = !state.filter || state.filter.disabled
      ? ''
      : (state.filter.value || '').trim().toLowerCase();

    let visibleCount = 0;
    state.items.forEach(item => {
      if(!item){ return; }
      const filterText = (item.getAttribute('data-filter-text') || '').toLowerCase();
      const matches = !query || filterText.includes(query);
      if(matches){
        item.removeAttribute('hidden');
        visibleCount += 1;
      }else{
        item.setAttribute('hidden', '');
      }
    });

    if(state.emptyMessage){
      if(visibleCount === 0){
        state.emptyMessage.removeAttribute('hidden');
      }else{
        state.emptyMessage.setAttribute('hidden', '');
      }
    }

    if(state.emptyState){
      if(state.items.length === 0){
        state.emptyState.removeAttribute('hidden');
      }else{
        state.emptyState.setAttribute('hidden', '');
      }
    }
  }

  function onCheckboxChange(){
    updateSelectionState();
  }

  function updateSelectionState(){
    const selectedIds = getSelectedIds();
    if(state.submit){
      if(state.items.length === 0 || selectedIds.length === 0){
        state.submit.setAttribute('disabled', '');
      }else{
        state.submit.removeAttribute('disabled');
      }
    }
    updateStatus(selectedIds.length);
  }

  function updateStatus(count){
    if(!state.status){ return; }
    const emptyMessage = state.status.getAttribute('data-empty-message') || 'No fellow surfers selected.';
    const singularFormat = state.status.getAttribute('data-singular-format') || '{0} fellow surfer selected.';
    const pluralFormat = state.status.getAttribute('data-plural-format') || '{0} fellow surfers selected.';

    let message;
    if(state.items.length === 0){
      if(state.emptyState && state.emptyState.textContent){
        message = state.emptyState.textContent.trim();
      }else{
        message = emptyMessage;
      }
    }else if(count === 0){
      message = emptyMessage;
    }else if(count === 1){
      message = singularFormat.replace('{0}', '1');
    }else{
      message = pluralFormat.replace('{0}', String(count));
    }

    state.status.textContent = message;
  }

  function getSelectedIds(){
    if(!state.checkboxes || state.checkboxes.length === 0){
      return [];
    }
    return state.checkboxes
      .filter(cb => cb.checked)
      .map(cb => cb.value)
      .filter(Boolean);
  }

  function setSelections(ids){
    refreshCollections();
    if(!Array.isArray(ids) || ids.length === 0){
      state.checkboxes.forEach(cb => { cb.checked = false; });
      updateSelectionState();
      return;
    }

    const normalized = new Set(ids.map(id => String(id)));
    state.checkboxes.forEach(cb => {
      cb.checked = normalized.has(cb.value);
    });
    updateSelectionState();
  }

  function onFormSubmit(event){
    const selectedIds = getSelectedIds();
    const submitEvent = new CustomEvent('sisterhood-user-select:submit', {
      detail: {
        selectedIds,
        submitter: event.submitter || null
      },
      cancelable: true
    });
    const proceed = state.form.dispatchEvent(submitEvent);
    if(!proceed || state.items.length === 0){
      event.preventDefault();
    }
  }

  document.addEventListener('click', event => {
    const trigger = event.target.closest(SELECTORS.trigger);
    if(!trigger){ return; }
    event.preventDefault();
    const raw = trigger.getAttribute('data-sisterhood-user-select-value');
    const preselected = raw
      ? raw.split(',').map(part => part.trim()).filter(Boolean)
      : null;
    open({ preselectedIds: preselected || undefined });
  });

  mount();

  window.SisterhoodUserSelectModal = {
    open,
    close: reason => close(reason),
    setSelections
  };
})();
