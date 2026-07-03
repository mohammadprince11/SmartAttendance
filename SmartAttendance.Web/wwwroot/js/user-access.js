(function () {
    function setValue(id, value) {
        const el = document.getElementById(id);
        if (!el) return;

        if (el.type === "checkbox") {
            el.checked = value === "true";
        } else {
            el.value = value || "";
        }
    }

    function initEdit() {
        document.querySelectorAll("[data-edit-user]").forEach(button => {
            button.addEventListener("click", () => {
                setValue("userId", button.dataset.id);
                setValue("userEmployeeId", button.dataset.employeeId);
                setValue("userUsername", button.dataset.username);
                setValue("userPassword", "");
                setValue("userRole", button.dataset.role);
                setValue("userIsActive", button.dataset.active);

                document.querySelector(".hrms-form-card")?.scrollIntoView({
                    behavior: "smooth",
                    block: "start"
                });
            });
        });
    }

    function initClear() {
        document.querySelector("[data-clear-user]")?.addEventListener("click", () => {
            setValue("userId", "0");
            setValue("userEmployeeId", "");
            setValue("userUsername", "");
            setValue("userPassword", "");
            setValue("userRole", "Employee");
            setValue("userIsActive", "true");
        });
    }

    function init() {
        initEdit();
        initClear();
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", init);
    } else {
        init();
    }
})();
