(function () {
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

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initDepartmentToggles);
    } else {
        initDepartmentToggles();
    }
})();
