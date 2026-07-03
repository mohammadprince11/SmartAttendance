(function () {
    const storageKey = "SmartAttendance.CustomReports.v1";

    const dictionary = {
        ar: {
            reports: "تقارير",
            reportsSubtitle: "إدارة تقارير النظام والتقارير الخاصة والتقارير المشاركة.",
            createReport: "إنشاء تقرير",
            systemReports: "تقارير النظام",
            systemReportsHint: "قوالب جاهزة تفتح بصفحة مستقلة. يمكن فلترتها وتصديرها Excel.",
            hrReportsHint: "تقارير شؤون الموظفين فقط.",
            attendanceReportsHint: "تقارير الحضور والانصراف فقط.",
            myReports: "تقاريري",
            myReportsHint: "التقارير التي تنشئها بنفسك تظهر هنا ككروت مستقلة.",
            sharedReports: "تقارير تمت مشاركتها معي",
            sharedReportsHint: "هنا تظهر التقارير التي يشاركها الأدمن أو مستخدم آخر معك لاحقاً.",
            searchSystemReports: "ابحث في تقارير النظام...",
            openReport: "فتح التقرير",
            noMyReports: "لا توجد تقارير خاصة حالياً.",
            createFirstReport: "اضغط إنشاء تقرير حتى تختار الأعمدة وتحفظ التقرير الخاص بك.",
            noSharedReports: "لا توجد تقارير مشاركة حالياً.",
            sharedLater: "نربط هذه الصفحة بالصلاحيات والمشاركة في مرحلة لاحقة.",
            delete: "حذف",
            employeeFile: "ملف الموظفين",
            attendanceRecords: "الحضور والانصراف",
            standaloneReportHint: "تقرير مستقل مع فلاتر وتصدير Excel.",
            records: "سجل",
            backToReports: "الرجوع للتقارير",
            exportExcel: "تصدير Excel",
            search: "بحث",
            branch: "الفرع",
            department: "القسم",
            fromDate: "من تاريخ",
            toDate: "إلى تاريخ",
            rowsPerPage: "عدد الصفوف",
            all: "الكل",
            apply: "تطبيق",
            clear: "مسح",
            displayDataOnly: "عرض البيانات",
            of: "من",
            previous: "السابق",
            next: "التالي",
            noData: "لا توجد بيانات."
        },
        en: {
            reports: "Reports",
            reportsSubtitle: "Manage system reports, private reports, and shared reports.",
            createReport: "Create Report",
            systemReports: "System Reports",
            systemReportsHint: "Ready templates open in a standalone page. Filter and export to Excel.",
            hrReportsHint: "HR reports only.",
            attendanceReportsHint: "Attendance reports only.",
            myReports: "My Reports",
            myReportsHint: "Reports you create appear here as standalone cards.",
            sharedReports: "Shared With Me",
            sharedReportsHint: "Reports shared by an admin or another user will appear here later.",
            searchSystemReports: "Search system reports...",
            openReport: "Open Report",
            noMyReports: "No private reports yet.",
            createFirstReport: "Create a report, choose columns, and save it to your reports.",
            noSharedReports: "No shared reports yet.",
            sharedLater: "We will connect this page with permissions and sharing later.",
            delete: "Delete",
            employeeFile: "Employee File",
            attendanceRecords: "Attendance Records",
            standaloneReportHint: "Standalone report with filters and Excel export.",
            records: "records",
            backToReports: "Back to Reports",
            exportExcel: "Export Excel",
            search: "Search",
            branch: "Branch",
            department: "Department",
            fromDate: "From Date",
            toDate: "To Date",
            rowsPerPage: "Rows",
            all: "All",
            apply: "Apply",
            clear: "Clear",
            displayDataOnly: "Display Data",
            of: "of",
            previous: "Previous",
            next: "Next",
            noData: "No data."
        }
    };

    function lang() { return document.documentElement.getAttribute("lang") === "ar" ? "ar" : "en"; }
    function t(key) { return dictionary[lang()][key] || key; }

    function translateReportsPage() {
        document.querySelectorAll("[data-reports-text]").forEach(function (el) {
            const key = el.getAttribute("data-reports-text");
            if (dictionary[lang()][key]) el.textContent = dictionary[lang()][key];
        });
        document.querySelectorAll("[data-reports-placeholder]").forEach(function (el) {
            const key = el.getAttribute("data-reports-placeholder");
            if (dictionary[lang()][key]) el.placeholder = dictionary[lang()][key];
        });
    }

    function wireTabs() {
        document.querySelectorAll("[data-report-tab]").forEach(function (btn) {
            btn.addEventListener("click", function () {
                const tab = btn.getAttribute("data-report-tab");
                document.querySelectorAll("[data-report-tab]").forEach(x => x.classList.toggle("active", x.getAttribute("data-report-tab") === tab));
                document.querySelectorAll("[data-report-panel]").forEach(x => x.classList.toggle("active", x.getAttribute("data-report-panel") === tab));
            });
        });
    }

    function wireSystemSearch() {
        const input = document.querySelector("#systemReportSearch");
        if (!input) return;
        input.addEventListener("input", function () {
            const term = (input.value || "").trim().toLowerCase();
            document.querySelectorAll("[data-system-report-card]").forEach(function (card) {
                const value = (card.getAttribute("data-report-search") || card.textContent || "").toLowerCase();
                card.hidden = term && !value.includes(term);
            });
        });
    }

    function getReports() {
        try { return JSON.parse(localStorage.getItem(storageKey) || "[]"); }
        catch { return []; }
    }

    function saveReports(reports) {
        localStorage.setItem(storageKey, JSON.stringify(reports));
    }

    function getPageArea() {
        const area = document.querySelector(".reports-page")?.getAttribute("data-report-area") || "all";
        return area === "attendance" || area === "hr" ? area : "all";
    }

    function renderMyReports() {
        const grid = document.querySelector("#myReportsGrid");
        const empty = document.querySelector("#myReportsEmpty");
        if (!grid) return;

        const area = getPageArea();
        let reports = getReports();
        if (area === "hr") reports = reports.filter(x => x.source === "employees");
        if (area === "attendance") reports = reports.filter(x => x.source === "attendance");

        grid.innerHTML = "";
        if (!reports.length) {
            if (empty) empty.style.display = "grid";
            return;
        }
        if (empty) empty.style.display = "none";

        reports.forEach(function (report) {
            const card = document.createElement("article");
            card.className = "report-card my-report-card";
            const sourceLabel = report.source === "attendance" ? t("attendanceRecords") : t("employeeFile");
            const url = `/Reports/View?Source=${encodeURIComponent(report.source)}&Fields=${encodeURIComponent(report.fields.join(","))}&Name=${encodeURIComponent(report.name)}&PageSize=25&PageNumber=1`;
            card.innerHTML = `
                <div class="report-card-main">
                    <div class="report-icon">📄</div>
                    <div>
                        <div class="report-title">${escapeHtml(report.name)}</div>
                        <p>${sourceLabel} · ${report.fields.length} fields</p>
                    </div>
                </div>
                <div class="report-card-footer">
                    <span class="report-chip">${sourceLabel}</span>
                    <span style="display:flex; gap:8px; flex-wrap:wrap;">
                        <a class="report-open" href="${url}">${t("openReport")}</a>
                        <button type="button" class="report-delete-button" data-delete-report="${report.id}">${t("delete")}</button>
                    </span>
                </div>`;
            grid.appendChild(card);
        });

        grid.querySelectorAll("[data-delete-report]").forEach(function (button) {
            button.addEventListener("click", function () {
                const id = button.getAttribute("data-delete-report");
                saveReports(getReports().filter(x => x.id !== id));
                renderMyReports();
            });
        });
    }

    function escapeHtml(value) {
        return (value || "").replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;").replaceAll('"', "&quot;").replaceAll("'", "&#039;");
    }

    function init() {
        wireTabs();
        wireSystemSearch();
        renderMyReports();
        translateReportsPage();
        const observer = new MutationObserver(function () { translateReportsPage(); renderMyReports(); });
        observer.observe(document.documentElement, { attributes: true, attributeFilter: ["lang"] });
    }

    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", init);
    else init();
})();
