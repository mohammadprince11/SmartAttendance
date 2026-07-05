
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
        var root = document.querySelector("[data-nxr-hr-ops]");
        if (!root) return;

        var tabs = Array.from(root.querySelectorAll("[data-hr-ops-tab]"));
        var panes = Array.from(root.querySelectorAll("[data-hr-ops-pane]"));
        var key = "NEXORA.HrOperations.ActiveTab";
        var saved = localStorage.getItem(key) || "engagement";

        function activate(name) {
            if (!name) name = "engagement";

            tabs.forEach(function (tab) {
                tab.classList.toggle("active", tab.getAttribute("data-hr-ops-tab") === name);
            });

            panes.forEach(function (pane) {
                pane.classList.toggle("active", pane.getAttribute("data-hr-ops-pane") === name);
            });

            localStorage.setItem(key, name);
        }

        tabs.forEach(function (tab) {
            tab.addEventListener("click", function () {
                activate(tab.getAttribute("data-hr-ops-tab"));
            });
        });

        if (!tabs.some(function (tab) { return tab.getAttribute("data-hr-ops-tab") === saved; })) {
            saved = "engagement";
        }

        activate(saved);
    });
})();
