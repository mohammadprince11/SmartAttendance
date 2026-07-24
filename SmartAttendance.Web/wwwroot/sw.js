/* بوابة الموظف الذكية — Service Worker
 * استراتيجية واعية بالخصوصية:
 *  - صفحات HTML (تحمل بيانات شخصية): شبكة-أولاً، والبديل صفحة عدم الاتصال — لا تُخزَّن.
 *  - نداءات /api/ (بيانات شخصية): شبكة فقط — لا تُخزَّن أبداً.
 *  - الأصول الثابتة (css/js/lib/brand/الأيقونات): stale-while-revalidate.
 */
const VERSION = 'v7';
const STATIC_CACHE = `sa-static-${VERSION}`;
const OFFLINE_URL = '/offline.html';

const PRECACHE = [
  OFFLINE_URL,
  '/manifest.webmanifest',
  '/brand/pwa/icon-192.png',
  '/brand/pwa/icon-512.png'
];

self.addEventListener('install', (event) => {
  event.waitUntil(
    caches.open(STATIC_CACHE)
      .then((cache) => cache.addAll(PRECACHE))
      .then(() => self.skipWaiting())
  );
});

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches.keys()
      .then((keys) => Promise.all(
        keys.filter((k) => k.startsWith('sa-static-') && k !== STATIC_CACHE)
            .map((k) => caches.delete(k))
      ))
      .then(() => self.clients.claim())
  );
});

function isStaticAsset(url) {
  return /^\/(css|js|lib|brand|images)\//.test(url.pathname) ||
         url.pathname === '/manifest.webmanifest' ||
         url.pathname === '/favicon.ico';
}

self.addEventListener('fetch', (event) => {
  const req = event.request;
  if (req.method !== 'GET') return;

  const url = new URL(req.url);
  if (url.origin !== self.location.origin) return;

  // بيانات شخصية عبر الـAPI: شبكة فقط، لا تخزين.
  if (url.pathname.startsWith('/api/')) return;

  // تنقّل بين الصفحات: شبكة-أولاً مع بديل عدم الاتصال (بلا تخزين لبيانات شخصية).
  if (req.mode === 'navigate') {
    event.respondWith(
      fetch(req).catch(() => caches.match(OFFLINE_URL))
    );
    return;
  }

  // أصول ثابتة: الشبكة أولاً (أونلاين = دائماً أحدث نسخة)، والكاش بديلٌ عند انقطاع الشبكة.
  if (isStaticAsset(url)) {
    event.respondWith(
      caches.open(STATIC_CACHE).then((cache) =>
        fetch(req).then((res) => {
          if (res && res.status === 200) cache.put(req, res.clone());
          return res;
        }).catch(() => cache.match(req))
      )
    );
  }
});

// تحديث فوري عند طلب الصفحة.
self.addEventListener('message', (event) => {
  if (event.data === 'SKIP_WAITING') self.skipWaiting();
});
