// ZYNORA HR — Service Worker
// الهدف: تفعيل تثبيت التطبيق (PWA) مع سلوك شبكي آمن.
// لا نُخزّن صفحات HTML الديناميكية (تحتوي جلسات/صلاحيات) لتفادي المحتوى القديم.
// نُخزّن فقط قشرة ثابتة صغيرة + صفحة offline احتياطية.

const CACHE_VERSION = "zynora-pwa-v1";
const OFFLINE_URL = "/offline.html";
const PRECACHE = [OFFLINE_URL, "/brand/zynora-symbol-1024.png", "/manifest.webmanifest"];

self.addEventListener("install", (event) => {
  event.waitUntil(
    caches.open(CACHE_VERSION).then((cache) => cache.addAll(PRECACHE))
  );
  self.skipWaiting();
});

self.addEventListener("activate", (event) => {
  event.waitUntil(
    caches
      .keys()
      .then((keys) =>
        Promise.all(keys.filter((k) => k !== CACHE_VERSION).map((k) => caches.delete(k)))
      )
      .then(() => self.clients.claim())
  );
});

self.addEventListener("fetch", (event) => {
  const request = event.request;

  // نتعامل فقط مع GET؛ الباقي يمرّ مباشرة للشبكة.
  if (request.method !== "GET") {
    return;
  }

  // طلبات التنقّل (فتح صفحة): شبكة أولاً، وعند الانقطاع نُظهر صفحة offline.
  if (request.mode === "navigate") {
    event.respondWith(
      fetch(request).catch(() => caches.match(OFFLINE_URL))
    );
    return;
  }

  // بقية الطلبات (أصول ثابتة): شبكة أولاً مع الرجوع للكاش عند التوفّر.
  event.respondWith(
    fetch(request).catch(() => caches.match(request))
  );
});
