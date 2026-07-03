(function () {
    function getBackdrop() {
        return document.querySelector("[data-modal-backdrop]");
    }

    function hideElement(element) {
        if (!element) return;
        element.setAttribute("hidden", "hidden");
        element.hidden = true;
    }

    function showElement(element) {
        if (!element) return;
        element.removeAttribute("hidden");
        element.hidden = false;
    }

    function closeModals() {
        document.querySelectorAll(".org-modal").forEach(function (modal) {
            hideElement(modal);
        });

        hideElement(getBackdrop());
        document.body.classList.remove("org-modal-open");
        document.body.style.overflow = "";
    }

    function showModal(id) {
        closeModals();

        const modal = document.getElementById(id);
        const backdrop = getBackdrop();

        if (!modal) return;

        showElement(backdrop);
        showElement(modal);

        document.body.classList.add("org-modal-open");
        document.body.style.overflow = "hidden";

        const firstInput = modal.querySelector("input:not([type='hidden']), select, button");
        if (firstInput) {
            window.setTimeout(function () {
                firstInput.focus();
            }, 80);
        }
    }

    function setSelectValue(selectId, value) {
        const select = document.getElementById(selectId);

        if (select) {
            select.value = value || "";
            select.dispatchEvent(new Event("change"));
        }
    }

    function initCleanState() {
        closeModals();
    }

    function initOpenButtons() {
        document.querySelectorAll("[data-open-modal]").forEach(function (button) {
            button.addEventListener("click", function (event) {
                event.preventDefault();
                event.stopPropagation();
                showModal(button.getAttribute("data-open-modal"));
            });
        });

        document.querySelectorAll("[data-open-branch]").forEach(function (button) {
            button.addEventListener("click", function (event) {
                event.preventDefault();
                event.stopPropagation();

                const companyId = button.getAttribute("data-company-id") || "";
                const companyName = button.getAttribute("data-company-name") || "";

                const hidden = document.getElementById("branchCompanyId");
                const label = document.getElementById("branchCompanyName");

                if (hidden) hidden.value = companyId;
                if (label) label.textContent = companyName ? "الشركة: " + companyName : "اختر الشركة من القائمة.";

                setSelectValue("branchCompanySelect", companyId);
                showModal("branchModal");
            });
        });

        document.querySelectorAll("[data-open-department]").forEach(function (button) {
            button.addEventListener("click", function (event) {
                event.preventDefault();
                event.stopPropagation();

                const branchId = button.getAttribute("data-branch-id") || "";
                const branchName = button.getAttribute("data-branch-name") || "";

                const hidden = document.getElementById("departmentBranchId");
                const label = document.getElementById("departmentBranchName");

                if (hidden) hidden.value = branchId;
                if (label) label.textContent = branchName ? "الفرع: " + branchName : "اختر الفرع من القائمة.";

                setSelectValue("departmentBranchSelect", branchId);
                showModal("departmentModal");
            });
        });
    }

    function initSelectSync() {
        const branchCompanySelect = document.getElementById("branchCompanySelect");
        const branchCompanyId = document.getElementById("branchCompanyId");
        const branchCompanyName = document.getElementById("branchCompanyName");

        if (branchCompanySelect && branchCompanyId) {
            branchCompanySelect.addEventListener("change", function () {
                branchCompanyId.value = branchCompanySelect.value;
                const text = branchCompanySelect.options[branchCompanySelect.selectedIndex]?.text || "";

                if (branchCompanyName) {
                    branchCompanyName.textContent = text && branchCompanySelect.value
                        ? "الشركة: " + text
                        : "اختر الشركة من القائمة.";
                }
            });
        }

        const departmentBranchSelect = document.getElementById("departmentBranchSelect");
        const departmentBranchId = document.getElementById("departmentBranchId");
        const departmentBranchName = document.getElementById("departmentBranchName");

        if (departmentBranchSelect && departmentBranchId) {
            departmentBranchSelect.addEventListener("change", function () {
                departmentBranchId.value = departmentBranchSelect.value;
                const text = departmentBranchSelect.options[departmentBranchSelect.selectedIndex]?.text || "";

                if (departmentBranchName) {
                    departmentBranchName.textContent = text && departmentBranchSelect.value
                        ? "الفرع: " + text
                        : "اختر الفرع من القائمة.";
                }
            });
        }
    }

    function initCloseButtons() {
        document.querySelectorAll("[data-close-modal], .org-modal-close").forEach(function (button) {
            button.addEventListener("click", function (event) {
                event.preventDefault();
                event.stopPropagation();
                closeModals();
            });
        });

        const backdrop = getBackdrop();

        if (backdrop) {
            backdrop.addEventListener("click", function (event) {
                event.preventDefault();
                closeModals();
            });
        }

        document.querySelectorAll(".org-modal").forEach(function (modal) {
            modal.addEventListener("click", function (event) {
                if (event.target === modal) {
                    closeModals();
                }
            });
        });

        document.addEventListener("keydown", function (event) {
            if (event.key === "Escape") {
                closeModals();
            }
        });
    }

    function initDepartmentToggles() {
        document.querySelectorAll("[data-toggle-departments]").forEach(function (button) {
            button.addEventListener("click", function (event) {
                event.preventDefault();

                const row = button.closest(".org-branch");
                const departments = row ? row.nextElementSibling : null;

                if (!departments || !departments.classList.contains("org-departments")) return;

                departments.hidden = !departments.hidden;
                button.classList.toggle("open", !departments.hidden);
            });
        });

        const expandAll = document.querySelector("[data-expand-all]");
        const collapseAll = document.querySelector("[data-collapse-all]");

        if (expandAll) {
            expandAll.addEventListener("click", function (event) {
                event.preventDefault();
                document.querySelectorAll(".org-departments").forEach(function (x) { x.hidden = false; });
                document.querySelectorAll("[data-toggle-departments]").forEach(function (x) { x.classList.add("open"); });
            });
        }

        if (collapseAll) {
            collapseAll.addEventListener("click", function (event) {
                event.preventDefault();
                document.querySelectorAll(".org-departments").forEach(function (x) { x.hidden = true; });
                document.querySelectorAll("[data-toggle-departments]").forEach(function (x) { x.classList.remove("open"); });
            });
        }
    }

    function init() {
        initCleanState();
        initOpenButtons();
        initSelectSync();
        initCloseButtons();
        initDepartmentToggles();
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", init);
    } else {
        init();
    }

    window.SmartAttendanceOrganizationModals = {
        close: closeModals,
        open: showModal
    };
})();
