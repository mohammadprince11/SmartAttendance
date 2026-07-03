(function () {
    const STORAGE_LANGUAGE = "SmartAttendance.Language";
    const STORAGE_THEME = "SmartAttendance.Theme";

    const ar = {
        enterprise: "\u0646\u0638\u0627\u0645 \u0627\u0644\u0645\u0648\u0627\u0631\u062f \u0627\u0644\u0628\u0634\u0631\u064a\u0629",
        dashboard: "\u0644\u0648\u062d\u0629 \u0627\u0644\u062a\u062d\u0643\u0645",
        organization: "\u0627\u0644\u0647\u064a\u0643\u0644 \u0627\u0644\u062a\u0646\u0638\u064a\u0645\u064a",
        hrAffairs: "\u0634\u0624\u0648\u0646 \u0627\u0644\u0645\u0648\u0638\u0641\u064a\u0646",
        employees: "\u0627\u0644\u0645\u0648\u0638\u0641\u0648\u0646",
        hrReports: "\u062a\u0642\u0627\u0631\u064a\u0631 \u0634\u0624\u0648\u0646 \u0627\u0644\u0645\u0648\u0638\u0641\u064a\u0646",
        attendanceModule: "\u0627\u0644\u062d\u0636\u0648\u0631 \u0648\u0627\u0644\u0627\u0646\u0635\u0631\u0627\u0641",
        devices: "\u0627\u0644\u0623\u062c\u0647\u0632\u0629",
        shifts: "\u0627\u0644\u0634\u0641\u062a\u0627\u062a",
        employeeShifts: "\u0634\u0641\u062a\u0627\u062a \u0627\u0644\u0645\u0648\u0638\u0641\u064a\u0646",
        attendance: "\u0633\u062c\u0644\u0627\u062a \u0627\u0644\u062d\u0636\u0648\u0631",
        attendanceProcessing: "\u0645\u0639\u0627\u0644\u062c\u0629 \u0627\u0644\u062d\u0636\u0648\u0631",
        attendanceOperations: "\u0639\u0645\u0644\u064a\u0627\u062a \u0627\u0644\u062d\u0636\u0648\u0631",
        attendanceCorrections: "\u062a\u0635\u062d\u064a\u062d \u0627\u0644\u062d\u0636\u0648\u0631",
        attendanceImport: "\u0627\u0633\u062a\u064a\u0631\u0627\u062f \u0627\u0644\u062d\u0636\u0648\u0631",
        attendanceReports: "\u062a\u0642\u0627\u0631\u064a\u0631 \u0627\u0644\u062d\u0636\u0648\u0631 \u0648\u0627\u0644\u0627\u0646\u0635\u0631\u0627\u0641",
        settingsModule: "\u0627\u0644\u0625\u0639\u062f\u0627\u062f\u0627\u062a",
        organizationSettings: "\u0625\u0639\u062f\u0627\u062f\u0627\u062a \u0627\u0644\u0647\u064a\u0643\u0644 \u0627\u0644\u062a\u0646\u0638\u064a\u0645\u064a",
        systemUsers: "\u0645\u0633\u062a\u062e\u062f\u0645\u064a \u0627\u0644\u0646\u0638\u0627\u0645",
        holidays: "\u0627\u0644\u0639\u0637\u0644 \u0627\u0644\u0631\u0633\u0645\u064a\u0629",
        systemPermissions: "\u0635\u0644\u0627\u062d\u064a\u0627\u062a \u0627\u0644\u0646\u0638\u0627\u0645",
        approvals: "\u0627\u0644\u0645\u0648\u0627\u0641\u0642\u0627\u062a",
        auditLogs: "\u0633\u062c\u0644 \u0627\u0644\u0639\u0645\u0644\u064a\u0627\u062a",
        notifications: "\u0627\u0644\u0625\u0634\u0639\u0627\u0631\u0627\u062a",
        maintenance: "\u0627\u0644\u0635\u064a\u0627\u0646\u0629 \u0648\u0627\u0644\u0646\u0633\u062e",
        settings: "\u0627\u0644\u0625\u0639\u062f\u0627\u062f\u0627\u062a",
        selfServices: "\u0627\u0644\u062e\u062f\u0645\u0627\u062a \u0627\u0644\u0630\u0627\u062a\u064a\u0629",
        myPage: "\u0635\u0641\u062d\u062a\u064a",
        leaveRequests: "\u0627\u0644\u0625\u062c\u0627\u0632\u0627\u062a",
        themeLight: "\u0641\u0627\u062a\u062d",
        themeDark: "\u062f\u0627\u0643\u0646",
        logout: "\u062e\u0631\u0648\u062c",
        login: "\u062f\u062e\u0648\u0644"
    };

    const en = {
        enterprise: "Human Resources System",
        dashboard: "Dashboard",
        organization: "Organization",
        hrAffairs: "HR Affairs",
        employees: "Employees",
        hrReports: "HR Reports",
        attendanceModule: "Attendance & Time",
        devices: "Devices",
        shifts: "Shifts",
        employeeShifts: "Employee Shifts",
        attendance: "Attendance Records",
        attendanceProcessing: "Attendance Processing",
        attendanceOperations: "Attendance Operations",
        attendanceCorrections: "Attendance Corrections",
        attendanceImport: "Attendance Import",
        attendanceReports: "Attendance Reports",
        settingsModule: "Settings",
        organizationSettings: "Organization Settings",
        systemUsers: "System Users",
        holidays: "Holidays",
        systemPermissions: "System Permissions",
        approvals: "Approvals",
        auditLogs: "Audit Logs",
        notifications: "Notifications",
        maintenance: "Maintenance & Backup",
        settings: "Settings",
        selfServices: "Self Services",
        myPage: "My Page",
        leaveRequests: "Leave Requests",
        themeLight: "Light",
        themeDark: "Dark",
        logout: "Logout",
        login: "Login"
    };

    function getLanguage() {
        return localStorage.getItem(STORAGE_LANGUAGE) === "en" ? "en" : "ar";
    }

    function getTheme() {
        return localStorage.getItem(STORAGE_THEME) === "light" ? "light" : "dark";
    }

    function ready() {
        document.documentElement.classList.remove("sa-preload");
        document.documentElement.classList.add("sa-ready");
    }

    function applyTheme(theme) {
        document.documentElement.setAttribute("data-theme", theme);
        document.querySelectorAll("[data-theme-button]").forEach(function (btn) {
            btn.classList.toggle("active", btn.getAttribute("data-theme-button") === theme);
        });
    }

    function applyLanguage(language) {
        const dict = language === "en" ? en : ar;

        document.documentElement.setAttribute("lang", language);
        document.documentElement.setAttribute("dir", language === "ar" ? "rtl" : "ltr");
        if (document.body) document.body.setAttribute("dir", language === "ar" ? "rtl" : "ltr");

        document.querySelectorAll("[data-i18n]").forEach(function (el) {
            const key = el.getAttribute("data-i18n");
            if (dict[key]) el.textContent = dict[key];
        });

        document.querySelectorAll("[data-language-button='en']").forEach(function (x) {
            x.textContent = "EN";
            x.classList.toggle("active", language === "en");
        });

        document.querySelectorAll("[data-language-button='ar']").forEach(function (x) {
            x.textContent = "AR";
            x.classList.toggle("active", language === "ar");
        });

        ready();
    }

    function bind() {
        document.querySelectorAll("[data-language-button]").forEach(function (button) {
            button.onclick = function () {
                const lang = button.getAttribute("data-language-button") === "en" ? "en" : "ar";
                localStorage.setItem(STORAGE_LANGUAGE, lang);
                applyLanguage(lang);
            };
        });

        document.querySelectorAll("[data-theme-button]").forEach(function (button) {
            button.onclick = function () {
                const theme = button.getAttribute("data-theme-button") === "light" ? "light" : "dark";
                localStorage.setItem(STORAGE_THEME, theme);
                applyTheme(theme);
            };
        });
    }

    function init() {
        if (!localStorage.getItem(STORAGE_LANGUAGE)) localStorage.setItem(STORAGE_LANGUAGE, "ar");
        if (!localStorage.getItem(STORAGE_THEME)) localStorage.setItem(STORAGE_THEME, "dark");

        applyTheme(getTheme());
        applyLanguage(getLanguage());
        bind();
        ready();
        setTimeout(ready, 100);
        setTimeout(function () { applyLanguage(getLanguage()); bind(); ready(); }, 500);
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", init);
    } else {
        init();
    }

    setTimeout(ready, 50);
    setTimeout(ready, 500);
})();

