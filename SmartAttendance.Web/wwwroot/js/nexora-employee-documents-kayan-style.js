
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
        return (value || "")
            .toString()
            .replace(/\s+/g, " ")
            .trim()
            .toLowerCase();
    }

    ready(function () {
        var fileInput = document.querySelector("[data-nxr-file-input]");
        var fileName = document.querySelector("[data-nxr-file-name]");

        if (fileInput && fileName) {
            fileInput.addEventListener("change", function () {
                if (fileInput.files && fileInput.files.length > 0) {
                    fileName.textContent = fileInput.files[0].name;
                    fileName.classList.remove("empty");
                } else {
                    fileName.textContent = "لم يتم اختيار ملف";
                    fileName.classList.add("empty");
                }
            });
        }

        var table = document.querySelector("[data-nxr-documents-table]");
        var countBadge = document.querySelector("[data-nxr-documents-count]");
        var searchInput = document.querySelector("[data-nxr-doc-search]");
        var typeSelect = document.querySelector("[data-nxr-doc-type]");
        var statusSelect = document.querySelector("[data-nxr-doc-status]");
        var resetButton = document.querySelector("[data-nxr-doc-reset]");

        if (!table) return;

        var rows = Array.from(table.querySelectorAll("[data-nxr-doc-row]"));

        function applyFilter() {
            var search = normalize(searchInput ? searchInput.value : "");
            var type = normalize(typeSelect ? typeSelect.value : "");
            var status = normalize(statusSelect ? statusSelect.value : "");
            var visible = 0;

            rows.forEach(function (row) {
                var rowSearch = normalize(row.getAttribute("data-doc-search"));
                var rowType = normalize(row.getAttribute("data-doc-type"));
                var rowStatus = normalize(row.getAttribute("data-doc-status"));

                var ok = true;

                if (search && rowSearch.indexOf(search) < 0) ok = false;
                if (type && rowType !== type) ok = false;
                if (status && rowStatus !== status) ok = false;

                row.classList.toggle("nxr-doc-row-hidden", !ok);
                if (ok) visible++;
            });

            if (countBadge) {
                countBadge.textContent = visible + " نتيجة";
            }
        }

        [searchInput, typeSelect, statusSelect].forEach(function (control) {
            if (!control) return;
            control.addEventListener("input", applyFilter);
            control.addEventListener("change", applyFilter);
        });

        if (resetButton) {
            resetButton.addEventListener("click", function () {
                if (searchInput) searchInput.value = "";
                if (typeSelect) typeSelect.value = "";
                if (statusSelect) statusSelect.value = "";
                applyFilter();
            });
        }

        applyFilter();
    });
})();
