/* NEXORA Remove Sidebar Collapse And Icons */
(function () {
    "use strict";

    function removeCollapseMode() {
        document.documentElement.removeAttribute("data-sidebar");
        document.documentElement.classList.remove("nx-sidebar-collapsed-final");
        document.body.classList.remove("nx-sidebar-collapsed-final");
        document.body.classList.remove("sidebar-collapsed");
        document.body.classList.remove("nexora-sidebar-collapsed");

        try {
            localStorage.removeItem("NEXORA.Sidebar.Final.Working");
            localStorage.removeItem("NEXORA.SidebarMode.Final");
            localStorage.removeItem("NEXORA.V2.Sidebar");
            localStorage.removeItem("NEXORA.Sidebar");
            localStorage.removeItem("SidebarCollapsed");
            localStorage.removeItem("sidebar");
        } catch (e) {}

        const shell = document.querySelector(".nexora-shell");
        if (shell) {
            shell.style.gridTemplateColumns = "minmax(0, 1fr) 286px";
        }
    }

    function removeCollapseButtonsAndIcons() {
        document.querySelectorAll(
            "[data-sidebar-toggle], .nx-sidebar-force-toggle, .nexora-menu-toggle, .sidebar-toggle, .navbar-toggler, .nx-collapsed-icon"
        ).forEach(el => el.remove());

        document.querySelectorAll(".nexora-brand-card button, .nexora-brand-card .nexora-icon-button").forEach(el => {
            const text = (el.textContent || "").trim();
            const aria = (el.getAttribute("aria-label") || "").toLowerCase();
            if (text === "☰" || aria.includes("sidebar") || aria.includes("menu") || el.classList.contains("nx-sidebar-force-toggle")) {
                el.remove();
            }
        });
    }

    function disableOldGlobals() {
        window.NexoraSidebarToggleForce = function (event) {
            if (event) {
                event.preventDefault();
                event.stopPropagation();
            }
            removeCollapseMode();
            removeCollapseButtonsAndIcons();
            return false;
        };

        window.NexoraSidebarSetModeForce = function () {
            removeCollapseMode();
            removeCollapseButtonsAndIcons();
        };

        window.NexoraToggleSidebar = window.NexoraSidebarToggleForce;
        window.NexoraApplySidebarMode = window.NexoraSidebarSetModeForce;
    }

    function init() {
        removeCollapseMode();
        removeCollapseButtonsAndIcons();
        disableOldGlobals();
    }

    document.addEventListener("DOMContentLoaded", init);

    /* Re-run after older scripts inject buttons/icons */
    setTimeout(init, 100);
    setTimeout(init, 400);
    setTimeout(init, 1000);
    setTimeout(init, 1800);
})();

