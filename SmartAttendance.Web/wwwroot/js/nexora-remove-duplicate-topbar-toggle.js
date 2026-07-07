/* NEXORA - Remove duplicate topbar toggle button */
(function () {
    "use strict";

    function removeDuplicateTopbarToggle() {
        const topbar = document.querySelector(".nexora-topbar");
        if (!topbar) return;

        const candidates = topbar.querySelectorAll(
            "[data-sidebar-toggle], .nexora-menu-toggle, .sidebar-toggle, .navbar-toggler, button[aria-label*='sidebar' i], button[aria-label*='menu' i]"
        );

        candidates.forEach(button => {
            button.remove();
        });
    }

    document.addEventListener("DOMContentLoaded", removeDuplicateTopbarToggle);

    // In case layout scripts add it after load
    setTimeout(removeDuplicateTopbarToggle, 250);
    setTimeout(removeDuplicateTopbarToggle, 800);
})();

