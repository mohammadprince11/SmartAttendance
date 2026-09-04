(function () {
    "use strict";

    function normalize(path) {
        if (!path) return "/";
        var clean = path
            .split("?")[0]
            .split("#")[0]
            .replace(/\/+$/, "")
            .toLowerCase();

        return clean || "/";
    }

    function hrefPath(link) {
        try {
            return normalize(
                new URL(link.href, window.location.origin).pathname
            );
        } catch (_) {
            return "";
        }
    }

    function isSame(current, target) {
        if (current === target) return true;

        if (target.endsWith("/index")) {
            var withoutIndex =
                target.slice(0, -"/index".length) || "/";

            return current === withoutIndex;
        }

        return false;
    }

    function lockExpandedSidebar() {
        document.documentElement.setAttribute(
            "data-sidebar",
            "expanded"
        );

        if (document.body) {
            document.body.classList.remove(
                "zynora-sidebar-collapsed",
                "zynora-sidebar-open",
                "nxv2-compact"
            );
        }

        [
            "SmartAttendance.Sidebar",
            "ZYNORA.Sidebar.State",
            "ZYNORA.Sidebar.Final.Working",
            "ZYNORA.SidebarMode.Final",
            "ZYNORA.V2.Sidebar",
            "ZYNORA.Sidebar",
            "SidebarCollapsed",
            "sidebar"
        ].forEach(function (key) {
            try {
                localStorage.removeItem(key);
            } catch (_) { }
        });
    }

    function closeOtherGroups(currentGroup) {
        document
            .querySelectorAll(".zynora-nav-group")
            .forEach(function (group) {
                if (group !== currentGroup) {
                    group.open = false;
                }
            });
    }

    function activateCurrentNavigation() {
        var current = normalize(window.location.pathname);
        var links = Array.from(
            document.querySelectorAll(
                ".zynora-nav-link[href]"
            )
        );

        links.forEach(function (link) {
            link.classList.remove(
                "active",
                "zynora-active",
                "is-active"
            );
            link.removeAttribute("aria-current");
        });

        var best = null;
        var bestLength = -1;

        links.forEach(function (link) {
            var path = hrefPath(link);

            if (
                path &&
                isSame(current, path) &&
                path.length > bestLength
            ) {
                best = link;
                bestLength = path.length;
            }
        });

        if (best) {
            best.classList.add(
                "active",
                "zynora-active"
            );
            best.setAttribute("aria-current", "page");

            var activeGroup = best.closest(
                ".zynora-nav-group"
            );

            if (activeGroup) {
                closeOtherGroups(activeGroup);
                activeGroup.open = true;
            }
        }

        document
            .querySelectorAll(".zynora-nav-group")
            .forEach(function (group) {
                var hasActiveLink = group.querySelector(
                    ".zynora-nav-link.active, " +
                    ".zynora-nav-link.zynora-active"
                );

                group.classList.toggle(
                    "is-active",
                    !!hasActiveLink
                );
            });
    }

    function bindAccordion() {
        document
            .querySelectorAll(".zynora-nav-group")
            .forEach(function (group) {
                group.addEventListener(
                    "toggle",
                    function () {
                        if (group.open) {
                            closeOtherGroups(group);
                        }
                    }
                );
            });
    }

    function bindSearchShortcut() {
        document.addEventListener(
            "keydown",
            function (event) {
                if (
                    !(event.ctrlKey || event.metaKey) ||
                    event.key.toLowerCase() !== "k"
                ) {
                    return;
                }

                var input = document.querySelector(
                    "input[type='search'], " +
                    "input[name='SearchTerm'], " +
                    ".nxr-input, " +
                    "input:not([type='hidden'])"
                );

                if (input) {
                    event.preventDefault();
                    input.focus();
                }
            }
        );
    }

    function init() {
        lockExpandedSidebar();
        activateCurrentNavigation();
        bindAccordion();
        bindSearchShortcut();

        window.setTimeout(
            activateCurrentNavigation,
            250
        );
    }

    if (document.readyState === "loading") {
        document.addEventListener(
            "DOMContentLoaded",
            init
        );
    } else {
        init();
    }
})();