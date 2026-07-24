// تسجيل Service Worker لتفعيل تثبيت التطبيق (PWA)
(function () {
  "use strict";

  if (!("serviceWorker" in navigator)) {
    return;
  }

  window.addEventListener("load", function () {
    navigator.serviceWorker
      .register("/service-worker.js", { scope: "/" })
      .catch(function (err) {
        console.warn("[PWA] فشل تسجيل Service Worker:", err);
      });
  });
})();
