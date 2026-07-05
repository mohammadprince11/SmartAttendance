
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
        var hub = document.querySelector("[data-nxr-profile-kayan-pages]");
        if (!hub) return;

        document.body.classList.add("nxr-profile-kayan-pages-active");

        var buttons = Array.from(hub.querySelectorAll("[data-nxr-profile-tab]"));
        var panes = Array.from(hub.querySelectorAll("[data-nxr-profile-pane]"));
        var key = "NEXORA.Employee.Profile.KayanTab";
        var saved = localStorage.getItem(key) || "personal";

        function activate(name) {
            if (!name) name = "personal";

            buttons.forEach(function (button) {
                button.classList.toggle("active", button.getAttribute("data-nxr-profile-tab") === name);
            });

            panes.forEach(function (pane) {
                pane.classList.toggle("active", pane.getAttribute("data-nxr-profile-pane") === name);
            });

            localStorage.setItem(key, name);
        }

        buttons.forEach(function (button) {
            button.addEventListener("click", function () {
                activate(button.getAttribute("data-nxr-profile-tab"));
            });
        });

        if (!buttons.some(function (b) { return b.getAttribute("data-nxr-profile-tab") === saved; })) {
            saved = "personal";
        }

        activate(saved);
    });
})();
