/* NEXORA Proper Sidebar Collapse Fix */
(function () {
    "use strict";

    const STORAGE_KEY = "NEXORA.SidebarMode.Final";

    function isCollapsed() {
        return document.documentElement.getAttribute("data-sidebar") === "collapsed" ||
            document.body.classList.contains("nxr-sidebar-collapsed");
    }

    function applySidebarMode(mode) {
        const collapsed = mode === "collapsed";

        if (collapsed) {
            document.documentElement.setAttribute("data-sidebar", "collapsed");
            document.body.classList.add("nxr-sidebar-collapsed");
            document.body.classList.remove("sidebar-collapsed");
        } else {
            document.documentElement.removeAttribute("data-sidebar");
            document.body.classList.remove("nxr-sidebar-collapsed");
            document.body.classList.remove("sidebar-collapsed");
        }

        try {
            localStorage.setItem(STORAGE_KEY, collapsed ? "collapsed" : "expanded");
            /* Neutralize older scripts */
            localStorage.removeItem("NEXORA.V2.Sidebar");
            localStorage.removeItem("SidebarCollapsed");
        } catch (e) {}

        const shell = document.querySelector(".nexora-shell");
        if (shell) {
            shell.style.gridTemplateColumns = collapsed
                ? "minmax(0, 1fr) var(--nxr-sidebar-collapsed, 92px)"
                : "minmax(0, 1fr) var(--nxr-sidebar-expanded, 286px)";
        }
    }

    function toggleSidebar(event) {
        if (event) {
            event.preventDefault();
            event.stopPropagation();
        }

        applySidebarMode(isCollapsed() ? "expanded" : "collapsed");
        return false;
    }

    function getInitialMode() {
        try {
            return localStorage.getItem(STORAGE_KEY) || "expanded";
        } catch (e) {
            return "expanded";
        }
    }

    function wireToggleButtons() {
        const brandCard = document.querySelector(".nexora-brand-card");
        const buttons = [];

        document.querySelectorAll("[data-sidebar-toggle]").forEach(btn => buttons.push(btn));

        if (brandCard) {
            brandCard.querySelectorAll("button, .nexora-icon-button, [role='button']").forEach(btn => buttons.push(btn));
        }

        [...new Set(buttons)].forEach(btn => {
            if (btn.closest(".nexora-topbar")) return;

            btn.setAttribute("type", "button");
            btn.setAttribute("aria-label", "Toggle sidebar");
            btn.dataset.nxrProperCollapse = "1";

            btn.onclick = toggleSidebar;
        });
    }

    function addTitlesForCollapsed() {
        document.querySelectorAll(".nexora-nav-link").forEach(link => {
            if (!link.getAttribute("title")) {
                const text = (link.textContent || "").trim();
                if (text) link.setAttribute("title", text);
            }
        });

        document.querySelectorAll(".nexora-nav-group > summary").forEach(summary => {
            if (!summary.getAttribute("title")) {
                const text = (summary.textContent || "").trim();
                if (text) summary.setAttribute("title", text);
            }
        });
    }

    function init() {
        wireToggleButtons();
        addTitlesForCollapsed();
        applySidebarMode(getInitialMode());
    }

    document.addEventListener("DOMContentLoaded", init);

    /*
      Older force-expanded patch used timers at 100ms/500ms.
      These timers re-apply the user's chosen state after old scripts run.
    */
    setTimeout(init, 650);
    setTimeout(init, 1200);

    window.NexoraToggleSidebar = toggleSidebar;
    window.NexoraApplySidebarMode = applySidebarMode;
})();
