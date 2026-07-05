
(function () {
    "use strict";

    function normalize(path) {
        if (!path) return "/";
        var p = path.split("?")[0].split("#")[0].replace(/\/+$/, "").toLowerCase();
        return p || "/";
    }

    function linkPath(link) {
        try {
            return normalize(new URL(link.href, window.location.origin).pathname);
        } catch (_) {
            return "";
        }
    }

    function aliases(path) {
        var p = normalize(path);
        var list = [p];

        if (p === "/" || p === "/index") list.push("/", "/index");

        if (p === "/employees" || p === "/employees/index") list.push("/employees", "/employees/index");
        if (p === "/employees/create") list.push("/employees/create");
        if (p === "/employeedocuments" || p === "/employeedocuments/index") list.push("/employeedocuments", "/employeedocuments/index");

        return Array.from(new Set(list));
    }

    function applyPreciseActiveMenu() {
        var current = normalize(window.location.pathname);
        var currentAliases = aliases(current);

        var links = Array.from(document.querySelectorAll(
            ".nexora-nav-link[href], .nexora-root-link[href], .nexora-nav-group-links a[href], .sidebar-single-link[href], .nav-module-links a[href]"
        ));

        links.forEach(function (link) {
            link.classList.remove("active", "nexora-active", "is-active");
            link.removeAttribute("aria-current");
        });

        document.querySelectorAll(".nexora-nav-group, .nav-module").forEach(function (group) {
            group.classList.remove("is-active");
            group.removeAttribute("data-active-group");
        });

        var exact = null;

        links.forEach(function (link) {
            var lp = linkPath(link);
            if (!lp) return;

            if (currentAliases.indexOf(lp) >= 0) {
                if (!exact || lp.length > linkPath(exact).length) exact = link;
            }
        });

        // Fallback for child pages that should keep the module open but not highlight Employee List incorrectly.
        if (!exact && current.indexOf("/employees/") === 0) {
            exact = links.find(function (link) { return linkPath(link) === "/employees/index" || linkPath(link) === "/employees"; }) || null;
        }

        if (!exact) return;

        exact.classList.add("active", "nexora-active");
        exact.setAttribute("aria-current", "page");

        var group = exact.closest(".nexora-nav-group, .nav-module");
        if (group) {
            group.classList.add("is-active");
            group.setAttribute("data-active-group", "true");
            group.setAttribute("open", "");
        }

        // Important: on Add Employee page, remove highlight from Employee List if old scripts added it later.
        if (current === "/employees/create") {
            links.forEach(function (link) {
                var lp = linkPath(link);
                if (lp === "/employees" || lp === "/employees/index") {
                    link.classList.remove("active", "nexora-active", "is-active");
                    link.removeAttribute("aria-current");
                }
            });
        }
    }

    function init() {
        applyPreciseActiveMenu();
        setTimeout(applyPreciseActiveMenu, 120);
        setTimeout(applyPreciseActiveMenu, 600);
        setTimeout(applyPreciseActiveMenu, 1200);
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", init);
    } else {
        init();
    }

    window.addEventListener("popstate", init);
})();
