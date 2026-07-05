
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

    function normalizePath(path) {
        return (path || "").split("?")[0].split("#")[0].replace(/\/+$/, "").toLowerCase() || "/";
    }

    function cleanupSidebar() {
        var sidebar = document.querySelector(".nexora-sidebar");
        if (!sidebar) return;

        sidebar.querySelectorAll("a[href]").forEach(function (link) {
            var href = normalizePath(link.getAttribute("href"));

            if (
                href === "/employees/create" ||
                href === "/employeedocuments" ||
                href === "/employeedocuments/index" ||
                href.indexOf("/employeedocuments") === 0
            ) {
                link.remove();
            }
        });

        var current = normalizePath(window.location.pathname);
        var employeeList =
            sidebar.querySelector('a[href*="/Employees/Index"]') ||
            sidebar.querySelector('a[href*="/employees/index"]') ||
            sidebar.querySelector('a[href$="/Employees"]') ||
            sidebar.querySelector('a[href$="/employees"]');

        if (current.indexOf("/employees") === 0 && employeeList) {
            sidebar.querySelectorAll(".nexora-nav-link").forEach(function (link) {
                link.classList.remove("active", "nexora-active", "is-active");
                link.removeAttribute("aria-current");
            });

            employeeList.classList.add("active", "nexora-active");
            employeeList.setAttribute("aria-current", "page");

            var details = employeeList.closest("details");
            if (details) details.open = true;
        }
    }

    function getCellText(row, index) {
        var cell = row.cells[index];
        return cell ? (cell.innerText || cell.textContent || "") : "";
    }

    function getRowData(row) {
        var person = row.querySelector(".nxr-emp-person");
        var name = person ? (person.innerText || person.textContent || "") : getCellText(row, 0);

        var code = getCellText(row, 1);
        var branch = getCellText(row, 2);
        var department = getCellText(row, 3);
        var position = getCellText(row, 4);
        var hireDate = getCellText(row, 5);
        var statusText = getCellText(row, 6);
        var status = normalize(statusText).indexOf("غير") >= 0 ? "inactive" : "active";

        return {
            name: normalize(name),
            code: normalize(code),
            branch: normalize(branch),
            department: normalize(department),
            position: normalize(position),
            hireDate: normalize(hireDate),
            statusText: normalize(statusText),
            status: status,
            searchText: normalize([name, code, branch, department, position, hireDate, statusText].join(" "))
        };
    }

    function setControlsEnabled(form, enabled) {
        form.querySelectorAll("input, select, button, a").forEach(function (el) {
            if ("disabled" in el) el.disabled = !enabled ? true : false;
            el.style.pointerEvents = "auto";
            el.style.opacity = "1";
        });
    }

    function setupSmoothEmployeeFilter() {
        var form = document.querySelector("form.nxr-emp-filter");
        var table = document.querySelector(".nxr-emp-table");

        if (!form || !table || !table.tBodies.length) return;
        if (form.dataset.nxrControlFix === "1") return;

        form.dataset.nxrControlFix = "1";
        form.classList.remove("is-live-submitting", "is-submitting", "is-smooth-filtering");
        setControlsEnabled(form, true);

        var tbody = table.tBodies[0];
        var searchInput = form.querySelector('input[name="SearchTerm"]');
        var branchSelect = form.querySelector('select[name="BranchFilter"]');
        var departmentSelect = form.querySelector('select[name="DepartmentFilter"]');
        var statusSelect = form.querySelector('select[name="StatusFilter"]');
        var sortSelect = form.querySelector('select[name="SortBy"]');
        var actions = form.querySelector(".nxr-emp-filter-actions") || form;
        var resetLink = actions.querySelector('a[href]');

        var submitButton = form.querySelector('button[type="submit"]');
        if (submitButton) submitButton.style.display = "none";

        var hint = actions.querySelector(".nxr-filter-auto-hint");
        if (!hint) {
            hint = document.createElement("span");
            hint.className = "nxr-filter-auto-hint";
            actions.appendChild(hint);
        }
        hint.textContent = "الفلترة فورية بدون تحديث الصفحة";

        var rows = Array.from(tbody.querySelectorAll("tr")).filter(function (row) {
            return !row.querySelector(".nxr-empty") && !row.hasAttribute("data-nxr-smooth-empty");
        });

        rows.forEach(function (row) {
            row.__nxrData = getRowData(row);
        });

        var emptyRow = tbody.querySelector("tr[data-nxr-smooth-empty]");
        if (!emptyRow) {
            emptyRow = document.createElement("tr");
            emptyRow.setAttribute("data-nxr-smooth-empty", "1");
            emptyRow.className = "nxr-row-hidden";
            emptyRow.innerHTML = '<td colspan="8" class="nxr-empty nxr-smooth-empty">لا توجد نتائج مطابقة للبحث أو الفلترة الحالية.</td>';
            tbody.appendChild(emptyRow);
        }

        function getValue(control) {
            return normalize(control ? control.value : "");
        }

        function sortRows(visibleRows) {
            var sortBy = getValue(sortSelect) || "name";

            function getter(row) {
                var d = row.__nxrData || getRowData(row);
                if (sortBy === "code") return d.code;
                if (sortBy === "branch") return d.branch + " " + d.name;
                if (sortBy === "department") return d.department + " " + d.name;
                if (sortBy === "hiredate") return d.hireDate;
                if (sortBy === "status") return d.status + " " + d.name;
                return d.name;
            }

            visibleRows.sort(function (a, b) {
                return getter(a).localeCompare(getter(b), "ar", { numeric: true, sensitivity: "base" });
            });

            visibleRows.forEach(function (row) {
                tbody.insertBefore(row, emptyRow);
            });
        }

        function updateCount(count) {
            var countBadge = document.querySelector(".nxr-emp-count");
            if (countBadge) countBadge.textContent = count + " نتيجة";

            var listText = document.querySelector(".nxr-emp-list-head p");
            if (listText) {
                listText.textContent = "عرض " + count + " نتيجة حسب الفلترة الحالية.";
            }

            if (hint) {
                hint.textContent = "تم عرض " + count + " نتيجة فورياً";
            }
        }

        var filterTimer = null;

        function applyFilter() {
            clearTimeout(filterTimer);

            // never lock controls
            form.classList.remove("is-live-submitting", "is-submitting");
            setControlsEnabled(form, true);

            filterTimer = setTimeout(function () {
                var search = getValue(searchInput);
                var branch = getValue(branchSelect);
                var department = getValue(departmentSelect);
                var status = getValue(statusSelect);

                var visible = [];

                rows.forEach(function (row) {
                    var d = row.__nxrData || getRowData(row);
                    var ok = true;

                    if (search && d.searchText.indexOf(search) < 0) ok = false;
                    if (branch && d.branch !== branch) ok = false;
                    if (department && d.department !== department) ok = false;
                    if (status && d.status !== status) ok = false;

                    row.classList.toggle("nxr-row-hidden", !ok);
                    if (ok) {
                        row.classList.add("nxr-row-showing");
                        visible.push(row);
                    } else {
                        row.classList.remove("nxr-row-showing");
                    }
                });

                sortRows(visible);
                emptyRow.classList.toggle("nxr-row-hidden", visible.length !== 0);
                updateCount(visible.length);

                setTimeout(function () {
                    rows.forEach(function (row) {
                        row.classList.remove("nxr-row-showing");
                    });
                    form.classList.remove("is-smooth-filtering", "is-live-submitting", "is-submitting");
                    setControlsEnabled(form, true);
                }, 140);
            }, 25);
        }

        [branchSelect, departmentSelect, statusSelect, sortSelect].forEach(function (select) {
            if (!select) return;
            select.addEventListener("change", applyFilter);
        });

        if (searchInput) {
            searchInput.addEventListener("input", applyFilter);
            searchInput.addEventListener("keydown", function (event) {
                if (event.key === "Enter") {
                    event.preventDefault();
                    applyFilter();
                }
            });
        }

        form.addEventListener("submit", function (event) {
            event.preventDefault();
            applyFilter();
        });

        if (resetLink) {
            resetLink.addEventListener("click", function (event) {
                event.preventDefault();

                if (searchInput) searchInput.value = "";
                if (branchSelect) branchSelect.value = "";
                if (departmentSelect) departmentSelect.value = "";
                if (statusSelect) statusSelect.value = "";
                if (sortSelect) sortSelect.value = "name";

                applyFilter();

                try {
                    window.history.replaceState({}, "", window.location.origin + window.location.pathname);
                } catch (_) { }
            });
        }

        applyFilter();
    }

    ready(function () {
        cleanupSidebar();
        setupSmoothEmployeeFilter();

        setTimeout(function () {
            cleanupSidebar();
            setupSmoothEmployeeFilter();
        }, 250);

        setTimeout(function () {
            cleanupSidebar();
            setupSmoothEmployeeFilter();
        }, 800);
    });
})();
