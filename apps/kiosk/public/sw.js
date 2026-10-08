// Kiosk service worker (docs §12): a brief network drop must not blank the screen.
//
//   app shell (index.html)   network first, cached copy when offline
//   /assets/* (hashed)       cache first: Vite gives every build new file names
//   GET …/api/kiosk/menu     network first, cached copy when offline (stock may be stale; the server re-checks
//                            everything when the order is placed)
//   images                   cache first: menu media URLs are content-hashed, so they never change
//
// Everything else — orders, payments, the realtime hub — goes straight to the network and is never cached.
// Bump VERSION to drop every cache on the next load.
const VERSION = "v1";
const SHELL = `kiosk-shell-${VERSION}`;
const MENU = `kiosk-menu-${VERSION}`;
const IMAGES = `kiosk-images-${VERSION}`;
const MAX_IMAGES = 300;

self.addEventListener("install", (event) => {
  event.waitUntil(caches.open(SHELL).then((cache) => cache.add("/")).then(() => self.skipWaiting()));
});

self.addEventListener("activate", (event) => {
  const keep = new Set([SHELL, MENU, IMAGES]);
  event.waitUntil(
    caches
      .keys()
      .then((names) => Promise.all(names.filter((n) => n.startsWith("kiosk-") && !keep.has(n)).map((n) => caches.delete(n))))
      .then(() => self.clients.claim()),
  );
});

self.addEventListener("fetch", (event) => {
  const { request } = event;
  if (request.method !== "GET") return;
  const url = new URL(request.url);

  if (request.mode === "navigate") {
    event.respondWith(networkFirst(request, SHELL, "/"));
  } else if (url.origin === self.location.origin && url.pathname.startsWith("/assets/")) {
    event.respondWith(cacheFirst(request, SHELL));
  } else if (url.pathname === "/api/kiosk/menu") {
    event.respondWith(networkFirst(request, MENU));
  } else if (request.destination === "image") {
    event.respondWith(cacheFirst(request, IMAGES, MAX_IMAGES));
  }
});

async function networkFirst(request, cacheName, fallbackKey) {
  const cache = await caches.open(cacheName);
  try {
    const response = await fetch(request);
    if (response.ok) await cache.put(fallbackKey ?? request, response.clone());
    return response;
  } catch (error) {
    // Cached by URL only: this device has a single kiosk token, so there is nobody else's menu to mix up.
    const cached = await cache.match(fallbackKey ?? request, { ignoreVary: true });
    if (cached) return cached;
    throw error;
  }
}

async function cacheFirst(request, cacheName, maxEntries) {
  const cache = await caches.open(cacheName);
  const cached = await cache.match(request);
  if (cached) return cached;

  const response = await fetch(request);
  // Opaque (no-CORS) image responses have status 0 but still display; keep them too.
  if (response.ok || response.type === "opaque") {
    await cache.put(request, response.clone());
    if (maxEntries) void trim(cache, maxEntries);
  }
  return response;
}

async function trim(cache, maxEntries) {
  const keys = await cache.keys();
  for (const key of keys.slice(0, Math.max(0, keys.length - maxEntries))) await cache.delete(key);
}
