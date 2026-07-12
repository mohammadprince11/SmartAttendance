(function () {
    "use strict";

    var sidebarStorageKey = "NEXORA.Sidebar.State";
    var desktopQuery = window.matchMedia("(min-width: 981px)");

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

    function readSidebarState() {
        try {
            return localStorage.getItem(sidebarStorageKey) === "collapsed"
                ? "collapsed"
                : "expanded";
        } catch (_) {
            return "expanded";
        }
    }

    function writeSidebarState(state) {
        try {
            localStorage.setItem(sidebarStorageKey, state);
        } catch (_) { }
    }

    function closeAllGroups(except) {
        document.querySelectorAll(".nexora-nav-group").forEach(function (group) {
            if (group !== except) {
                group.open = false;
            }
        });
    }

    function restoreActiveGroup() {
        var activeGroup = document.querySelector(
            ".nexora-nav-group.is-active"
        );

        if (activeGroup) {
            closeAllGroups(activeGroup);
            activeGroup.open = true;
        }
    }

    function syncSidebarControls(state) {
        var expanded = state === "expanded";

        document.querySelectorAll("[data-sidebar-toggle]").forEach(function (button) {
            button.setAttribute("aria-expanded", expanded ? "true" : "false");
            button.setAttribute(
                "aria-label",
                expanded
                    ? "\u062a\u0642\u0644\u064a\u0635 \u0627\u0644\u0642\u0627\u0626\u0645\u0629 \u0627\u0644\u062c\u0627\u0646\u0628\u064a\u0629"
                    : "\u062a\u0648\u0633\u064a\u0639 \u0627\u0644\u0642\u0627\u0626\u0645\u0629 \u0627\u0644\u062c\u0627\u0646\u0628\u064a\u0629"
            );
            button.title = expanded
                ? "\u062a\u0642\u0644\u064a\u0635 \u0627\u0644\u0642\u0627\u0626\u0645\u0629"
                : "\u062a\u0648\u0633\u064a\u0639 \u0627\u0644\u0642\u0627\u0626\u0645\u0629";
        });
    }

    function setSidebarState(state, persist) {
        var nextState = state === "collapsed"
            ? "collapsed"
            : "expanded";

        document.documentElement.setAttribute(
            "data-sidebar",
            nextState
        );

        if (document.body) {
            document.body.classList.toggle(
                "nexora-sidebar-collapsed",
                nextState === "collapsed"
            );
        }

        if (nextState === "collapsed" && desktopQuery.matches) {
            closeAllGroups();
        } else if (desktopQuery.matches) {
            restoreActiveGroup();
        }

        syncSidebarControls(nextState);

        if (persist) {
            writeSidebarState(nextState);
        }
    }

    function applyStableState() {
        document.documentElement.setAttribute("lang", "ar");
        document.documentElement.setAttribute("dir", "rtl");
        document.documentElement.setAttribute("data-theme", "dark");

        if (!document.documentElement.hasAttribute("data-sidebar")) {
            document.documentElement.setAttribute(
                "data-sidebar",
                readSidebarState()
            );
        }

        if (document.body) {
            document.body.setAttribute("dir", "rtl");
            document.body.classList.remove(
                "nxv2-compact",
                "nexora-sidebar-open"
            );
        }

        try {
            localStorage.setItem("SmartAttendance.Language", "ar");
            localStorage.setItem("SmartAttendance.Theme", "dark");
        } catch (_) { }

        document.querySelectorAll(
            "[data-language-button], [data-theme-button], [data-nxv2-density]"
        ).forEach(function (element) {
            element.style.display = "none";
        });
    }

    function activeMenu() {
        var current = normalize(window.location.pathname);
        var links = Array.from(
            document.querySelectorAll(".nexora-nav-link[href]")
        );

        links.forEach(function (link) {
            link.classList.remove(
                "active",
                "nexora-active",
                "is-active"
            );
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
            var active = group.querySelector(
                ".nexora-nav-link.active, .nexora-nav-link.nexora-active"
            );
            group.classList.toggle("is-active", !!active);
        });
    }

    function sidebarControls() {
        document.querySelectorAll("[data-sidebar-toggle]").forEach(function (button) {
            button.addEventListener("click", function () {
                if (!desktopQuery.matches) {
                    return;
                }

                var currentState =
                    document.documentElement.getAttribute("data-sidebar");

                setSidebarState(
                    currentState === "collapsed"
                        ? "expanded"
                        : "collapsed",
                    true
                );
            });
        });

        document.querySelectorAll(
            ".nexora-nav-group > summary"
        ).forEach(function (summary) {
            summary.addEventListener("click", function (event) {
                if (
                    desktopQuery.matches &&
                    document.documentElement.getAttribute(
                        "data-sidebar"
                    ) === "collapsed"
                ) {
                    event.preventDefault();

                    var group = summary.closest(
                        ".nexora-nav-group"
                    );

                    setSidebarState("expanded", true);

                    window.requestAnimationFrame(function () {
                        if (group) {
                            closeAllGroups(group);
                            group.open = true;
                        }

                        summary.focus();
                    });
                }
            });
        });

        document.querySelectorAll(
            ".nexora-nav-group"
        ).forEach(function (group) {
            group.addEventListener("toggle", function () {
                if (group.open) {
                    closeAllGroups(group);
                }
            });
        });

        if (typeof desktopQuery.addEventListener === "function") {
            desktopQuery.addEventListener("change", function () {
                setSidebarState(readSidebarState(), false);
            });
        }
    }

    function shortcuts() {
        document.addEventListener("keydown", function (event) {
            if (
                (event.ctrlKey || event.metaKey) &&
                event.shiftKey &&
                event.key.toLowerCase() === "b" &&
                desktopQuery.matches
            ) {
                event.preventDefault();

                var currentState =
                    document.documentElement.getAttribute("data-sidebar");

                setSidebarState(
                    currentState === "collapsed"
                        ? "expanded"
                        : "collapsed",
                    true
                );
                return;
            }

            if (
                (event.ctrlKey || event.metaKey) &&
                event.key.toLowerCase() === "k"
            ) {
                var input = document.querySelector(
                    "input[type='search'], input[name='SearchTerm'], .nxr-input, input:not([type='hidden'])"
                );

                if (input) {
                    event.preventDefault();
                    input.focus();
                }
            }
        });
    }

    function refreshStableUi() {
        applyStableState();
        activeMenu();
        setSidebarState(readSidebarState(), false);
    }

    function init() {
        refreshStableUi();
        sidebarControls();
        shortcuts();

        window.setTimeout(refreshStableUi, 250);
        window.setTimeout(refreshStableUi, 800);
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", init);
    } else {
        init();
    }
})();