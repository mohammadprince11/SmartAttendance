
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

    function getCellText(row, index) {
        var cell = row.cells[index];
        return cell ? (cell.innerText || cell.textContent || "") : "";
    }

    function isDataRow(row) {
        if (!row) return false;
        if (row.querySelector(".nxr-empty")) return false;
        if (row.hasAttribute("data-nxr-smooth-empty")) return false;
        if (row.hasAttribute("data-nxr-master-empty")) return false;
        return true;
    }

    function getRowData(row) {
        var person = row.querySelector(".nxr-emp-person");
        var employeeText = person ? (person.innerText || person.textContent || "") : getCellText(row, 0);

        var code = getCellText(row, 1);
        var branch = getCellText(row, 2);
        var department = getCellText(row, 3);
        var position = getCellText(row, 4);
        var hireDate = getCellText(row, 5);
        var statusText = getCellText(row, 6);

        var status = normalize(statusText).indexOf("غير") >= 0 ? "inactive" : "active";

        return {
            employee: normalize(employeeText),
            code: normalize(code),
            branch: normalize(branch),
            department: normalize(department),
            position: normalize(position),
            hireDate: normalize(hireDate),
            statusText: normalize(statusText),
            status: status,
            searchText: normalize([employeeText, code, branch, department, position, hireDate, statusText].join(" "))
        };
    }

    ready(function () {
        var form = document.querySelector("form.nxr-emp-filter, .nxr-emp-filter");
        var table = document.querySelector(".nxr-emp-table");

        if (!form || !table || !table.tBodies.length) return;

        document.body.classList.add("nxr-employees-master-filter-active");

        // Disable previous jitter/patch classes.
        document.body.classList.remove(
            "nxr-employees-filter-symmetry",
            "nxr-employees-no-jitter",
            "nxr-employees-filter-polished"
        );

        if (form.dataset.nxrMasterFilterReady === "1") return;
        form.dataset.nxrMasterFilterReady = "1";

        var tbody = table.tBodies[0];
        var rows = Array.from(tbody.querySelectorAll("tr")).filter(isDataRow);

        rows.forEach(function (row) {
            row.__nxrMasterData = getRowData(row);
        });

        // Remove old injected controls.
        document.querySelectorAll(
            ".nxr-emp-page-size-wrap, .nxr-filter-auto-hint, .nxr-live-filter-hint, .nxr-master-empty-wrap"
        ).forEach(function (el) {
            el.remove();
        });

        // Collect original fields.
        var searchInput = form.querySelector('input[name="SearchTerm"]');
        var branchSelect = form.querySelector('select[name="BranchFilter"]');
        var departmentSelect = form.querySelector('select[name="DepartmentFilter"]');
        var statusSelect = form.querySelector('select[name="StatusFilter"]');
        var sortSelect = form.querySelector('select[name="SortBy"]');

        var fields = [searchInput, branchSelect, departmentSelect, statusSelect, sortSelect]
            .map(function (control) { return control ? control.closest(".nxr-field") || control.parentElement : null; })
            .filter(Boolean);

        // Build a new clean structure.
        var masterGrid = document.createElement("div");
        masterGrid.className = "nxr-emp-master-grid";

        fields.forEach(function (field) {
            masterGrid.appendChild(field);
        });

        var toolbar = document.createElement("div");
        toolbar.className = "nxr-emp-master-toolbar";
        toolbar.innerHTML =
            '<div class="nxr-emp-master-summary">' +
                '<span class="nxr-emp-master-counter" data-nxr-master-counter>عرض 25</span>' +
                '<span class="nxr-emp-master-total" data-nxr-master-total>الإجمالي 0</span>' +
            '</div>' +
            '<div class="nxr-emp-master-actions">' +
                '<label class="nxr-emp-master-page-size">' +
                    '<span>عدد الأسطر</span>' +
                    '<select data-nxr-master-page-size aria-label="عدد الأسطر">' +
                        '<option value="10">10</option>' +
                        '<option value="25">25</option>' +
                        '<option value="50">50</option>' +
                        '<option value="100">100</option>' +
                        '<option value="all">الكل</option>' +
                    '</select>' +
                '</label>' +
                '<button type="button" class="nxr-emp-master-reset" data-nxr-master-reset>\u0645\u0633\u062d \u0627\u0644\u0641\u0644\u0627\u062a\u0631</button>' +
            '</div>';

        form.innerHTML = "";
        form.appendChild(masterGrid);
        form.appendChild(toolbar);

        var pageSizeSelect = form.querySelector("[data-nxr-master-page-size]");
        var resetButton = form.querySelector("[data-nxr-master-reset]");
        var counter = form.querySelector("[data-nxr-master-counter]");
        var totalBadge = form.querySelector("[data-nxr-master-total]");

        var savedPageSize = localStorage.getItem("NEXORA.EmployeeList.PageSize") || "25";
        if (["10", "25", "50", "100", "all"].indexOf(savedPageSize) < 0) savedPageSize = "25";
        pageSizeSelect.value = savedPageSize;

        var emptyRow = tbody.querySelector("tr[data-nxr-master-empty]");
        if (!emptyRow) {
            emptyRow = document.createElement("tr");
            emptyRow.setAttribute("data-nxr-master-empty", "1");
            emptyRow.className = "nxr-master-row-hidden";
            emptyRow.innerHTML = '<td colspan="8" class="nxr-master-empty">لا توجد نتائج مطابقة للبحث أو الفلترة الحالية.</td>';
            tbody.appendChild(emptyRow);
        }

        function getValue(control) {
            return normalize(control ? control.value : "");
        }

        function getVisibleBeforePage() {
            return rows.filter(function (row) {
                return !row.classList.contains("nxr-master-row-hidden");
            });
        }

        function sortRows(visibleRows) {
            var sortBy = getValue(sortSelect) || "name";

            function getter(row) {
                var d = row.__nxrMasterData || getRowData(row);

                if (sortBy === "code") return d.code;
                if (sortBy === "branch") return d.branch + " " + d.employee;
                if (sortBy === "department") return d.department + " " + d.employee;
                if (sortBy === "hiredate") return d.hireDate;
                if (sortBy === "status") return d.status + " " + d.employee;

                return d.employee;
            }

            visibleRows.sort(function (a, b) {
                return getter(a).localeCompare(getter(b), "ar", { numeric: true, sensitivity: "base" });
            });

            visibleRows.forEach(function (row) {
                tbody.insertBefore(row, emptyRow);
            });
        }

        function applyMasterFilter() {
            var search = getValue(searchInput);
            var branch = getValue(branchSelect);
            var department = getValue(departmentSelect);
            var status = getValue(statusSelect);

            rows.forEach(function (row) {
                row.classList.remove(
                    "nxr-row-hidden",
                    "nxr-page-row-hidden",
                    "nxr-master-row-hidden"
                );
            });

            var visible = [];

            rows.forEach(function (row) {
                var d = row.__nxrMasterData || getRowData(row);
                var ok = true;

                if (search && d.searchText.indexOf(search) < 0) ok = false;
                if (branch && d.branch !== branch) ok = false;
                if (department && d.department !== department) ok = false;
                if (status && d.status !== status) ok = false;

                if (!ok) {
                    row.classList.add("nxr-master-row-hidden");
                } else {
                    visible.push(row);
                }
            });

            sortRows(visible);

            var value = pageSizeSelect.value;
            var size = value === "all" ? visible.length : parseInt(value, 10);
            if (!Number.isFinite(size) || size <= 0) size = 25;

            visible.forEach(function (row, index) {
                if (index >= size) {
                    row.classList.add("nxr-page-row-hidden");
                }
            });

            var shown = Math.min(size, visible.length);

            emptyRow.classList.toggle("nxr-master-row-hidden", visible.length !== 0);

            if (counter) counter.textContent = "عرض " + shown + " من " + visible.length;
            if (totalBadge) totalBadge.textContent = "الإجمالي " + rows.length;

            document.querySelectorAll(".nxr-emp-count").forEach(function (el) {
                el.textContent = "عرض " + shown + " من " + visible.length;
            });

            var listText = document.querySelector(".nxr-emp-list-head p");
            if (listText) {
                listText.textContent = "عرض " + shown + " من " + visible.length + " حسب الفلترة الحالية.";
            }
        }

        [searchInput, branchSelect, departmentSelect, statusSelect, sortSelect, pageSizeSelect].forEach(function (control) {
            if (!control) return;

            control.addEventListener("input", applyMasterFilter);
            control.addEventListener("change", function () {
                if (control === pageSizeSelect) {
                    localStorage.setItem("NEXORA.EmployeeList.PageSize", pageSizeSelect.value);
                }

                applyMasterFilter();
            });
        });

        if (resetButton) {
            resetButton.addEventListener("click", function () {
                if (searchInput) searchInput.value = "";
                if (branchSelect) branchSelect.value = "";
                if (departmentSelect) departmentSelect.value = "";
                if (statusSelect) statusSelect.value = "";
                if (sortSelect) sortSelect.value = "name";
                if (pageSizeSelect) {
                    pageSizeSelect.value = "25";
                    localStorage.setItem("NEXORA.EmployeeList.PageSize", "25");
                }

                applyMasterFilter();

                try {
                    window.history.replaceState({}, "", window.location.origin + window.location.pathname);
                } catch (_) { }
            });
        }

        applyMasterFilter();
    });
})();
