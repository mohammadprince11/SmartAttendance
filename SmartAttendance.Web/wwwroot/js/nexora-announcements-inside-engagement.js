
(function () {
    "use strict";

    function ready(fn) {
        if (document.readyState === "loading") {
            document.addEventListener("DOMContentLoaded", fn);
        } else {
            fn();
        }
    }

    function normalize(value) {
        return (value || "").toString().replace(/\s+/g, " ").trim().toLowerCase();
    }

    ready(function () {
        var root = document.querySelector("[data-nxr-ann-page]");
        if (!root) return;

        var search = root.querySelector("[data-ann-search]");
        var status = root.querySelector("[data-ann-status]");
        var channel = root.querySelector("[data-ann-channel]");
        var category = root.querySelector("[data-ann-category]");
        var rows = Array.from(root.querySelectorAll("[data-ann-row]"));
        var counter = root.querySelector("[data-ann-counter]");
        var emptyRow = root.querySelector("[data-ann-empty]");

        function apply() {
            var q = normalize(search ? search.value : "");
            var s = normalize(status ? status.value : "");
            var c = normalize(channel ? channel.value : "");
            var cat = normalize(category ? category.value : "");
            var shown = 0;

            rows.forEach(function (row) {
                var text = normalize(row.innerText || row.textContent || "");
                var rowStatus = normalize(row.getAttribute("data-status"));
                var rowChannel = normalize(row.getAttribute("data-channel"));
                var rowCategory = normalize(row.getAttribute("data-category"));

                var ok = true;
                if (q && text.indexOf(q) < 0) ok = false;
                if (s && rowStatus !== s) ok = false;
                if (c && rowChannel !== c) ok = false;
                if (cat && rowCategory !== cat) ok = false;

                row.style.display = ok ? "" : "none";
                if (ok) shown++;
            });

            if (counter) counter.textContent = "عرض " + shown + " من " + rows.length;
            if (emptyRow) emptyRow.classList.toggle("show", shown === 0);
        }

        [search, status, channel, category].forEach(function (el) {
            if (!el) return;
            el.addEventListener("input", apply);
            el.addEventListener("change", apply);
        });

        apply();
    });
})();
