
(function () {
    "use strict";

    function ready(fn) {
        if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", fn);
        else fn();
    }

    function isEmployeePortal() {
        return /^\/EmployeePortal/i.test(window.location.pathname || "");
    }

    function findSidebar() {
        var selectors = [
            ".nexora-sidebar",
            ".nx-sidebar",
            ".app-sidebar",
            "aside[class*='sidebar']",
            "nav[class*='sidebar']",
            ".sidebar"
        ];

        for (var i = 0; i < selectors.length; i++) {
            var el = document.querySelector(selectors[i]);
            if (el) return el;
        }

        var candidates = Array.from(document.querySelectorAll("aside, nav, [class*='side'], [class*='Side']"));
        return candidates.find(function (el) {
            var t = (el.textContent || "").replace(/\s+/g, " ").trim();
            return t.indexOf("NEXORA") >= 0 || t.indexOf("أشخاص") >= 0 || t.indexOf("لوحة") >= 0;
        }) || null;
    }

    var items = [
        { tab: "home", title: "الصفحة الرئيسية", sub: "حائط الموظف", icon: "⌂" },
        { tab: "profile", title: "ملفي الشخصي", sub: "بياناتي", icon: "◎" },
        { tab: "compensation", title: "التعويضات", sub: "الراتب والبدلات", icon: "$" },
        { tab: "requests", title: "طلباتي", sub: "الإجازات والموافقات", icon: "+" },
        { tab: "team", title: "فريقي", sub: "الفريق المباشر", icon: "◆" },
        { tab: "attendance", title: "حضوري", sub: "الحضور والانصراف", icon: "◷" },
        { tab: "feedback", title: "الشكاوي والمقترحات", sub: "إرسال ومتابعة", icon: "✎" }
    ];

    function buildShell() {
        var nav = items.map(function (item) {
            return '<a href="/EmployeePortal?tab=' + item.tab + '" class="nx-employee-nav-link" data-employee-portal-tab="' + item.tab + '">' +
                '<span class="nx-employee-nav-icon">' + item.icon + '</span>' +
                '<span class="nx-employee-nav-text"><strong>' + item.title + '</strong><span>' + item.sub + '</span></span>' +
                '<span class="nx-employee-nav-dot"></span>' +
            '</a>';
        }).join("");

        return '<div class="nx-employee-shell-inner">' +
            '<div class="nx-employee-brand">' +
                '<div class="nx-employee-brand-logo">N</div>' +
                '<div class="nx-employee-brand-title"><strong>NEXORA</strong><span>نيكسورا</span><small>Employee Self Service</small></div>' +
            '</div>' +
            '<nav class="nx-employee-nav" aria-label="Employee portal navigation">' +
                '<div class="nx-employee-nav-section">بوابة الموظف</div>' +
                nav +
                '<a href="/" class="nx-employee-nav-link" data-employee-portal-logout="1">' +
                    '<span class="nx-employee-nav-icon">↩</span>' +
                    '<span class="nx-employee-nav-text"><strong>تسجيل الخروج</strong><span>إنهاء جلسة الموظف</span></span>' +
                    '<span class="nx-employee-nav-dot"></span>' +
                '</a>' +
            '</nav>' +
            '<div class="nx-employee-shell-footer">صلاحية العرض الحالية: Employee</div>' +
        '</div>';
    }

    function currentTab() {
        try {
            return new URL(window.location.href).searchParams.get("tab") || localStorage.getItem("NEXORA.EmployeePortal.ActiveTab.DB") || "home";
        } catch (_) {
            return "home";
        }
    }

    function setActive(tab) {
        document.querySelectorAll("[data-employee-portal-tab]").forEach(function (a) {
            a.classList.toggle("active", a.getAttribute("data-employee-portal-tab") === tab);
        });
    }

    function activatePortalTab(tab) {
        if (!tab) tab = "home";

        var portalButton = document.querySelector('[data-nxep-tab="' + tab + '"]');
        if (portalButton) {
            portalButton.click();
        } else {
            try {
                var u = new URL(window.location.href);
                u.searchParams.set("tab", tab);
                history.replaceState(null, "", u.toString());
            } catch (_) {}
        }

        localStorage.setItem("NEXORA.EmployeePortal.ActiveTab.DB", tab);
        setActive(tab);
    }

    ready(function () {
        if (!isEmployeePortal()) return;

        document.body.classList.add("nx-employee-portal-shell");

        var sidebar = findSidebar();
        if (!sidebar) {
            sidebar = document.createElement("aside");
            sidebar.className = "nexora-sidebar nx-employee-shell nx-employee-shell-floating";
            document.body.appendChild(sidebar);
        }

        sidebar.classList.add("nx-employee-shell");
        sidebar.innerHTML = buildShell();

        sidebar.addEventListener("click", function (event) {
            var link = event.target.closest("[data-employee-portal-tab]");
            if (!link) return;

            event.preventDefault();
            event.stopPropagation();

            activatePortalTab(link.getAttribute("data-employee-portal-tab"));
        });

        var tab = currentTab();
        activatePortalTab(tab);

        window.addEventListener("nexora:employee-portal-tab", function (event) {
            if (event && event.detail && event.detail.tab) setActive(event.detail.tab);
        });
    });
})();
