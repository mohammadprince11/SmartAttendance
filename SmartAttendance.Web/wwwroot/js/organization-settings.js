(function () {
    function byId(id) {
        return document.getElementById(id);
    }

    function boolValue(value) {
        return value === "true" || value === "True";
    }

    function setValue(id, value) {
        const element = byId(id);
        if (!element) return;

        if (element.type === "checkbox") {
            element.checked = boolValue(value);
        } else {
            element.value = value || "";
        }
    }

    function scrollToForm() {
        document.querySelector(".hrms-form-card")?.scrollIntoView({ behavior: "smooth", block: "start" });
    }

    function initCompany() {
        document.querySelectorAll("[data-edit-company]").forEach(button => {
            button.addEventListener("click", () => {
                setValue("companyId", button.dataset.id);
                setValue("companyCode", button.dataset.code);
                setValue("companyName", button.dataset.name);
                setValue("companyIsActive", button.dataset.active);
                scrollToForm();
            });
        });
    }

    function initBranch() {
        document.querySelectorAll("[data-edit-branch]").forEach(button => {
            button.addEventListener("click", () => {
                setValue("branchId", button.dataset.id);
                setValue("branchCompanyId", button.dataset.companyId);
                setValue("branchCode", button.dataset.code);
                setValue("branchName", button.dataset.name);
                setValue("branchAddress", button.dataset.address);
                setValue("branchIsActive", button.dataset.active);
                scrollToForm();
            });
        });
    }

    function initDepartment() {
        document.querySelectorAll("[data-edit-department]").forEach(button => {
            button.addEventListener("click", () => {
                setValue("departmentId", button.dataset.id);
                setValue("departmentBranchId", button.dataset.branchId);
                setValue("departmentCode", button.dataset.code);
                setValue("departmentName", button.dataset.name);
                setValue("departmentIsActive", button.dataset.active);
                scrollToForm();
            });
        });
    }

    function initPosition() {
        document.querySelectorAll("[data-edit-position]").forEach(button => {
            button.addEventListener("click", () => {
                setValue("positionId", button.dataset.id);
                setValue("positionCode", button.dataset.code);
                setValue("positionName", button.dataset.name);
                setValue("positionDescription", button.dataset.description);
                setValue("positionIsActive", button.dataset.active);
                scrollToForm();
            });
        });
    }

    function initClear() {
        document.querySelectorAll("[data-clear-form]").forEach(button => {
            button.addEventListener("click", () => {
                const form = button.closest("form");
                if (!form) return;

                form.reset();

                form.querySelectorAll("input[type='hidden']").forEach(hidden => {
                    hidden.value = "0";
                });

                form.querySelectorAll("input[type='checkbox']").forEach(check => {
                    check.checked = true;
                });
            });
        });
    }

    function init() {
        initCompany();
        initBranch();
        initDepartment();
        initPosition();
        initClear();
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", init);
    } else {
        init();
    }
})();
