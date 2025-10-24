(() => {
  if (!('serviceWorker' in navigator)) {
    return;
  }

  const registerServiceWorker = async () => {
    try {
      const registration = await navigator.serviceWorker.register('/service-worker.js');
      console.info('Wine Surfer service worker registered', registration.scope);
    } catch (error) {
      console.warn('Wine Surfer service worker registration failed', error);
    }
  };

  if (document.readyState === 'complete') {
    registerServiceWorker();
  } else {
    window.addEventListener('load', registerServiceWorker);
  }
})();
