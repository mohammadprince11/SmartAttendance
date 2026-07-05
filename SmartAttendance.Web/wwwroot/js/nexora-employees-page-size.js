
(function () {
    "use strict";

    function ready(fn) {
        if (document.readyState === "loading") {
            document.addEventListener("DOMContentLoaded", fn);
        } else {
            fn();
        }
    }

    function isDataRow(row) {
        if (!row) return false;
        if (row.querySelector(".nxr-empty")) return false;
        if (row.hasAttribute("data-nxr-smooth-empty")) return false;
        return true;
    }

    function isFilteredVisible(row) {
        return !row.classList.contains("nxr-row-hidden") &&
               !row.classList.contains("nxr-doc-row-hidden");
    }

    ready(function () {
        var table = document.querySelector(".nxr-emp-table");
        if (!table || !table.tBodies.length) return;

        document.body.classList.add("nxr-page-size-active");

        var tbody = table.tBodies[0];
        var rows = Array.from(tbody.querySelectorAll("tr")).filter(isDataRow);

        if (!rows.length) return;

        // Remove duplicate old page-size controls if previous patch inserted more than one.
        document.querySelectorAll(".nxr-emp-page-size-wrap").forEach(function (oldControl) {
            oldControl.remove();
        });

        var savedValue = localStorage.getItem("NEXORA.EmployeeList.PageSize") || "25";
        if (["10", "25", "50", "100", "all"].indexOf(savedValue) < 0) {
            savedValue = "25";
        }

        var wrap = document.createElement("div");
        wrap.className = "nxr-emp-page-size-wrap";
        wrap.innerHTML =
            '<label class="nxr-emp-page-size-control">' +
                '<span>عدد الأسطر</span>' +
                '<select data-nxr-emp-page-size aria-label="عدد الأسطر">' +
                    '<option value="10">10</option>' +
                    '<option value="25">25</option>' +
                    '<option value="50">50</option>' +
                    '<option value="100">100</option>' +
                    '<option value="all">الكل</option>' +
                '</select>' +
            '</label>' +
            '<span class="nxr-emp-page-size-status" data-nxr-emp-page-size-status>عرض 25</span>';

        var select = wrap.querySelector("[data-nxr-emp-page-size]");
        var status = wrap.querySelector("[data-nxr-emp-page-size-status]");
        select.value = savedValue;

        var filterActions = document.querySelector(".nxr-emp-filter-actions");
        var filterForm = document.querySelector(".nxr-emp-filter");

        if (filterActions) {
            // Put page size before reset button if possible.
            var resetLink = Array.from(filterActions.querySelectorAll("a, button")).find(function (el) {
                return (el.textContent || "").indexOf("إعادة") >= 0;
            });

            if (resetLink) {
                filterActions.insertBefore(wrap, resetLink);
            } else {
                filterActions.appendChild(wrap);
            }
        } else if (filterForm) {
            filterForm.appendChild(wrap);
        } else {
            var listHead = document.querySelector(".nxr-emp-list-head") || table.parentElement;
            if (listHead) listHead.appendChild(wrap);
        }

        function getVisibleRows() {
            return rows.filter(isFilteredVisible);
        }

        function updateGlobalCounters(shown, total) {
            var text = "عرض " + shown + " من " + total;

            if (status) {
                status.textContent = text;
            }

            // Update top/list badges to avoid contradictory "1356" messages.
            document.querySelectorAll(".nxr-emp-count").forEach(function (el) {
                el.textContent = text;
            });

            var listText = document.querySelector(".nxr-emp-list-head p");
            if (listText) {
                listText.textContent = text + " حسب الفلترة الحالية.";
            }

            document.querySelectorAll(".nxr-filter-auto-hint, .nxr-live-filter-hint").forEach(function (el) {
                el.textContent = text;
            });
        }

        function applyPageSize() {
            rows.forEach(function (row) {
                row.classList.remove("nxr-page-row-hidden");
            });

            var visibleRows = getVisibleRows();
            var value = select.value;
            var size = value === "all" ? visibleRows.length : parseInt(value, 10);

            if (!Number.isFinite(size) || size <= 0) size = 25;

            visibleRows.forEach(function (row, index) {
                if (index >= size) {
                    row.classList.add("nxr-page-row-hidden");
                }
            });

            var shown = Math.min(size, visibleRows.length);
            updateGlobalCounters(shown, visibleRows.length);
        }

        select.addEventListener("change", function () {
            localStorage.setItem("NEXORA.EmployeeList.PageSize", select.value);
            applyPageSize();
        });

        // Re-apply after existing smooth filter changes.
        var filterControls = document.querySelectorAll(".nxr-emp-filter input, .nxr-emp-filter select, .nxr-emp-filter button, .nxr-emp-filter a");
        filterControls.forEach(function (control) {
            if (control === select) return;

            control.addEventListener("input", function () {
                setTimeout(applyPageSize, 80);
                setTimeout(applyPageSize, 260);
            });

            control.addEventListener("change", function () {
                setTimeout(applyPageSize, 80);
                setTimeout(applyPageSize, 260);
            });

            control.addEventListener("click", function () {
                setTimeout(applyPageSize, 80);
                setTimeout(applyPageSize, 260);
            });
        });

        var observer = new MutationObserver(function () {
            clearTimeout(window.__nxrEmpPageSizeFixTimer);
            window.__nxrEmpPageSizeFixTimer = setTimeout(applyPageSize, 70);
        });

        observer.observe(tbody, {
            childList: true,
            subtree: true,
            attributes: true,
            attributeFilter: ["class"]
        });

        setTimeout(applyPageSize, 40);
        setTimeout(applyPageSize, 300);
        setTimeout(applyPageSize, 850);
    });
})();
