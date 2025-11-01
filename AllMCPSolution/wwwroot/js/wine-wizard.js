(function(){
  'use strict';

  const state = {
    active: false,
    mounted: false,
    step: 'idle',
    selectedBottleIds: [],
    pendingCreations: [],
    recipientIds: [],
    resolve: null,
    reject: null,
    options: {},
    submitting: false,
    expectingInventoryReopen: false
  };

  function start(options = {}){
    if(state.active){
      return Promise.reject(new Error('Wine Wizard is already running.'));
    }

    mount();

    state.active = true;
    state.step = 'choose';
    state.selectedBottleIds = [];
    state.pendingCreations = [];
    state.recipientIds = [];
    state.resolve = null;
    state.reject = null;
    state.options = options || {};
    state.submitting = false;
    state.expectingInventoryReopen = false;

    return new Promise((resolve, reject) => {
      state.resolve = resolve;
      state.reject = reject;
      openChooseStep();
    });
  }

  function cancel(reason){
    if(!state.active){
      return;
    }
    const message = reason || 'Wine Wizard cancelled.';
    if(typeof state.options.onCancel === 'function'){
      try { state.options.onCancel(message); } catch (error) { console.error(error); }
    }
    document.dispatchEvent(new CustomEvent('wineWizard:cancelled', { detail: { reason: message } }));
    if(typeof state.reject === 'function'){
      state.reject(new Error(message));
    }
    resetState();
  }

  function resetState(){
    state.active = false;
    state.step = 'idle';
    state.selectedBottleIds = [];
    state.pendingCreations = [];
    state.recipientIds = [];
    state.resolve = null;
    state.reject = null;
    state.options = {};
    state.submitting = false;
    state.expectingInventoryReopen = false;
  }

  function mount(){
    if(state.mounted){
      return;
    }
    state.mounted = true;
    document.addEventListener('choose-bottles:selected', handleChooseSelected);
    document.addEventListener('choose-bottles:closed', handleChooseClosed);
    document.addEventListener('inventoryAddModal:wizardSelection', handleInventorySelection);
    document.addEventListener('inventoryAddModal:closed', handleInventoryClosed);
    document.addEventListener('sisterhood-user-select:submit', handleSisterhoodSubmit);
    document.addEventListener('sisterhood-user-select:closed', handleSisterhoodClosed);
  }

  function handleChooseSelected(event){
    if(!state.active || state.step !== 'choose'){
      return;
    }
    const ids = normalizeIdList(event?.detail?.ids);
    state.selectedBottleIds = ids;
    if(ids.length > 0){
      openUserSelectionStep();
    }
  }

  function handleChooseClosed(event){
    if(!state.active || state.step !== 'choose'){
      return;
    }
    const reason = event?.detail?.reason || 'dismissed';
    if(reason === 'selected'){
      return;
    }
    state.selectedBottleIds = [];
    openInventoryStep();
  }

  function openChooseStep(){
    if(!window.ChooseBottlesModal || typeof window.ChooseBottlesModal.open !== 'function'){
      cancel('Choose bottles modal is unavailable.');
      return;
    }
    window.ChooseBottlesModal.open();
  }

  function openInventoryStep(){
    state.step = 'inventory';
    state.expectingInventoryReopen = false;
    const api = window.InventoryAddModal;
    if(!api || typeof api.open !== 'function'){
      cancel('Inventory add modal is unavailable.');
      return;
    }
    if(typeof api.initialize === 'function'){
      try { api.initialize(); } catch (error) { console.error(error); }
    }
    Promise.resolve(api.open({ mode: 'wine-wizard' })).catch(error => {
      const message = error?.message || 'Unable to open inventory modal.';
      cancel(message);
    });
  }

  function handleInventorySelection(event){
    if(!state.active || state.step !== 'inventory'){
      return;
    }
    const detail = event?.detail || {};
    const wineId = normalizeGuid(detail.wineId);
    const vintage = normalizeVintage(detail.vintage);
    const quantity = normalizeQuantity(detail.quantity);
    const bottleLocationId = normalizeGuid(detail.bottleLocationId);

    if(!wineId || !vintage){
      return;
    }

    state.pendingCreations.push({ wineId, vintage, quantity, bottleLocationId });
    state.expectingInventoryReopen = true;
  }

  function handleInventoryClosed(event){
    if(!state.active || state.step !== 'inventory'){
      return;
    }
    const reason = event?.detail?.reason || 'dismissed';

    if(reason === 'wizard' && state.expectingInventoryReopen){
      state.expectingInventoryReopen = false;
      reOpenInventoryModal();
      return;
    }

    state.expectingInventoryReopen = false;

    if(reason === 'error'){
      cancel('Unable to add wine to inventory.');
      return;
    }

    if(hasBottlesToShare()){
      openUserSelectionStep();
    } else {
      cancel('No bottles selected or created.');
    }
  }

  function reOpenInventoryModal(){
    const api = window.InventoryAddModal;
    if(!api || typeof api.open !== 'function'){
      openUserSelectionStep();
      return;
    }
    Promise.resolve(api.open({ mode: 'wine-wizard' })).catch(() => {
      openUserSelectionStep();
    });
  }

  function openUserSelectionStep(){
    state.step = 'users';
    const api = window.SisterhoodUserSelectModal;
    if(!api || typeof api.open !== 'function'){
      cancel('Sisterhood selection modal is unavailable.');
      return;
    }
    const preselected = normalizeIdList(state.options.preselectedRecipients);
    api.open({ preselectedIds: preselected.length > 0 ? preselected : undefined });
  }

  function handleSisterhoodSubmit(event){
    if(!state.active || state.step !== 'users'){
      return;
    }
    event.preventDefault();
    const detail = event?.detail || {};
    const ids = normalizeIdList(detail.selectedIds);
    if(ids.length === 0){
      return;
    }
    state.recipientIds = ids;
    submitWizard(detail.submitter || null);
  }

  function handleSisterhoodClosed(event){
    if(!state.active || state.step !== 'users'){
      return;
    }
    const reason = event?.detail?.reason || 'dismissed';
    if(reason === 'completed'){
      return;
    }
    if(state.submitting){
      return;
    }
    cancel('Wine share was cancelled.');
  }

  async function submitWizard(submitter){
    if(state.submitting){
      return;
    }
    if(!hasBottlesToShare()){
      notifyError('Select at least one bottle before sharing.');
      return;
    }
    if(state.recipientIds.length === 0){
      notifyError('Choose at least one fellow surfer to share with.');
      return;
    }

    state.submitting = true;
    if(submitter instanceof HTMLElement){
      submitter.disabled = true;
      submitter.setAttribute('aria-busy', 'true');
    }

    const payload = {
      existingBottleIds: state.selectedBottleIds,
      newBottleRequests: state.pendingCreations.map(entry => ({
        wineId: entry.wineId,
        vintage: entry.vintage,
        quantity: entry.quantity,
        bottleLocationId: entry.bottleLocationId
      })),
      recipientUserIds: state.recipientIds
    };

    try {
      const response = await sendJson('/wine-manager/wine-wizard/share', {
        method: 'POST',
        body: JSON.stringify(payload)
      });
      const api = window.SisterhoodUserSelectModal;
      if(api && typeof api.close === 'function'){
        api.close('completed');
      }
      complete(response);
    } catch (error) {
      notifyError(error?.message || 'Unable to share bottles right now.');
    } finally {
      state.submitting = false;
      if(submitter instanceof HTMLElement){
        submitter.disabled = false;
        submitter.removeAttribute('aria-busy');
      }
    }
  }

  function complete(result){
    const response = result || {};
    const message = resolveSuccessMessage(response);
    if(typeof state.options.onComplete === 'function'){
      try { state.options.onComplete(response); } catch (error) { console.error(error); }
    }
    document.dispatchEvent(new CustomEvent('wineWizard:completed', { detail: { response } }));
    if(typeof state.resolve === 'function'){
      state.resolve(response);
    }
    if(!state.options.silent && message){
      window.alert(message);
    }
    resetState();
  }

  function hasBottlesToShare(){
    if(state.selectedBottleIds.length > 0){
      return true;
    }
    return state.pendingCreations.some(entry => entry && entry.quantity > 0);
  }

  function resolveSuccessMessage(response){
    if(response && typeof response.message === 'string' && response.message.trim().length > 0){
      return response.message.trim();
    }
    const sharedIds = Array.isArray(response?.sharedBottleIds) ? response.sharedBottleIds : [];
    const recipientIds = Array.isArray(response?.recipientUserIds) ? response.recipientUserIds : state.recipientIds;
    const shareCount = sharedIds.length;
    const recipientCount = recipientIds.length;
    if(shareCount === 0 || recipientCount === 0){
      return 'Wine sharing completed.';
    }
    const bottleWord = shareCount === 1 ? 'bottle' : 'bottles';
    const recipientWord = recipientCount === 1 ? 'fellow surfer' : 'fellow surfers';
    return `Shared ${shareCount} ${bottleWord} with ${recipientCount} ${recipientWord}.`;
  }

  async function sendJson(url, options = {}){
    const init = {
      headers: {
        'Content-Type': 'application/json',
        'Accept': 'application/json'
      },
      credentials: 'same-origin',
      ...options
    };
    const response = await fetch(url, init);
    if(!response.ok){
      let message = `${response.status} ${response.statusText}`;
      try {
        const problem = await response.json();
        message = extractProblemMessage(problem) || message;
      } catch {
        const text = await response.text();
        if(text){
          message = text;
        }
      }
      throw new Error(message);
    }
    if(response.status === 204){
      return {};
    }
    return response.json();
  }

  function extractProblemMessage(problem){
    if(!problem){
      return '';
    }
    if(typeof problem === 'string'){
      return problem;
    }
    if(typeof problem.message === 'string'){
      return problem.message;
    }
    if(typeof problem.title === 'string'){
      return problem.title;
    }
    if(problem.errors){
      const values = Object.values(problem.errors);
      for(const value of values){
        if(Array.isArray(value) && value.length > 0){
          const first = value[0];
          if(typeof first === 'string' && first.trim().length > 0){
            return first;
          }
        }
      }
    }
    return '';
  }

  function normalizeIdList(source){
    if(!Array.isArray(source)){
      return [];
    }
    const seen = new Set();
    const result = [];
    for(const value of source){
      const guid = normalizeGuid(value);
      if(!guid){
        continue;
      }
      const key = guid.toLowerCase();
      if(seen.has(key)){
        continue;
      }
      seen.add(key);
      result.push(guid);
    }
    return result;
  }

  function normalizeGuid(value){
    if(!value){
      return '';
    }
    const text = (typeof value === 'string') ? value : String(value);
    const trimmed = text.trim();
    return trimmed;
  }

  function normalizeVintage(value){
    const parsed = Number.parseInt(value, 10);
    if(Number.isNaN(parsed)){
      return null;
    }
    return parsed;
  }

  function normalizeQuantity(value){
    const parsed = Number.parseInt(value, 10);
    if(Number.isNaN(parsed) || parsed < 1){
      return 1;
    }
    return Math.min(parsed, 12);
  }

  function notifyError(message){
    const text = message && typeof message === 'string' ? message : 'Something went wrong.';
    if(state.options.silent){
      console.error(text);
      return;
    }
    window.alert(text);
  }

  window.WineWizard = {
    start,
    cancel,
    isActive: () => state.active
  };
})();
