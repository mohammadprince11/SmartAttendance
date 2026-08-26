(function () {
    "use strict";

    var languageStorageKey = "ZY.Language";

    function currentLanguage() {
        try {
            return localStorage.getItem(languageStorageKey) === "en" ? "en" : "ar";
        } catch (_) {
            return "ar";
        }
    }

    function applyLanguage(language) {
        var next = language === "en" ? "en" : "ar";
        var root = document.documentElement;
        root.lang = next;
        root.dir = next === "ar" ? "rtl" : "ltr";
        document.body.dir = root.dir;

        document.querySelectorAll("[data-zy-menu-label]").forEach(function (label) {
            var translated = label.getAttribute(next === "ar" ? "data-ar" : "data-en");
            if (translated) label.textContent = translated;
        });

        document.querySelectorAll("[data-zy-language-code]").forEach(function (code) {
            code.textContent = next.toUpperCase();
        });

        document.querySelectorAll("[data-zy-language-option]").forEach(function (option) {
            option.setAttribute("aria-checked", String(option.getAttribute("data-zy-language-option") === next));
        });

        try { localStorage.setItem(languageStorageKey, next); } catch (_) { }
    }

    function setupMenu(root) {
        var trigger = root.querySelector("[data-zy-user-menu-trigger]");
        var panel = root.querySelector("[data-zy-user-menu-panel]");
        if (!trigger || !panel) return;

        function enabledItems() {
            return Array.prototype.slice.call(panel.querySelectorAll("[role='menuitem']"))
                .filter(function (item) { return item.getAttribute("aria-disabled") !== "true"; });
        }

        function open(focusFirst) {
            panel.hidden = false;
            trigger.setAttribute("aria-expanded", "true");
            if (focusFirst) {
                var first = enabledItems()[0];
                if (first) first.focus();
            }
        }

        function close(returnFocus) {
            panel.hidden = true;
            trigger.setAttribute("aria-expanded", "false");
            if (returnFocus) trigger.focus();
        }

        trigger.addEventListener("click", function () {
            if (panel.hidden) open(false);
            else close(false);
        });

        trigger.addEventListener("keydown", function (event) {
            if (event.key === "ArrowDown") {
                event.preventDefault();
                open(true);
            }
        });

        panel.addEventListener("keydown", function (event) {
            var items = enabledItems();
            var index = items.indexOf(document.activeElement);

            if (event.key === "Escape") {
                event.preventDefault();
                close(true);
                return;
            }

            if (!items.length) return;

            if (event.key === "ArrowDown" || event.key === "ArrowUp") {
                event.preventDefault();
                var delta = event.key === "ArrowDown" ? 1 : -1;
                var next = index < 0 ? 0 : (index + delta + items.length) % items.length;
                items[next].focus();
            } else if (event.key === "Home") {
                event.preventDefault();
                items[0].focus();
            } else if (event.key === "End") {
                event.preventDefault();
                items[items.length - 1].focus();
            }
        });

        panel.addEventListener("click", function (event) {
            if (event.target.closest("a[role='menuitem']")) close(false);
        });

        document.addEventListener("pointerdown", function (event) {
            if (!panel.hidden && !root.contains(event.target)) close(false);
        });

        document.addEventListener("keydown", function (event) {
            if (event.key === "Escape" && !panel.hidden) close(true);
        });
    }

    function setupLanguageDialog() {
        var dialog = document.querySelector("[data-zy-language-dialog]");
        var openButton = document.querySelector("[data-zy-language-open]");
        if (!dialog || !openButton) return;

        openButton.addEventListener("click", function () {
            var menu = openButton.closest("[data-zy-user-menu]");
            var trigger = menu && menu.querySelector("[data-zy-user-menu-trigger]");
            var panel = menu && menu.querySelector("[data-zy-user-menu-panel]");
            if (panel) panel.hidden = true;
            if (trigger) trigger.setAttribute("aria-expanded", "false");
            if (typeof dialog.showModal === "function") dialog.showModal();
            else dialog.setAttribute("open", "");
        });

        dialog.querySelectorAll("[data-zy-language-option]").forEach(function (option) {
            option.addEventListener("click", function () {
                applyLanguage(option.getAttribute("data-zy-language-option"));
                dialog.close();
                openButton.focus();
            });
        });

        dialog.addEventListener("click", function (event) {
            if (event.target === dialog) dialog.close();
        });
    }

    function init() {
        document.querySelectorAll("[data-zy-user-menu]").forEach(setupMenu);
        setupLanguageDialog();
        applyLanguage(currentLanguage());
    }

    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", init);
    else init();
})();
