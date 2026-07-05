
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
        document.body.classList.add("nxr-page-size-active", "nxr-employees-filter-polished");

        var form = document.querySelector(".nxr-emp-filter");
        if (!form) return;

        // Move the actions block to the end and ensure only one page size control is visible.
        var actions = form.querySelector(".nxr-emp-filter-actions");
        if (actions && actions.parentElement === form) {
            form.appendChild(actions);
        }

        var pageSizeWraps = Array.from(document.querySelectorAll(".nxr-emp-page-size-wrap"));
        if (pageSizeWraps.length > 1) {
            pageSizeWraps.slice(1).forEach(function (wrap) {
                wrap.remove();
            });
        }

        // Rename old counter text if it still exists.
        document.querySelectorAll(".nxr-filter-auto-hint, .nxr-live-filter-hint").forEach(function (el) {
            el.style.display = "none";
        });

        // Improve Arabic label if the old text remains.
        var pageSizeLabel = document.querySelector(".nxr-emp-page-size-control span");
        if (pageSizeLabel) {
            pageSizeLabel.textContent = "عدد الأسطر";
        }

        // Make reset button text shorter only if needed.
        document.querySelectorAll(".nxr-emp-filter-actions a, .nxr-emp-filter-actions button").forEach(function (el) {
            var text = (el.textContent || "").replace(/\s+/g, " ").trim();
            if (text === "إعادة تعيين") {
                el.textContent = "إعادة تعيين";
            }
        });
    });
})();
