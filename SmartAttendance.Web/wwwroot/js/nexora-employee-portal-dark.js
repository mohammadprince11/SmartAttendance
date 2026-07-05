
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
        var root = document.querySelector("[data-nxep-page]");
        if (!root) return;

        var tabs = Array.from(root.querySelectorAll("[data-nxep-tab]"));
        var panes = Array.from(root.querySelectorAll("[data-nxep-pane]"));
        var quick = Array.from(root.querySelectorAll("[data-nxep-jump]"));
        var key = "NEXORA.EmployeePortal.ActiveTab";

        function activate(name) {
            if (!name) name = "home";

            tabs.forEach(function (tab) {
                tab.classList.toggle("active", tab.getAttribute("data-nxep-tab") === name);
            });

            panes.forEach(function (pane) {
                pane.classList.toggle("active", pane.getAttribute("data-nxep-pane") === name);
            });

            localStorage.setItem(key, name);
            window.scrollTo({ top: 0, behavior: "smooth" });
        }

        tabs.forEach(function (tab) {
            tab.addEventListener("click", function () {
                activate(tab.getAttribute("data-nxep-tab"));
            });
        });

        quick.forEach(function (button) {
            button.addEventListener("click", function () {
                activate(button.getAttribute("data-nxep-jump"));
            });
        });

        var saved = localStorage.getItem(key);
        if (!tabs.some(function (tab) { return tab.getAttribute("data-nxep-tab") === saved; })) {
            saved = "home";
        }

        activate(saved);
    });
})();
