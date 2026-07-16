(function () {
    "use strict";

    var storageKey = "ZYNORA.Theme";

    function normalizeTheme(value) {
        return value === "dark" ? "dark" : "light";
    }

    function currentTheme() {
        return normalizeTheme(
            document.documentElement.getAttribute("data-theme")
        );
    }

    function updateToggle(theme) {
        var button = document.querySelector(
            "[data-zynora-theme-toggle]"
        );

        if (!button) return;

        var isDark = theme === "dark";
        var label = isDark
            ? "تفعيل الوضع الفاتح"
            : "تفعيل الوضع الداكن";

        button.setAttribute("aria-label", label);
        button.setAttribute("title", label);
        button.setAttribute(
            "aria-pressed",
            isDark ? "true" : "false"
        );
    }

    function applyTheme(value, persist) {
        var theme = normalizeTheme(value);

        document.documentElement.setAttribute(
            "data-theme",
            theme
        );

        if (persist) {
            try {
                localStorage.setItem(storageKey, theme);
            } catch (_) { }
        }

        updateToggle(theme);

        window.dispatchEvent(
            new CustomEvent("zynora:themechange", {
                detail: { theme: theme }
            })
        );
    }

    function init() {
        applyTheme(currentTheme(), false);

        var button = document.querySelector(
            "[data-zynora-theme-toggle]"
        );

        if (button) {
            button.addEventListener("click", function () {
                applyTheme(
                    currentTheme() === "dark"
                        ? "light"
                        : "dark",
                    true
                );
            });
        }
    }

    window.addEventListener("storage", function (event) {
        if (event.key !== storageKey) return;

        applyTheme(event.newValue, false);
    });

    if (document.readyState === "loading") {
        document.addEventListener(
            "DOMContentLoaded",
            init
        );
    } else {
        init();
    }

    window.ZynoraTheme = {
        get: currentTheme,
        set: function (theme) {
            applyTheme(theme, true);
        }
    };
})();
