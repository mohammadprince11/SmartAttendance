(() => {
    "use strict";

    document.querySelectorAll("[data-platform-plan-select]").forEach(select => {
        select.addEventListener("change", () => {
            const option = select.selectedOptions[0];
            if (!option || option.dataset.custom === "true") return;

            const form = select.closest("form");
            if (!form) return;

            const setValue = (selector, value) => {
                const input = form.querySelector(selector);
                if (input && value !== undefined) input.value = value;
            };

            setValue("[data-plan-limit='companies']", option.dataset.maxCompanies);
            setValue("[data-plan-limit='employees']", option.dataset.maxEmployees);
            setValue("[data-plan-limit='devices']", option.dataset.maxDevices);

            const modules = new Set((option.dataset.modules || "").split(",").filter(Boolean));
            form.querySelectorAll("[data-plan-module]").forEach(input => {
                input.checked = modules.has(input.value);
            });
        });
    });
})();
