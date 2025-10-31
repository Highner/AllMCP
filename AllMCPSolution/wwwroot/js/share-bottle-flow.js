(function(){
  'use strict';

  const SHARE_BUTTON_SELECTOR = '[data-share-bottle-trigger]';
  const BOTTLE_DIALOG_SELECTOR = '#choose-bottles-modal';
  const RECIPIENT_DIALOG_SELECTOR = '#choose-recipients-modal';

  const state = {
    bottleIds: []
  };

  let shareButton = null;

  function init(){
    shareButton = document.querySelector(SHARE_BUTTON_SELECTOR);
    if(shareButton){
      shareButton.addEventListener('click', onShareButtonClick);
    }

    const bottleDialog = document.querySelector(BOTTLE_DIALOG_SELECTOR);
    if(bottleDialog){
      bottleDialog.addEventListener('choose-bottles:selected', onBottlesSelected);
    }

    const recipientDialog = document.querySelector(RECIPIENT_DIALOG_SELECTOR);
    if(recipientDialog){
      recipientDialog.addEventListener('choose-recipients:selected', onRecipientsSelected);
    }
  }

  function onShareButtonClick(){
    if(typeof window.ChooseBottlesModal?.open === 'function'){
      window.ChooseBottlesModal.open();
    }
  }

  function onBottlesSelected(event){
    const ids = Array.isArray(event?.detail?.ids)
      ? event.detail.ids.filter(Boolean).map(String)
      : [];

    state.bottleIds = ids;

    if(ids.length === 0){
      return;
    }

    if(typeof window.ChooseRecipientsModal?.open === 'function'){
      window.ChooseRecipientsModal.open();
    }
  }

  function onRecipientsSelected(event){
    const userIds = Array.isArray(event?.detail?.userIds)
      ? event.detail.userIds.filter(Boolean).map(String)
      : [];

    const detail = {
      bottleIds: state.bottleIds.slice(),
      userIds
    };

    const target = shareButton || document;
    const shareEvent = new CustomEvent('bottle-share:selected', { detail });
    target.dispatchEvent(shareEvent);

    state.bottleIds = [];
  }

  if(document.readyState === 'loading'){
    document.addEventListener('DOMContentLoaded', init);
  }else{
    init();
  }
})();
