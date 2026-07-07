/* NEXORA Force Expanded Sidebar Fix */
(function () {
    "use strict";

    function forceExpandedSidebar() {
        try {
            localStorage.removeItem("NEXORA.V2.Sidebar");
            localStorage.removeItem("NEXORA.Sidebar");
            localStorage.removeItem("sidebar");
            localStorage.removeItem("SidebarCollapsed");
        } catch (e) {
            // ignore localStorage errors
        }

        document.documentElement.removeAttribute("data-sidebar");
        document.body.classList.remove("sidebar-collapsed");
        document.body.classList.remove("nexora-sidebar-collapsed");

        const shell = document.querySelector(".nexora-shell");
        if (shell) {
            shell.style.gridTemplateColumns = "minmax(0, 1fr) var(--nxr-sidebar, 286px)";
        }

        document.querySelectorAll(".nexora-brand-copy,.nexora-nav-text,.nexora-nav-link span:not(.nexora-nav-dot),.nexora-nav-group-links").forEach(el => {
            el.style.removeProperty("display");
        });
    }

    document.addEventListener("DOMContentLoaded", forceExpandedSidebar);
    setTimeout(forceExpandedSidebar, 100);
    setTimeout(forceExpandedSidebar, 500);
})();

