
(function () {
    "use strict";

    function normalize(path) {
        if (!path) return "/";
        var clean = path.split("?")[0].split("#")[0].replace(/\/+$/, "").toLowerCase();
        return clean || "/";
    }

    function hrefPath(link) {
        try {
            return normalize(new URL(link.href, window.location.origin).pathname);
        } catch (_) {
            return "";
        }
    }

    function isSame(current, target) {
        if (current === target) return true;
        if (target.endsWith("/index")) {
            var noIndex = target.slice(0, -"/index".length) || "/";
            return current === noIndex;
        }
        return false;
    }

    function applyStableState() {
        document.documentElement.setAttribute("lang", "ar");
        document.documentElement.setAttribute("dir", "rtl");
        document.documentElement.setAttribute("data-theme", "dark");
        document.documentElement.setAttribute("data-sidebar", "expanded");

        if (document.body) {
            document.body.setAttribute("dir", "rtl");
            document.body.classList.remove("nxv2-compact", "nexora-sidebar-open");
        }

        try {
            localStorage.setItem("SmartAttendance.Language", "ar");
            localStorage.setItem("SmartAttendance.Theme", "dark");
        } catch (_) { }

        document.querySelectorAll("[data-language-button], [data-theme-button], [data-nxv2-density], [data-sidebar-toggle]").forEach(function (el) {
            el.style.display = "none";
        });
    }

    function activeMenu() {
        var current = normalize(window.location.pathname);
        var links = Array.from(document.querySelectorAll(".nexora-nav-link[href]"));

        links.forEach(function (link) {
            link.classList.remove("active", "nexora-active", "is-active");
            link.removeAttribute("aria-current");
        });

        var best = null;
        var bestLength = -1;

        links.forEach(function (link) {
            var path = hrefPath(link);
            if (!path) return;

            if (isSame(current, path) && path.length > bestLength) {
                best = link;
                bestLength = path.length;
            }
        });

        if (best) {
            best.classList.add("active", "nexora-active");
            best.setAttribute("aria-current", "page");
            var details = best.closest("details");
            if (details) details.open = true;
        }

        document.querySelectorAll(".nexora-nav-group").forEach(function (group) {
            var active = group.querySelector(".nexora-nav-link.active, .nexora-nav-link.nexora-active");
            group.classList.toggle("is-active", !!active);
        });
    }

    function shortcuts() {
        document.addEventListener("keydown", function (event) {
            if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "k") {
                var input = document.querySelector("input[type='search'], input[name='SearchTerm'], .nxr-input, input:not([type='hidden'])");
                if (input) {
                    event.preventDefault();
                    input.focus();
                }
            }
        });
    }

    function init() {
        applyStableState();
        activeMenu();
        shortcuts();
        setTimeout(function () {
            applyStableState();
            activeMenu();
        }, 250);
        setTimeout(function () {
            applyStableState();
            activeMenu();
        }, 800);
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", init);
    } else {
        init();
    }
})();
