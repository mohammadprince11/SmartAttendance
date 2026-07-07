(function () {
    "use strict";

    function normalizePath(path) {
        if (!path) return "/";
        var cleaned = path.split("?")[0].split("#")[0].replace(/\/+$/, "").toLowerCase();
        return cleaned || "/";
    }

    function getPathFromLink(link) {
        try {
            return normalizePath(new URL(link.href, window.location.origin).pathname);
        } catch (_) {
            return "";
        }
    }

    function findBestActiveLink() {
        var currentPath = normalizePath(window.location.pathname);
        var links = Array.from(document.querySelectorAll(
            ".sidebar-single-link[href], .nav-module-links a[href], .nexora-nav-link[href], .nexora-nav-group-links a[href]"
        ));

        var best = null;
        var bestLength = -1;

        links.forEach(function (link) {
            var linkPath = getPathFromLink(link);
            if (!linkPath) return;

            var exact = currentPath === linkPath;
            var child = linkPath !== "/" && currentPath.startsWith(linkPath + "/");

            if ((exact || child) && linkPath.length > bestLength) {
                best = link;
                bestLength = linkPath.length;
            }
        });

        return best;
    }

    function applyActiveState() {
        var allModules = document.querySelectorAll(".nav-module, .nexora-nav-group");
        var allLinks = document.querySelectorAll(
            ".sidebar-single-link, .nav-module-links a, .nexora-nav-link, .nexora-nav-group-links a"
        );

        allModules.forEach(function (module) {
            module.classList.remove("is-active");
        });

        allLinks.forEach(function (link) {
            link.classList.remove("active");
            link.removeAttribute("aria-current");
        });

        var activeLink = findBestActiveLink();
        if (!activeLink) return;

        activeLink.classList.add("active");
        activeLink.setAttribute("aria-current", "page");

        var parentModule = activeLink.closest(".nav-module, .nexora-nav-group");
        if (parentModule) {
            parentModule.classList.add("is-active");
            parentModule.setAttribute("open", "");
        }
    }

    function makeOpenModulesCalm() {
        document.querySelectorAll(".nav-module, .nexora-nav-group").forEach(function (module) {
            module.addEventListener("toggle", function () {
                window.setTimeout(applyActiveState, 0);
            });
        });
    }

    function init() {
        applyActiveState();
        makeOpenModulesCalm();

        window.setTimeout(applyActiveState, 100);
        window.setTimeout(applyActiveState, 500);
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", init);
    } else {
        init();
    }
})();

