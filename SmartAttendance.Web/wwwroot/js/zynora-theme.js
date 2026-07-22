(function () {
    "use strict";

    // الثيم الداكن حصراً — أُلغي الوضع الفاتح نهائياً (قرار 2026-07).
    // أي محاولة تبديل (زر قديم، localStorage، تبويب آخر، API) تُثبَّت على dark.

    var storageKey = "ZYNORA.Theme";

    function forceDark() {
        document.documentElement.setAttribute("data-theme", "dark");
        try {
            localStorage.removeItem(storageKey);
        } catch (_) { }
    }

    forceDark();

    window.addEventListener("storage", function (event) {
        if (event.key === storageKey) forceDark();
    });

    // واجهة متوافقة مع الكود القديم — تتجاهل أي قيمة وتبقي الداكن
    window.ZynoraTheme = {
        get: function () { return "dark"; },
        set: forceDark
    };
})();
