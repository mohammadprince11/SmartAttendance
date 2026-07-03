(function () {
    const storageKey = "SmartAttendance.CustomReports.v1";

    const dictionary = {
        ar: {
            createReport: "إنشاء تقرير",
            createReportSubtitle: "اختر مصدر التقرير، الأعمدة، الترتيب، ثم احفظه ضمن تقاريري.",
            backToReports: "الرجوع للتقارير",
            reportSettings: "إعدادات التقرير",
            reportName: "اسم التقرير",
            reportNamePlaceholder: "مثال: تقرير موظفي HQ",
            reportSource: "مصدر التقرير",
            employeeFile: "ملف الموظفين",
            attendanceRecords: "الحضور والانصراف",
            quickFilters: "فلاتر اختيارية",
            generalSearch: "بحث...",
            saveReport: "حفظ التقرير",
            preview: "معاينة",
            chooseColumns: "اختيار الأعمدة",
            chooseColumnsHint: "اضغط على الحقول لإضافتها إلى التقرير، ويمكنك ترتيبها.",
            availableFields: "الحقول المتاحة",
            selectedFields: "أعمدة التقرير",
            searchFields: "بحث...",
            clear: "مسح",
            selectedFieldsEmpty: "لم تختر أي عمود بعد.",
            nameRequired: "اكتب اسم التقرير أولاً.",
            fieldsRequired: "اختر عمود واحد على الأقل.",
            duplicateField: "هذا الحقل مضاف مسبقاً."
        },
        en: {
            createReport: "Create Report",
            createReportSubtitle: "Choose report source, columns, order, then save it under My Reports.",
            backToReports: "Back to Reports",
            reportSettings: "Report Settings",
            reportName: "Report Name",
            reportNamePlaceholder: "Example: HQ Employees Report",
            reportSource: "Report Source",
            employeeFile: "Employee File",
            attendanceRecords: "Attendance Records",
            quickFilters: "Optional Filters",
            generalSearch: "Search...",
            saveReport: "Save Report",
            preview: "Preview",
            chooseColumns: "Choose Columns",
            chooseColumnsHint: "Click fields to add them to your report and arrange their order.",
            availableFields: "Available Fields",
            selectedFields: "Report Columns",
            searchFields: "Search...",
            clear: "Clear",
            selectedFieldsEmpty: "No columns selected yet.",
            nameRequired: "Write the report name first.",
            fieldsRequired: "Select at least one column.",
            duplicateField: "This field is already added."
        }
    };

    let selectedFields = [];

    function lang() {
        return document.documentElement.getAttribute("lang") === "ar" ? "ar" : "en";
    }

    function t(key) {
        return dictionary[lang()][key] || key;
    }

    function translateReportsPage() {
        document.querySelectorAll("[data-reports-text]").forEach(function (element) {
            const key = element.getAttribute("data-reports-text");

            if (dictionary[lang()][key]) {
                element.textContent = dictionary[lang()][key];
            }
        });

        document.querySelectorAll("[data-reports-placeholder]").forEach(function (element) {
            const key = element.getAttribute("data-reports-placeholder");

            if (dictionary[lang()][key]) {
                element.placeholder = dictionary[lang()][key];
            }
        });
    }

    function getCurrentSource() {
        return document.querySelector("#reportSource")?.value || "employees";
    }

    function filterAvailableFields() {
        const source = getCurrentSource();
        const term = (document.querySelector("#availableFieldSearch")?.value || "").trim().toLowerCase();

        document.querySelectorAll(".field-pill").forEach(function (button) {
            const buttonSource = button.getAttribute("data-source");
            const textValue = (button.textContent || "").toLowerCase();

            const sourceMatch = buttonSource === source;
            const searchMatch = !term || textValue.includes(term);

            button.hidden = !sourceMatch || !searchMatch;
        });
    }

    function addField(button) {
        const key = button.getAttribute("data-field-key");
        const labelAr = button.getAttribute("data-label-ar");
        const labelEn = button.getAttribute("data-label-en");

        if (!key) return;

        if (selectedFields.some(field => field.key === key)) {
            alert(t("duplicateField"));
            return;
        }

        selectedFields.push({ key, labelAr, labelEn });
        renderSelectedFields();
    }

    function renderSelectedFields() {
        const list = document.querySelector("#selectedFields");
        const empty = document.querySelector("#selectedFieldsEmpty");

        if (!list) return;

        list.innerHTML = "";

        if (!selectedFields.length) {
            if (empty) empty.style.display = "block";
            return;
        }

        if (empty) empty.style.display = "none";

        selectedFields.forEach(function (field, index) {
            const item = document.createElement("div");
            item.className = "selected-field";

            const label = lang() === "ar" ? field.labelAr : field.labelEn;

            item.innerHTML = `
                <span class="field-order-number">${index + 1}</span>
                <span>${escapeHtml(label)}</span>
                <div class="field-actions-mini">
                    <button type="button" data-move-up="${index}">↑</button>
                    <button type="button" data-move-down="${index}">↓</button>
                    <button type="button" data-remove-field="${index}">×</button>
                </div>
            `;

            list.appendChild(item);
        });

        list.querySelectorAll("[data-move-up]").forEach(function (button) {
            button.addEventListener("click", function () {
                const index = Number(button.getAttribute("data-move-up"));

                if (index <= 0) return;

                [selectedFields[index - 1], selectedFields[index]] = [selectedFields[index], selectedFields[index - 1]];
                renderSelectedFields();
            });
        });

        list.querySelectorAll("[data-move-down]").forEach(function (button) {
            button.addEventListener("click", function () {
                const index = Number(button.getAttribute("data-move-down"));

                if (index >= selectedFields.length - 1) return;

                [selectedFields[index + 1], selectedFields[index]] = [selectedFields[index], selectedFields[index + 1]];
                renderSelectedFields();
            });
        });

        list.querySelectorAll("[data-remove-field]").forEach(function (button) {
            button.addEventListener("click", function () {
                const index = Number(button.getAttribute("data-remove-field"));
                selectedFields.splice(index, 1);
                renderSelectedFields();
            });
        });
    }

    function getReports() {
        try {
            return JSON.parse(localStorage.getItem(storageKey) || "[]");
        } catch {
            return [];
        }
    }

    function saveReports(reports) {
        localStorage.setItem(storageKey, JSON.stringify(reports));
    }

    function getReportPayload() {
        const name = (document.querySelector("#reportName")?.value || "").trim();
        const source = getCurrentSource();
        const fields = selectedFields.map(field => field.key);

        return {
            id: crypto.randomUUID ? crypto.randomUUID() : String(Date.now()),
            name,
            source,
            fields,
            search: document.querySelector("#reportSearch")?.value || "",
            fromDate: document.querySelector("#fromDate")?.value || "",
            toDate: document.querySelector("#toDate")?.value || "",
            createdAt: new Date().toISOString()
        };
    }

    function validate(payload) {
        if (!payload.name) {
            alert(t("nameRequired"));
            return false;
        }

        if (!payload.fields.length) {
            alert(t("fieldsRequired"));
            return false;
        }

        return true;
    }

    function buildViewUrl(payload) {
        const params = new URLSearchParams();
        params.set("Source", payload.source);
        params.set("Fields", payload.fields.join(","));
        params.set("Name", payload.name);

        if (payload.search) params.set("Search", payload.search);
        if (payload.fromDate) params.set("FromDate", payload.fromDate);
        if (payload.toDate) params.set("ToDate", payload.toDate);

        return `/Reports/View?${params.toString()}`;
    }

    function saveReport() {
        const payload = getReportPayload();

        if (!validate(payload)) return;

        const reports = getReports();
        reports.unshift(payload);
        saveReports(reports);

        window.location.href = buildViewUrl(payload);
    }

    function previewReport() {
        const payload = getReportPayload();

        if (!validate(payload)) return;

        window.location.href = buildViewUrl(payload);
    }

    function resetDefaultsForSource() {
        const source = getCurrentSource();

        selectedFields = [];

        const defaults = source === "attendance"
            ? ["EmployeeCode", "EmployeeName", "Date", "CheckIn", "CheckOut", "Status"]
            : ["EmployeeCode", "EmployeeName", "Branch", "Department", "HireDate", "Active"];

        defaults.forEach(function (key) {
            const button = document.querySelector(`.field-pill[data-source="${source}"][data-field-key="${key}"]`);

            if (button) {
                selectedFields.push({
                    key,
                    labelAr: button.getAttribute("data-label-ar"),
                    labelEn: button.getAttribute("data-label-en")
                });
            }
        });

        renderSelectedFields();
    }

    function escapeHtml(value) {
        return (value || "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#039;");
    }

    function init() {
        translateReportsPage();
        filterAvailableFields();
        resetDefaultsForSource();

        document.querySelector("#reportSource")?.addEventListener("change", function () {
            filterAvailableFields();
            resetDefaultsForSource();
        });

        document.querySelector("#availableFieldSearch")?.addEventListener("input", filterAvailableFields);

        document.querySelectorAll(".field-pill").forEach(function (button) {
            button.addEventListener("click", function () {
                addField(button);
            });
        });

        document.querySelector("#clearFieldsButton")?.addEventListener("click", function () {
            selectedFields = [];
            renderSelectedFields();
        });

        document.querySelector("#saveReportButton")?.addEventListener("click", saveReport);
        document.querySelector("#previewReportButton")?.addEventListener("click", previewReport);

        const observer = new MutationObserver(function () {
            translateReportsPage();
            renderSelectedFields();
        });

        observer.observe(document.documentElement, {
            attributes: true,
            attributeFilter: ["lang"]
        });
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", init);
    } else {
        init();
    }
})();
