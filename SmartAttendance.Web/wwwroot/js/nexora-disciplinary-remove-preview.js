(function () {
    "use strict";

    function currentTab() {
        try {
            const params = new URLSearchParams(window.location.search);
            return (params.get("tab") || "setup").toLowerCase();
        } catch {
            return "setup";
        }
    }

    function removeSetupPreviewOnly() {
        if (!window.location.pathname.toLowerCase().includes("/disciplinaryrules")) {
            return;
        }

        if (currentTab() !== "setup") {
            return;
        }

        document.querySelectorAll(".nxpen-form-preview").forEach(function (el) {
            el.remove();
        });

        window.NEXORA_SETUP_PREVIEW_REMOVED_ONLY = true;
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", removeSetupPreviewOnly);
    } else {
        removeSetupPreviewOnly();
    }

    setTimeout(removeSetupPreviewOnly, 300);
})();