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
