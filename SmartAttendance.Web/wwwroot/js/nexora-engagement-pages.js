
(function () {
    "use strict";

    function ready(fn) {
        if (document.readyState === "loading") {
            document.addEventListener("DOMContentLoaded", fn);
        } else {
            fn();
        }
    }

    ready(function () {
        var root = document.querySelector("[data-nxr-engagement-pages]");
        if (!root) return;

        var tabs = Array.from(root.querySelectorAll("[data-engagement-tab]"));
        var panes = Array.from(root.querySelectorAll("[data-engagement-pane]"));
        var key = "NEXORA.HrOperations.EngagementTab";
        var saved = localStorage.getItem(key) || "announcements";

        function activate(name) {
            if (!name) name = "announcements";

            tabs.forEach(function (tab) {
                tab.classList.toggle("active", tab.getAttribute("data-engagement-tab") === name);
            });

            panes.forEach(function (pane) {
                pane.classList.toggle("active", pane.getAttribute("data-engagement-pane") === name);
            });

            localStorage.setItem(key, name);
        }

        tabs.forEach(function (tab) {
            tab.addEventListener("click", function () {
                activate(tab.getAttribute("data-engagement-tab"));
            });
        });

        if (!tabs.some(function (tab) { return tab.getAttribute("data-engagement-tab") === saved; })) {
            saved = "announcements";
        }

        activate(saved);
    });
})();
