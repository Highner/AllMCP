const CACHE_NAME = 'wine-surfer-pwa-v2';
const OFFLINE_URL = '/offline.html';
const PRECACHE_ASSETS = [
  '/manifest.json',
  '/favicon.png',
  '/css/wine-surfer-theme.css',
  '/css/wine-surfer-shadcn.css',
  '/js/pwa.js',
  '/js/drink-bottle-modal.js',
  '/js/inventory-add-modal.js',
  '/js/wine-inventory-tables.js',
  '/assets/logo.png',
  '/assets/icons/icon.svg',
  OFFLINE_URL
];

const STATIC_ASSET_EXTENSIONS = new Set([
  'css',
  'js',
  'png',
  'jpg',
  'jpeg',
  'svg',
  'webp',
  'ico',
  'woff',
  'woff2',
  'ttf',
  'eot'
]);

const JSON_API_PREFIXES = ['/wine-surfer', '/wine-manager'];

self.addEventListener('install', (event) => {
  event.waitUntil(
    caches
      .open(CACHE_NAME)
      .then((cache) => cache.addAll(PRECACHE_ASSETS))
      .catch((error) => {
        console.error('Failed to pre-cache assets', error);
      })
  );
  self.skipWaiting();
});

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches
      .keys()
      .then((keys) =>
        Promise.all(keys.filter((key) => key !== CACHE_NAME).map((key) => caches.delete(key)))
      )
  );
  self.clients.claim();
});

self.addEventListener('fetch', (event) => {
  if (event.request.method !== 'GET') {
    return;
  }

  const requestUrl = new URL(event.request.url);
  if (requestUrl.origin !== self.location.origin) {
    return;
  }

  if (event.request.mode === 'navigate') {
    if (shouldBypassNavigationHandling(requestUrl)) {
      return;
    }

    event.respondWith(handleNavigationRequest(event.request));
    return;
  }

  if (isJsonApiRequest(event.request)) {
    event.respondWith(handleJsonApiRequest(event.request));
    return;
  }

  if (isStaticAssetRequest(requestUrl)) {
    event.respondWith(handleStaticAssetRequest(event.request));
    return;
  }

  event.respondWith(handleDefaultRequest(event.request));
});

async function handleNavigationRequest(request) {
  try {
    const networkResponse = await fetch(request);
    return networkResponse;
  } catch (error) {
    const cachedResponse = await caches.match(request);
    if (cachedResponse) {
      return cachedResponse;
    }

    const offlinePage = await caches.match(OFFLINE_URL);
    if (offlinePage) {
      return offlinePage;
    }

    return buildOfflineResponse('text/html');
  }
}

async function handleJsonApiRequest(request) {
  try {
    const networkResponse = await fetch(request);

    if (shouldCacheApiResponse(request, networkResponse)) {
      const cache = await caches.open(CACHE_NAME);
      await cache.put(request, networkResponse.clone());
    }

    return networkResponse;
  } catch (error) {
    const cachedResponse = await caches.match(request);
    if (cachedResponse) {
      return cachedResponse;
    }

    return buildOfflineResponse('application/json');
  }
}

async function handleStaticAssetRequest(request) {
  try {
    const networkResponse = await fetch(request);
    if (shouldCacheStaticAssetResponse(networkResponse)) {
      const cache = await caches.open(CACHE_NAME);
      await cache.put(request, networkResponse.clone());
    }
    return networkResponse;
  } catch (error) {
    const cachedResponse = await caches.match(request);
    if (cachedResponse) {
      return cachedResponse;
    }
    return buildOfflineResponse('text/plain');
  }
}

async function handleDefaultRequest(request) {
  try {
    return await fetch(request);
  } catch (error) {
    const cachedResponse = await caches.match(request);
    if (cachedResponse) {
      return cachedResponse;
    }
    return buildOfflineResponse('text/plain');
  }
}

function isStaticAssetRequest(url) {
  if (PRECACHE_ASSETS.includes(url.pathname)) {
    return true;
  }

  const segments = url.pathname.split('/');
  const lastSegment = segments[segments.length - 1];
  if (!lastSegment || !lastSegment.includes('.')) {
    return false;
  }

  const extension = lastSegment.split('.').pop();
  if (!extension) {
    return false;
  }

  return STATIC_ASSET_EXTENSIONS.has(extension.toLowerCase());
}

function isJsonApiRequest(request) {
  const url = new URL(request.url);
  if (!JSON_API_PREFIXES.some((prefix) => url.pathname.startsWith(prefix))) {
    return false;
  }

  const acceptHeader = request.headers.get('accept') || '';
  const isJsonAccept = acceptHeader.split(',').some((value) => value.trim().startsWith('application/json'));

  return isJsonAccept || url.pathname.endsWith('.json');
}

function shouldCacheStaticAssetResponse(response) {
  return isCacheableResponse(response);
}

function shouldCacheApiResponse(request, response) {
  if (!isCacheableResponse(response)) {
    return false;
  }

  const url = new URL(request.url);
  return !isUserSpecificRequest(url, request);
}

function isCacheableResponse(response) {
  if (!response || response.status !== 200) {
    return false;
  }

  if (response.type !== 'basic' && response.type !== 'cors') {
    return false;
  }

  const cacheControl = response.headers.get('cache-control');
  if (cacheControl && /no-store|no-cache|private/i.test(cacheControl)) {
    return false;
  }

  return true;
}

function isUserSpecificRequest(url, request) {
  if ((request.headers.get('authorization') || '').trim() !== '') {
    return true;
  }

  const lowerPath = url.pathname.toLowerCase();
  const userSpecificKeywords = [
    'inventory',
    'tasting',
    'favorites',
    'profile',
    'sip-session',
    'sip',
    'session',
    'notifications',
    'sisterhood',
    'bottle',
    'notes'
  ];

  return userSpecificKeywords.some((keyword) => lowerPath.includes(keyword));
}

function shouldBypassNavigationHandling(url) {
  const protectedPrefixes = ['/wine-manager', '/sip-session', '/wine-surfer/sessions'];

  return protectedPrefixes.some((prefix) =>
    url.pathname === prefix || url.pathname.startsWith(`${prefix}/`)
  );
}

function buildOfflineResponse(contentType) {
  const body = contentType === 'application/json'
    ? JSON.stringify({ error: 'offline', message: 'Content unavailable while offline.' })
    : 'Offline';

  return new Response(body, {
    status: 503,
    statusText: 'Service Unavailable',
    headers: {
      'Content-Type': contentType
    }
  });
}
