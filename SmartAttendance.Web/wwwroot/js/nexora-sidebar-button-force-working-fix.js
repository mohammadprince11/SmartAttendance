/* NEXORA Sidebar Button Force Working Fix */
(function () {
    "use strict";

    const KEY = "NEXORA.Sidebar.Final.Working";

    function setMode(mode) {
        const collapsed = mode === "collapsed";

        if (collapsed) {
            document.documentElement.classList.add("nx-sidebar-collapsed-final");
            document.documentElement.setAttribute("data-sidebar", "collapsed");
        } else {
            document.documentElement.classList.remove("nx-sidebar-collapsed-final");
            document.documentElement.removeAttribute("data-sidebar");
        }

        document.body.classList.remove("sidebar-collapsed");
        document.body.classList.remove("nexora-sidebar-collapsed");
        document.body.classList.toggle("nx-sidebar-collapsed-final", collapsed);

        try {
            localStorage.setItem(KEY, collapsed ? "collapsed" : "expanded");
            localStorage.removeItem("NEXORA.V2.Sidebar");
            localStorage.removeItem("NEXORA.Sidebar");
            localStorage.removeItem("SidebarCollapsed");
        } catch (e) {}

        const shell = document.querySelector(".nexora-shell");
        if (shell) {
            shell.style.gridTemplateColumns = collapsed
                ? "minmax(0, 1fr) var(--nx-sidebar-closed-width, 86px)"
                : "minmax(0, 1fr) var(--nx-sidebar-open-width, 286px)";
        }
    }

    function toggle(event) {
        if (event) {
            event.preventDefault();
            event.stopPropagation();
        }

        const collapsed = document.documentElement.classList.contains("nx-sidebar-collapsed-final");
        setMode(collapsed ? "expanded" : "collapsed");
        return false;
    }

    function getSavedMode() {
        try {
            return localStorage.getItem(KEY) || "expanded";
        } catch (e) {
            return "expanded";
        }
    }

    function ensureButton() {
        const brandCard = document.querySelector(".nexora-brand-card");
        if (!brandCard) return;

        let button = brandCard.querySelector(".nx-sidebar-force-toggle");

        if (!button) {
            button = brandCard.querySelector("[data-sidebar-toggle], button, .nexora-icon-button");
        }

        if (!button) {
            button = document.createElement("button");
            brandCard.insertBefore(button, brandCard.firstChild);
        }

        button.type = "button";
        button.classList.add("nx-sidebar-force-toggle");
        button.setAttribute("aria-label", "Toggle sidebar");
        button.setAttribute("title", "تقليص / توسيع القائمة");
        button.innerHTML = "☰";
        button.onclick = toggle;

        button.addEventListener("click", toggle, true);
    }

    function removeTopbarToggle() {
        document.querySelectorAll(".nexora-topbar [data-sidebar-toggle], .nexora-topbar .nexora-menu-toggle, .nexora-topbar .sidebar-toggle, .nexora-topbar .navbar-toggler").forEach(x => x.remove());
    }

    function init() {
        ensureButton();
        removeTopbarToggle();
        setMode(getSavedMode());
    }

    document.addEventListener("DOMContentLoaded", init);

    // Re-apply after old scripts
    setTimeout(init, 200);
    setTimeout(init, 800);
    setTimeout(init, 1500);

    window.NexoraSidebarToggleForce = toggle;
    window.NexoraSidebarSetModeForce = setMode;
})();

