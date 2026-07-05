
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
        document.body.classList.add("nxr-employees-filter-symmetry", "nxr-page-size-active");

        var form = document.querySelector(".nxr-emp-filter");
        if (!form) return;

        var actions = form.querySelector(".nxr-emp-filter-actions");
        if (actions && actions.parentElement === form) {
            form.appendChild(actions);
        }

        // Remove duplicated page-size controls if any old script created more than one.
        var wraps = Array.from(document.querySelectorAll(".nxr-emp-page-size-wrap"));
        if (wraps.length > 1) {
            wraps.slice(1).forEach(function (wrap) { wrap.remove(); });
        }

        var pageSizeLabel = document.querySelector(".nxr-emp-page-size-control span");
        if (pageSizeLabel) {
            pageSizeLabel.textContent = "عدد الأسطر";
        }

        // Make counter shorter when possible: "عرض 100 من 1356" => "100 من 1356"
        function shortenCounter() {
            document.querySelectorAll(".nxr-emp-page-size-status").forEach(function (el) {
                var text = (el.textContent || "").replace(/\s+/g, " ").trim();
                text = text.replace(/^عرض\s+/u, "");
                el.textContent = text;
            });
        }

        shortenCounter();
        setTimeout(shortenCounter, 150);
        setTimeout(shortenCounter, 600);

        var observer = new MutationObserver(function () {
            clearTimeout(window.__nxrSymCounterTimer);
            window.__nxrSymCounterTimer = setTimeout(shortenCounter, 50);
        });

        document.querySelectorAll(".nxr-emp-page-size-status").forEach(function (el) {
            observer.observe(el, { childList: true, characterData: true, subtree: true });
        });
    });
})();
