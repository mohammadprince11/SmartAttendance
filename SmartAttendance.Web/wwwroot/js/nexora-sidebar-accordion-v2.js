(function () {
    "use strict";

    if (window.__NEXORA_SIDEBAR_ONLY_ONE_OPEN__) {
        return;
    }

    window.__NEXORA_SIDEBAR_ONLY_ONE_OPEN__ = true;

    var sidebarSelector = ".nexora-sidebar, .nx-sidebar, .layout-sidebar, .app-sidebar, .sidebar, .side-bar, aside, [role='navigation']";

    function getSidebar() {
        return document.querySelector(sidebarSelector);
    }

    function visible(el) {
        if (!el) return false;
        var st = window.getComputedStyle(el);
        return st.display !== "none" && st.visibility !== "hidden" && el.offsetHeight > 2;
    }

    function hasMenuItems(el) {
        if (!el) return false;
        return el.querySelectorAll("a, button, [role='button'], [role='menuitem']").length > 0;
    }

    function getText(el) {
        return (el && el.textContent ? el.textContent : "").replace(/\s+/g, " ").trim();
    }

    function looksLikeGroup(el, sidebar) {
        if (!el || el === sidebar || el === document.body || el === document.documentElement) {
            return false;
        }

        var rect = el.getBoundingClientRect();
        var sidebarRect = sidebar.getBoundingClientRect();

        if (rect.width < sidebarRect.width * 0.55) return false;
        if (rect.height < 45) return false;

        var controls = el.querySelectorAll("a, button, [role='button'], [aria-expanded], [aria-controls]").length;
        var textLength = getText(el).length;

        return controls >= 2 && textLength > 0;
    }

    function findGroupFromAnyElement(sidebar, el) {
        var current = el;

        while (current && current !== sidebar && current !== document.body) {
            if (looksLikeGroup(current, sidebar)) {
                return current;
            }

            current = current.parentElement;
        }

        return null;
    }

    function isClickOnGroupHeader(group, clicked) {
        if (!group || !clicked) return false;

        var groupRect = group.getBoundingClientRect();
        var clickedRect = clicked.getBoundingClientRect();

        var y = clickedRect.top - groupRect.top;

        return y >= -5 && y <= 70;
    }

    function findMenuInsideGroup(group, clicked) {
        if (!group) return null;

        var explicit = null;
        var selector =
            clicked.getAttribute("data-bs-target") ||
            clicked.getAttribute("data-target") ||
            clicked.getAttribute("href");

        if (selector && selector.indexOf("#") === 0) {
            try {
                explicit = document.querySelector(selector);
            } catch (e) {
                explicit = null;
            }
        }

        if (explicit && group.contains(explicit)) {
            return explicit;
        }

        var groupRect = group.getBoundingClientRect();

        var candidates = Array.prototype.slice.call(group.querySelectorAll("ul, ol, .collapse, .submenu, .sub-menu, .menu, .nav, div"))
            .filter(function (el) {
                if (el === group) return false;
                if (el.contains(clicked)) return false;
                if (!hasMenuItems(el)) return false;

                var rect = el.getBoundingClientRect();

                if (rect.top < groupRect.top + 35) return false;

                return true;
            });

        candidates.sort(function (a, b) {
            return b.scrollHeight - a.scrollHeight;
        });

        return candidates[0] || null;
    }

    function getGroupMenu(group) {
        if (!group) return null;

        var firstControl = group.querySelector("[data-bs-toggle='collapse'], [data-toggle='collapse'], [aria-expanded], [aria-controls], button, a, [role='button']");

        if (firstControl) {
            return findMenuInsideGroup(group, firstControl);
        }

        var candidates = Array.prototype.slice.call(group.children)
            .filter(function (el) {
                return hasMenuItems(el);
            });

        return candidates[candidates.length - 1] || null;
    }

    function openGroup(group) {
        var menu = getGroupMenu(group);

        group.removeAttribute("data-nexora-group-closed");
        group.classList.add("nexora-open");

        if (menu) {
            menu.removeAttribute("data-nexora-menu-closed");
            menu.style.display = "";
            menu.style.maxHeight = "";
            menu.style.opacity = "";
            menu.style.overflow = "";

            if (!visible(menu)) {
                menu.style.display = "block";
            }
        }

        var trigger = group.querySelector("[aria-expanded]");
        if (trigger) {
            trigger.setAttribute("aria-expanded", "true");
        }
    }

    function closeGroup(group) {
        var menu = getGroupMenu(group);

        group.setAttribute("data-nexora-group-closed", "true");
        group.classList.remove("nexora-open", "open", "show", "expanded", "is-open");

        if (menu) {
            menu.setAttribute("data-nexora-menu-closed", "true");
            menu.classList.remove("show", "open", "expanded", "is-open");
            menu.style.display = "none";
            menu.style.maxHeight = "0px";
            menu.style.opacity = "0";
            menu.style.overflow = "hidden";
        }

        var triggers = group.querySelectorAll("[aria-expanded]");
        triggers.forEach(function (trigger) {
            trigger.setAttribute("aria-expanded", "false");
        });
    }

    function getSiblingGroups(sidebar, currentGroup) {
        var parent = currentGroup.parentElement;

        for (var level = 0; level < 5 && parent && parent !== sidebar.parentElement; level++) {
            var groups = Array.prototype.slice.call(parent.children)
                .filter(function (el) {
                    return looksLikeGroup(el, sidebar);
                });

            if (groups.length >= 2 && groups.indexOf(currentGroup) >= 0) {
                return groups;
            }

            parent = parent.parentElement;
        }

        var all = Array.prototype.slice.call(sidebar.querySelectorAll("div, section, li"))
            .filter(function (el) {
                return looksLikeGroup(el, sidebar);
            });

        var top = [];

        all.forEach(function (g) {
            var insideAnother = all.some(function (other) {
                return other !== g && other.contains(g);
            });

            if (!insideAnother) {
                top.push(g);
            }
        });

        return top.length ? top : all;
    }

    function closeOthers(sidebar, currentGroup) {
        var groups = getSiblingGroups(sidebar, currentGroup);

        groups.forEach(function (group) {
            if (group === currentGroup) {
                openGroup(group);
            } else {
                closeGroup(group);
            }
        });
    }

    function activeGroupOnLoad(sidebar) {
        var active = sidebar.querySelector(".active, .selected, [aria-current='page']");

        if (!active) return null;

        return findGroupFromAnyElement(sidebar, active);
    }

    function setupInitialState(sidebar) {
        var group = activeGroupOnLoad(sidebar);

        if (group) {
            closeOthers(sidebar, group);
        }
    }

    function setup() {
        var sidebar = getSidebar();

        if (!sidebar) return;

        sidebar.style.overflowY = "auto";
        sidebar.style.overflowX = "hidden";
        sidebar.style.maxHeight = "100vh";

        sidebar.setAttribute("data-nexora-sidebar-only-one-open", "true");

        setupInitialState(sidebar);

        sidebar.addEventListener("click", function (event) {
            var clicked = event.target.closest("button, a, [role='button'], [aria-expanded], [aria-controls], [data-bs-toggle='collapse'], [data-toggle='collapse'], div, span");

            if (!clicked || !sidebar.contains(clicked)) return;

            var group = findGroupFromAnyElement(sidebar, clicked);

            if (!group) return;

            if (!isClickOnGroupHeader(group, clicked)) {
                return;
            }

            setTimeout(function () {
                closeOthers(sidebar, group);
            }, 80);
        }, true);

        console.log("NEXORA sidebar only one open loaded");
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", setup);
    } else {
        setup();
    }

    window.addEventListener("load", function () {
        setTimeout(setup, 150);
    });
})();