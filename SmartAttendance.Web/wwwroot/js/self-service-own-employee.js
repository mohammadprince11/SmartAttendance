(function () {
    function isEmployeeRole() {
        return window.SA_AUTH &&
            String(window.SA_AUTH.role || "").toLowerCase() === "employee" &&
            String(window.SA_AUTH.employeeId || "").trim() !== "";
    }

    function isSelfServicePath() {
        const path = window.location.pathname.toLowerCase();
        return path.startsWith("/selfservices") || path.startsWith("/leaverequests");
    }

    function lockEmployeeSelects() {
        if (!isEmployeeRole() || !isSelfServicePath()) return;

        const ownEmployeeId = String(window.SA_AUTH.employeeId).trim();

        document.querySelectorAll("select").forEach(function (select) {
            const name = (select.getAttribute("name") || "").toLowerCase();
            const id = (select.getAttribute("id") || "").toLowerCase();
            const label = select.closest(".hrms-field, .form-group, div")?.querySelector("label")?.textContent || "";

            const looksEmployee =
                name.includes("employeeid") ||
                id.includes("employeeid") ||
                label.includes("الموظف") ||
                label.toLowerCase().includes("employee");

            if (!looksEmployee) return;

            let ownText = "";

            Array.from(select.options).forEach(function (option) {
                if (option.value === ownEmployeeId) {
                    ownText = option.textContent || option.value;
                }
            });

            select.innerHTML = "";

            const option = document.createElement("option");
            option.value = ownEmployeeId;
            option.textContent = ownText || "الموظف الحالي";
            option.selected = true;
            select.appendChild(option);

            select.value = ownEmployeeId;
            select.dataset.lockedToCurrentEmployee = "true";

            select.style.pointerEvents = "none";
            select.style.opacity = "0.85";

            const note = document.createElement("small");
            note.textContent = "يتم تقديم الطلب باسمك فقط.";
            note.style.display = "block";
            note.style.marginTop = "5px";
            note.style.color = "var(--sa-muted)";
            note.style.fontWeight = "900";

            if (!select.parentElement.querySelector("[data-own-employee-note]")) {
                note.setAttribute("data-own-employee-note", "true");
                select.parentElement.appendChild(note);
            }
        });

        document.querySelectorAll("input[type='hidden']").forEach(function (input) {
            const name = (input.getAttribute("name") || "").toLowerCase();
            const id = (input.getAttribute("id") || "").toLowerCase();

            if (name.includes("employeeid") || id.includes("employeeid")) {
                input.value = ownEmployeeId;
            }
        });
    }

    function init() {
        lockEmployeeSelects();

        const observer = new MutationObserver(function () {
            window.clearTimeout(window.__ownEmployeeLockTimer);
            window.__ownEmployeeLockTimer = window.setTimeout(lockEmployeeSelects, 80);
        });

        observer.observe(document.body, { childList: true, subtree: true });
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", init);
    } else {
        init();
    }
})();
