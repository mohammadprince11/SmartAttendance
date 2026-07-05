
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
        document.body.classList.add("nxr-employees-table-modern");

        var table = document.querySelector(".nxr-emp-table");
        if (!table) return;

        var wrap = table.closest(".nxr-table-wrap") || table.parentElement;
        if (wrap) {
            wrap.classList.add("nxr-modern-emp-table-wrap", "nxr-emp-table-wrap");
        }

        // Ensure action header has a modern label if old text stayed.
        var lastHeader = table.querySelector("thead th:last-child");
        if (lastHeader) {
            lastHeader.textContent = "الإجراء";
        }

        // Make file buttons consistent.
        table.querySelectorAll("a, button").forEach(function (el) {
            var text = (el.textContent || "").replace(/\s+/g, " ").trim();
            if (text === "ملف") {
                el.classList.add("nxr-btn");
            }
        });
    });
})();
