(function () {
    function repairSidebar() {
        document.querySelectorAll(".sidebar .nav-icon").forEach(function (icon) {
            icon.setAttribute("aria-hidden", "true");
        });

        document.querySelectorAll(".sidebar-local-toggle, .sa-sidebar-toggle").forEach(function (btn) {
            btn.setAttribute("aria-label", "Menu");
            btn.setAttribute("title", "Menu");
        });
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", repairSidebar);
    } else {
        repairSidebar();
    }

    setTimeout(repairSidebar, 300);
})();
