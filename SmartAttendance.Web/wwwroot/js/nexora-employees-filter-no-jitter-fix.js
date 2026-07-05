
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
        document.body.classList.add("nxr-employees-no-jitter", "nxr-page-size-active");

        // Remove the previous jitter class behavior without removing its CSS file.
        document.body.classList.remove("nxr-employees-filter-symmetry");

        var form = document.querySelector(".nxr-emp-filter");
        if (!form) return;

        var actions = form.querySelector(".nxr-emp-filter-actions");
        if (actions && actions.parentElement === form) {
            form.appendChild(actions);
        }

        // Keep only one page-size control.
        var wraps = Array.from(document.querySelectorAll(".nxr-emp-page-size-wrap"));
        if (wraps.length > 1) {
            wraps.slice(1).forEach(function (wrap) {
                wrap.remove();
            });
        }

        // Do not use MutationObserver here. It caused the fast left/right movement.
        function stabilizeOnce() {
            var label = document.querySelector(".nxr-emp-page-size-control span");
            if (label) label.textContent = "عدد الأسطر";

            document.querySelectorAll(".nxr-filter-auto-hint, .nxr-live-filter-hint").forEach(function (el) {
                el.style.display = "none";
            });

            document.querySelectorAll(".nxr-emp-page-size-status").forEach(function (el) {
                var text = (el.textContent || "").replace(/\s+/g, " ").trim();
                // Keep text stable and short.
                text = text.replace(/^عرض\s+/u, "");
                el.textContent = text;
            });
        }

        stabilizeOnce();
        setTimeout(stabilizeOnce, 250);
        setTimeout(stabilizeOnce, 900);
    });
})();
