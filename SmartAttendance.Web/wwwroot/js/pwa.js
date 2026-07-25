/* تسجيل الـService Worker + إدارة زر التثبيت المخصّص لبوابة الموظف. */
(function () {
  'use strict';

  // 1) تسجيل الـService Worker + تحديث تلقائي فوري (يمنع بقاء نسخة قديمة على التلفون).
  if ('serviceWorker' in navigator) {
    var refreshing = false;
    // عند تفعيل SW جديد يتحكّم بالصفحة، أعد التحميل مرة واحدة لتطبيق آخر نسخة.
    navigator.serviceWorker.addEventListener('controllerchange', function () {
      if (refreshing) return;
      refreshing = true;
      window.location.reload();
    });

    window.addEventListener('load', function () {
      navigator.serviceWorker.register('/sw.js').then(function (reg) {
        // افحص وجود تحديث فوراً وكل مرة تُفتح فيها الصفحة.
        reg.update();
        function promote(worker) {
          if (!worker) return;
          worker.addEventListener('statechange', function () {
            // نسخة جديدة جاهزة وهناك نسخة قديمة تتحكّم ⟶ فعّلها فوراً.
            if (worker.state === 'installed' && navigator.serviceWorker.controller) {
              worker.postMessage('SKIP_WAITING');
            }
          });
        }
        if (reg.waiting && navigator.serviceWorker.controller) {
          reg.waiting.postMessage('SKIP_WAITING');
        }
        promote(reg.installing);
        reg.addEventListener('updatefound', function () { promote(reg.installing); });
      }).catch(function (err) {
        console.warn('[PWA] تعذّر تسجيل الـService Worker:', err);
      });
    });
  }

  // 1ب) اشتراك Web-Push: بعد جاهزية الـSW، إن كانت القناة مفعّلة والإذن ممنوح
  // (أو يُطلب عند نقر زر التفعيل) نشترك ونرسل الاشتراك للخادم. آمن حين معطّلة.
  function urlBase64ToUint8Array(base64String) {
    var padding = '='.repeat((4 - (base64String.length % 4)) % 4);
    var base64 = (base64String + padding).replace(/-/g, '+').replace(/_/g, '/');
    var raw = window.atob(base64);
    var out = new Uint8Array(raw.length);
    for (var i = 0; i < raw.length; i++) out[i] = raw.charCodeAt(i);
    return out;
  }

  async function subscribePush(reg) {
    try {
      if (!('PushManager' in window)) return;
      var res = await fetch('/push/vapid-key', { credentials: 'same-origin' });
      if (!res.ok) return;
      var cfg = await res.json();
      if (!cfg.enabled || !cfg.publicKey) return; // القناة معطّلة

      if (Notification.permission === 'denied') return;
      if (Notification.permission !== 'granted') {
        var perm = await Notification.requestPermission();
        if (perm !== 'granted') return;
      }

      var sub = await reg.pushManager.getSubscription();
      if (!sub) {
        sub = await reg.pushManager.subscribe({
          userVisibleOnly: true,
          applicationServerKey: urlBase64ToUint8Array(cfg.publicKey)
        });
      }
      var raw = sub.toJSON();
      await fetch('/push/subscribe', {
        method: 'POST',
        credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          endpoint: sub.endpoint,
          p256dh: raw.keys && raw.keys.p256dh,
          auth: raw.keys && raw.keys.auth
        })
      });
    } catch (err) {
      console.warn('[Push] تعذّر الاشتراك:', err);
    }
  }
  window.__saSubscribePush = function () {
    if ('serviceWorker' in navigator) navigator.serviceWorker.ready.then(subscribePush);
  };
  if ('serviceWorker' in navigator && Notification && Notification.permission === 'granted') {
    navigator.serviceWorker.ready.then(subscribePush);
  }

  // 2) زر تثبيت مخصّص (أندرويد/كروم/إيدج يدعمون beforeinstallprompt).
  var deferredPrompt = null;
  var installBtn = document.getElementById('pwa-install-btn');
  var iosHint = document.getElementById('pwa-ios-hint');

  function isStandalone() {
    return window.matchMedia('(display-mode: standalone)').matches ||
           window.navigator.standalone === true;
  }

  function isIos() {
    return /iphone|ipad|ipod/i.test(window.navigator.userAgent) &&
           !window.MSStream;
  }

  // إذا كان مثبَّتاً/يعمل كتطبيق، لا نعرض شيئاً.
  if (isStandalone()) {
    if (installBtn) installBtn.hidden = true;
    if (iosHint) iosHint.hidden = true;
    return;
  }

  window.addEventListener('beforeinstallprompt', function (e) {
    e.preventDefault();
    deferredPrompt = e;
    if (installBtn) installBtn.hidden = false;
  });

  if (installBtn) {
    installBtn.addEventListener('click', function () {
      if (!deferredPrompt) return;
      deferredPrompt.prompt();
      deferredPrompt.userChoice.then(function () {
        deferredPrompt = null;
        installBtn.hidden = true;
      });
    });
  }

  window.addEventListener('appinstalled', function () {
    if (installBtn) installBtn.hidden = true;
    if (iosHint) iosHint.hidden = true;
    deferredPrompt = null;
  });

  // iOS لا يدعم beforeinstallprompt — نعرض تلميح «أضف إلى الشاشة الرئيسية».
  if (isIos() && iosHint) {
    iosHint.hidden = false;
  }
})();
