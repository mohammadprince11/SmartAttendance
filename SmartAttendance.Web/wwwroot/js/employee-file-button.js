(function () {
    function isEmployeesPage() {
        const path = window.location.pathname.toLowerCase();
        return path === "/employees" || path === "/employees/index" || path.endsWith("/employees");
    }

    function normalize(text) {
        return (text || "").trim();
    }

    function looksLikeEmployeeNo(value) {
        return /^[0-9]{3,10}$/.test(normalize(value));
    }

    function findEmployeeNo(row) {
        const cells = Array.from(row.querySelectorAll("td"));

        for (const cell of cells) {
            const text = normalize(cell.textContent);

            if (looksLikeEmployeeNo(text)) {
                return text;
            }

            const match = text.match(/\b[0-9]{4,10}\b/);
            if (match) {
                return match[0];
            }
        }

        return "";
    }

    function isDeleteElement(element) {
        const text = normalize(element.textContent).toLowerCase();
        const href = (element.getAttribute("href") || "").toLowerCase();
        const action = (element.getAttribute("formaction") || "").toLowerCase();

        return text === "حذف" ||
            text === "delete" ||
            href.includes("/delete") ||
            action.includes("/delete") ||
            element.className.toString().toLowerCase().includes("delete");
    }

    function isEditElement(element) {
        const text = normalize(element.textContent).toLowerCase();
        const href = (element.getAttribute("href") || "").toLowerCase();
        const action = (element.getAttribute("formaction") || "").toLowerCase();

        return text === "تعديل" ||
            text === "edit" ||
            href.includes("/edit") ||
            action.includes("/edit") ||
            element.className.toString().toLowerCase().includes("edit");
    }

    function cleanEmployeeActions() {
        if (!isEmployeesPage()) return;

        document.querySelectorAll("table tbody tr").forEach(function (row) {
            const employeeNo = findEmployeeNo(row);
            if (!employeeNo) return;

            row.querySelectorAll("a, button").forEach(function (element) {
                if (element.classList.contains("sa-employee-file-btn")) {
                    element.remove();
                    return;
                }

                if (isDeleteElement(element)) {
                    element.remove();
                    return;
                }

                if (isEditElement(element)) {
                    const link = document.createElement("a");
                    link.className = "btn-table btn-edit sa-edit-employee-file";
                    link.href = "/EmployeeFile?EmployeeNo=" + encodeURIComponent(employeeNo);
                    link.textContent = normalize(element.textContent) === "Edit" ? "Edit" : "تعديل";
                    link.title = "فتح ملف الموظف للتعديل";

                    element.replaceWith(link);
                }
            });
        });
    }

    function init() {
        cleanEmployeeActions();

        const observer = new MutationObserver(function () {
            window.clearTimeout(window.__employeeActionsCleanTimer);
            window.__employeeActionsCleanTimer = window.setTimeout(cleanEmployeeActions, 80);
        });

        observer.observe(document.body, { childList: true, subtree: true });
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", init);
    } else {
        init();
    }
})();
