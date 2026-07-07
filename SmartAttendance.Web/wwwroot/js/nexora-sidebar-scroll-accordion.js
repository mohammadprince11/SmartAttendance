(function () {
    "use strict";

    function getSidebar() {
        return document.querySelector(
            ".nexora-sidebar, .nx-sidebar, .layout-sidebar, .app-sidebar, .sidebar, .side-bar, aside, [role='navigation']"
        );
    }

    function isVisible(el) {
        if (!el) return false;
        const style = window.getComputedStyle(el);
        return style.display !== "none" && style.visibility !== "hidden";
    }

    function textOf(el) {
        return (el && el.textContent ? el.textContent : "").replace(/\s+/g, " ").trim();
    }

    function looksLikeSubmenu(el) {
        if (!el || !isVisible(el)) return false;

        const links = el.querySelectorAll("a, button").length;
        const textLength = textOf(el).length;

        return links > 0 && textLength > 0;
    }

    function findGroupFromTrigger(trigger) {
        let current = trigger;

        for (let i = 0; i < 8 && current && current.parentElement; i++) {
            const next = current.nextElementSibling;

            if (looksLikeSubmenu(next)) {
                return {
                    root: current,
                    submenu: next
                };
            }

            const childSubmenus = Array.from(current.children || []).filter(function (x) {
                return x !== trigger && looksLikeSubmenu(x);
            });

            if (childSubmenus.length > 0) {
                return {
                    root: current,
                    submenu: childSubmenus[0]
                };
            }

            current = current.parentElement;
        }

        return null;
    }

    function findBootstrapTarget(trigger) {
        const selector =
            trigger.getAttribute("data-bs-target") ||
            trigger.getAttribute("data-target") ||
            trigger.getAttribute("href");

        if (!selector || !selector.startsWith("#")) return null;

        try {
            return document.querySelector(selector);
        } catch {
            return null;
        }
    }

    function closeWithBootstrap(el) {
        if (!el) return false;

        if (window.bootstrap && window.bootstrap.Collapse) {
            try {
                const instance = window.bootstrap.Collapse.getOrCreateInstance(el, { toggle: false });
                instance.hide();
                return true;
            } catch {
                return false;
            }
        }

        return false;
    }

    function closeSubmenu(submenu, trigger) {
        if (!submenu) return;

        if (!closeWithBootstrap(submenu)) {
            submenu.classList.remove("show", "open", "expanded", "active");
            submenu.style.display = "none";
            submenu.style.maxHeight = "0px";
            submenu.style.opacity = "0";
        }

        if (trigger) {
            trigger.setAttribute("aria-expanded", "false");
            trigger.classList.remove("show", "open", "expanded", "active");
        }
    }

    function openSubmenu(submenu, trigger) {
        if (!submenu) return;

        submenu.style.display = "";
        submenu.style.maxHeight = submenu.scrollHeight + "px";
        submenu.style.opacity = "1";

        if (trigger) {
            trigger.setAttribute("aria-expanded", "true");
        }
    }

    function collectGroups(sidebar) {
        const groups = [];

        const triggers = Array.from(sidebar.querySelectorAll(
            "[data-bs-toggle='collapse'], [data-toggle='collapse'], [aria-expanded], button, a, .nav-link, .menu-link, .sidebar-link"
        ));

        triggers.forEach(function (trigger) {
            const bsTarget = findBootstrapTarget(trigger);

            if (bsTarget && sidebar.contains(bsTarget)) {
                groups.push({
                    trigger: trigger,
                    submenu: bsTarget
                });
                return;
            }

            const group = findGroupFromTrigger(trigger);
            if (group && group.submenu && sidebar.contains(group.submenu)) {
                groups.push({
                    trigger: trigger,
                    submenu: group.submenu
                });
            }
        });

        const unique = [];
        const seen = new Set();

        groups.forEach(function (g) {
            if (!g.submenu || seen.has(g.submenu)) return;
            seen.add(g.submenu);
            unique.push(g);
        });

        return unique;
    }

    function closeOtherGroups(sidebar, currentSubmenu) {
        const groups = collectGroups(sidebar);

        groups.forEach(function (g) {
            if (g.submenu === currentSubmenu) return;

            const hasActiveLink = !!g.submenu.querySelector(
                ".active, .selected, [aria-current='page']"
            );

            if (hasActiveLink) return;

            closeSubmenu(g.submenu, g.trigger);
        });
    }

    function setupAccordion() {
        const sidebar = getSidebar();
        if (!sidebar) return;

        sidebar.setAttribute("data-nexora-sidebar-accordion", "true");

        sidebar.addEventListener("click", function (e) {
            const trigger = e.target.closest(
                "[data-bs-toggle='collapse'], [data-toggle='collapse'], [aria-expanded], button, a, .nav-link, .menu-link, .sidebar-link"
            );

            if (!trigger || !sidebar.contains(trigger)) return;

            const bsTarget = findBootstrapTarget(trigger);
            let submenu = bsTarget;

            if (!submenu) {
                const group = findGroupFromTrigger(trigger);
                submenu = group ? group.submenu : null;
            }

            if (!submenu || !sidebar.contains(submenu)) return;

            closeOtherGroups(sidebar, submenu);

            setTimeout(function () {
                if (isVisible(submenu)) {
                    openSubmenu(submenu, trigger);
                }
            }, 40);
        }, true);

        const activeLink = sidebar.querySelector(".active, .selected, [aria-current='page']");
        if (activeLink) {
            let current = activeLink.parentElement;
            while (current && current !== sidebar) {
                if (looksLikeSubmenu(current)) {
                    current.style.display = "";
                    current.style.maxHeight = current.scrollHeight + "px";
                    current.style.opacity = "1";
                }
                current = current.parentElement;
            }
        }

        console.log("NEXORA sidebar accordion loaded.");
    }

    function keepSidebarScrollClean() {
        const sidebar = getSidebar();
        if (!sidebar) return;

        sidebar.style.overflowY = "auto";
        sidebar.style.overflowX = "hidden";
        sidebar.style.maxHeight = "100vh";
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", function () {
            setupAccordion();
            keepSidebarScrollClean();
        });
    } else {
        setupAccordion();
        keepSidebarScrollClean();
    }

    window.addEventListener("load", keepSidebarScrollClean);
})();