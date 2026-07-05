
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
        document.querySelectorAll("[data-nxr-action-menu]").forEach(function (menu) {
            var toggle = menu.querySelector("[data-nxr-action-toggle]");
            if (!toggle) return;

            toggle.addEventListener("click", function (event) {
                event.preventDefault();
                event.stopPropagation();

                document.querySelectorAll("[data-nxr-action-menu].is-open").forEach(function (other) {
                    if (other !== menu) other.classList.remove("is-open");
                });

                menu.classList.toggle("is-open");
            });

            menu.addEventListener("click", function (event) {
                event.stopPropagation();
            });
        });

        document.addEventListener("click", function () {
            document.querySelectorAll("[data-nxr-action-menu].is-open").forEach(function (menu) {
                menu.classList.remove("is-open");
            });
        });

        document.addEventListener("keydown", function (event) {
            if (event.key === "Escape") {
                document.querySelectorAll("[data-nxr-action-menu].is-open").forEach(function (menu) {
                    menu.classList.remove("is-open");
                });
            }
        });
    });
})();
