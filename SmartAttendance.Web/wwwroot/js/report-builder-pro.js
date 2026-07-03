(function () {
    const isReportsPage =
        location.pathname.toLowerCase().includes("/reports") ||
        location.pathname.toLowerCase().includes("/reportbuilder");

    if (!isReportsPage) {
        return;
    }

    document.body.classList.add("sa-report-builder-pro");

    const dictionary = {
        ar: {
            quickBuild: "بناء سريع",
            quickBuildDesc: "اختر المصدر والحقول ثم ابنِ التقرير.",
            templates: "القوالب",
            templatesDesc: "احفظ التقارير المتكررة كقوالب.",
            exportExcel: "تصدير Excel",
            exportExcelDesc: "الترتيب الظاهر هو نفس ترتيب التصدير.",
            preview: "معاينة ذكية",
            previewDesc: "راجع النتائج قبل تحميل الملف.",
            emptyTitle: "ابدأ ببناء التقرير",
            emptyDesc: "اختر مصدر التقرير، حدد الحقول، رتب الأعمدة، ثم اضغط Build Report. بعد ذلك يمكنك تحميل Excel.",
            loading: "جاري تجهيز التقرير...",
            buildReport: "Build Report",
            exportXlsx: "Export XLSX",
            employeeFile: "ملف الموظفين",
            attendanceProcessing: "الحضور والمعالجة",
            reportSource: "مصدر التقرير",
            fieldsOrder: "الحقول والترتيب",
            filters: "الفلاتر",
            templatesLabel: "القوالب",
            save: "حفظ",
            load: "تحميل",
            duplicate: "نسخ",
            delete: "حذف",
            selectAll: "تحديد الكل",
            clear: "مسح"
        },
        en: {
            quickBuild: "Quick Build",
            quickBuildDesc: "Choose source, fields, then build.",
            templates: "Templates",
            templatesDesc: "Save repeated reports as templates.",
            exportExcel: "Excel Export",
            exportExcelDesc: "Export follows the same visible order.",
            preview: "Smart Preview",
            previewDesc: "Review results before downloading.",
            emptyTitle: "Start building your report",
            emptyDesc: "Choose a report source, select fields, arrange columns, then click Build Report. After that you can export Excel.",
            loading: "Preparing report...",
            buildReport: "Build Report",
            exportXlsx: "Export XLSX",
            employeeFile: "Employee File",
            attendanceProcessing: "Attendance & Processing",
            reportSource: "Report Source",
            fieldsOrder: "Fields & Order",
            filters: "Filters",
            templatesLabel: "Templates",
            save: "Save",
            load: "Load",
            duplicate: "Duplicate",
            delete: "Delete",
            selectAll: "Select All",
            clear: "Clear"
        }
    };

    function lang() {
        return document.documentElement.getAttribute("lang") === "ar" ? "ar" : "en";
    }

    function t(key) {
        return dictionary[lang()][key] || dictionary.en[key] || key;
    }

    function createIntro() {
        if (document.querySelector(".report-pro-strip")) return;

        const header = document.querySelector(".page-header");
        if (!header) return;

        const strip = document.createElement("div");
        strip.className = "report-pro-strip";
        strip.innerHTML = `
            <div class="report-pro-card">
                <div class="report-pro-card-icon">⚡</div>
                <div><strong data-rb-key="quickBuild">${t("quickBuild")}</strong><span data-rb-key="quickBuildDesc">${t("quickBuildDesc")}</span></div>
            </div>
            <div class="report-pro-card">
                <div class="report-pro-card-icon">📌</div>
                <div><strong data-rb-key="templates">${t("templates")}</strong><span data-rb-key="templatesDesc">${t("templatesDesc")}</span></div>
            </div>
            <div class="report-pro-card">
                <div class="report-pro-card-icon">📤</div>
                <div><strong data-rb-key="exportExcel">${t("exportExcel")}</strong><span data-rb-key="exportExcelDesc">${t("exportExcelDesc")}</span></div>
            </div>
            <div class="report-pro-card">
                <div class="report-pro-card-icon">👁️</div>
                <div><strong data-rb-key="preview">${t("preview")}</strong><span data-rb-key="previewDesc">${t("previewDesc")}</span></div>
            </div>
        `;

        header.insertAdjacentElement("afterend", strip);
    }

    function addStepBadges() {
        const headings = Array.from(document.querySelectorAll("h2, h3, h4"));
        const patterns = [
            { words: ["Report Source", "مصدر التقرير"], step: "1" },
            { words: ["Fields & Order", "الحقول والترتيب"], step: "2" },
            { words: ["Filters", "الفلاتر"], step: "3" },
            { words: ["Templates", "قوالب"], step: "★" }
        ];

        headings.forEach(function (heading) {
            if (heading.querySelector(".report-step-badge")) return;

            const text = heading.textContent.trim();

            const match = patterns.find(function (p) {
                return p.words.some(function (word) {
                    return text.includes(word);
                });
            });

            if (!match) return;

            const badge = document.createElement("span");
            badge.className = "report-step-badge";
            badge.textContent = match.step;
            heading.prepend(badge);
        });
    }

    function enhanceEmptyPreview() {
        const possiblePreviewPanels = Array.from(document.querySelectorAll(
            ".report-preview, .preview-container, .preview-box, .report-panel, .builder-panel, div[class*='preview']"
        ));

        const previewPanel = possiblePreviewPanels.find(function (el) {
            const text = (el.textContent || "").toLowerCase();
            return text.includes("report preview") ||
                   text.includes("معاينة") ||
                   text.includes("load a system template") ||
                   text.includes("build your own report");
        });

        if (!previewPanel) return;

        if (previewPanel.querySelector("table")) return;
        if (previewPanel.querySelector(".report-pro-empty-preview")) return;

        const empty = document.createElement("div");
        empty.className = "report-pro-empty-preview";
        empty.innerHTML = `
            <div>
                <strong data-rb-key="emptyTitle">${t("emptyTitle")}</strong>
                <span data-rb-key="emptyDesc">${t("emptyDesc")}</span>
            </div>
        `;

        previewPanel.appendChild(empty);
    }

    function addLoadingOverlay() {
        if (document.querySelector(".report-pro-loading")) return;

        const overlay = document.createElement("div");
        overlay.className = "report-pro-loading";
        overlay.innerHTML = `
            <div class="report-pro-loading-box">
                <span class="report-pro-spinner"></span>
                <span data-rb-key="loading">${t("loading")}</span>
            </div>
        `;

        document.body.appendChild(overlay);
    }

    function showLoading() {
        const overlay = document.querySelector(".report-pro-loading");
        if (!overlay) return;

        overlay.classList.add("show");
        window.setTimeout(function () {
            overlay.classList.remove("show");
        }, 900);
    }

    function wireActions() {
        addLoadingOverlay();

        document.addEventListener("click", function (event) {
            const target = event.target.closest("button, a, input[type='submit']");
            if (!target) return;

            const text = (target.textContent || target.value || "").toLowerCase();

            if (
                text.includes("build") ||
                text.includes("export") ||
                text.includes("تحميل") ||
                text.includes("تصدير") ||
                text.includes("بناء")
            ) {
                showLoading();
            }
        });
    }

    function translateReportBuilder() {
        const current = lang();

        document.querySelectorAll("[data-rb-key]").forEach(function (el) {
            const key = el.getAttribute("data-rb-key");
            if (dictionary[current][key]) {
                el.textContent = dictionary[current][key];
            }
        });

        const phraseMap = current === "ar" ? {
            "Employee File": "ملف الموظفين",
            "Attendance & Processing": "الحضور والمعالجة",
            "Report Source": "مصدر التقرير",
            "Fields & Order": "الحقول والترتيب",
            "Filters": "الفلاتر",
            "Templates": "القوالب",
            "Save": "حفظ",
            "Load": "تحميل",
            "Duplicate": "نسخ",
            "Delete": "حذف",
            "Select All": "تحديد الكل",
            "Clear": "مسح",
            "Build Report": "بناء التقرير",
            "Export XLSX": "تصدير Excel"
        } : {
            "ملف الموظفين": "Employee File",
            "الحضور والمعالجة": "Attendance & Processing",
            "مصدر التقرير": "Report Source",
            "الحقول والترتيب": "Fields & Order",
            "الفلاتر": "Filters",
            "القوالب": "Templates",
            "حفظ": "Save",
            "تحميل": "Load",
            "نسخ": "Duplicate",
            "حذف": "Delete",
            "تحديد الكل": "Select All",
            "مسح": "Clear",
            "بناء التقرير": "Build Report",
            "تصدير Excel": "Export XLSX"
        };

        document.querySelectorAll("button, a, label, h2, h3, h4, option, span, strong").forEach(function (el) {
            if (el.children.length > 1) return;

            const value = (el.textContent || "").trim();
            if (phraseMap[value]) {
                el.textContent = phraseMap[value];
            }
        });
    }

    function markPanelTypes() {
        document.querySelectorAll(".report-panel, .builder-panel, .template-panel, .card").forEach(function (panel) {
            const text = (panel.textContent || "").toLowerCase();

            if (text.includes("template") || text.includes("قوالب")) {
                panel.classList.add("rb-template-panel");
            }

            if (text.includes("report source") || text.includes("مصدر التقرير")) {
                panel.classList.add("rb-source-panel");
            }

            if (text.includes("fields") || text.includes("الحقول")) {
                panel.classList.add("rb-fields-panel");
            }

            if (text.includes("filters") || text.includes("الفلاتر")) {
                panel.classList.add("rb-filter-panel");
            }
        });
    }

    function init() {
        createIntro();
        addStepBadges();
        enhanceEmptyPreview();
        wireActions();
        markPanelTypes();
        translateReportBuilder();

        const observer = new MutationObserver(function () {
            window.clearTimeout(window.__rbProTimer);
            window.__rbProTimer = window.setTimeout(function () {
                addStepBadges();
                enhanceEmptyPreview();
                markPanelTypes();
                translateReportBuilder();
            }, 120);
        });

        observer.observe(document.body, { childList: true, subtree: true });

        const langObserver = new MutationObserver(translateReportBuilder);
        langObserver.observe(document.documentElement, {
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
