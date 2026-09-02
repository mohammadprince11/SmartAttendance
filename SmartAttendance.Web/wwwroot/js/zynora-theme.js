(function () {
    "use strict";

    // Light Mode متوقف مؤقتاً حتى تكتمل إعادة تصميمه. نبقي الواجهة العامة
    // للتوافق مع أي كود قديم، لكن كل العمليات تعيد Dark ولا تسمح بتغييره.

    var storageKey = "ZY.Theme";

    function current() {
        return "dark";
    }

    function enforceDark() {
        document.documentElement.setAttribute("data-theme", "dark");
        try { localStorage.setItem(storageKey, "dark"); } catch (_) { }
        return "dark";
    }

    window.addEventListener("storage", function (event) {
        if (event.key === storageKey && event.newValue !== "dark") {
            enforceDark();
        }
    });

    enforceDark();

    window.ZynoraTheme = {
        get: current,
        set: enforceDark,
        toggle: enforceDark
    };
})();
