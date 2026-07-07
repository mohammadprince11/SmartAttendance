(function () {
    function ready() {
        document.documentElement.classList.remove("sa-preload");
        document.documentElement.classList.add("sa-ready");
    }

    function toggleSidebar(event) {
        if (event) {
            event.preventDefault();
            event.stopPropagation();
        }

        document.documentElement.setAttribute("data-sidebar", "expanded");
        document.documentElement.setAttribute("data-sidebar-mobile", "open");

        try {
            localStorage.removeItem("SmartAttendance.Sidebar");
            localStorage.removeItem("NEXORA.Sidebar.Final.Working");
            localStorage.removeItem("NEXORA.SidebarMode.Final");
            localStorage.removeItem("NEXORA.V2.Sidebar");
            localStorage.removeItem("NEXORA.Sidebar");
            localStorage.removeItem("SidebarCollapsed");
            localStorage.removeItem("sidebar");
        } catch (_) { }

        return false;
    }

    function bindSidebar() {
        document.querySelectorAll("[data-sidebar-toggle]").forEach(function (button) {
            button.addEventListener("click", toggleSidebar);
        });

        document.addEventListener("click", function (event) {
            if (!window.matchMedia("(max-width: 980px)").matches) return;

            var sidebar = document.querySelector(".nexora-sidebar");
            var toggle = event.target.closest("[data-sidebar-toggle]");

            if (!sidebar || toggle) return;

            if (!sidebar.contains(event.target)) {
                document.documentElement.setAttribute("data-sidebar-mobile", "open");
            }
        });
    }

    function markActiveLinks() {
        var currentPath = window.location.pathname.toLowerCase();

        document.querySelectorAll(".nexora-nav a[href]").forEach(function (link) {
            try {
                var url = new URL(link.href, window.location.origin);
                var linkPath = url.pathname.toLowerCase();

                var isActive = currentPath === linkPath || (linkPath !== "/" && currentPath.startsWith(linkPath));
                link.classList.toggle("is-active", isActive);

                if (isActive) {
                    var group = link.closest("details");
                    if (group) group.open = true;
                }
            } catch (_) { }
        });
    }

    function init() {
        bindSidebar();
        markActiveLinks();
        ready();
        setTimeout(markActiveLinks, 250);
        setTimeout(ready, 400);
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", init);
    } else {
        init();
    }
})();

