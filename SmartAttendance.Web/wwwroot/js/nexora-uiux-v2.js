/* NEXORA UI/UX REDESIGN V2 */
(function () {
    "use strict";

    const links = [
        { title: "مركز القيادة", subtitle: "Command Center", url: "/Nexora/CommandCenter", key: "C" },
        { title: "شؤون الموظفين", subtitle: "HR Affairs", url: "/HRAffairs", key: "H" },
        { title: "الموظفون", subtitle: "People Center", url: "/Employees", key: "E" },
        { title: "إغلاق الراتب", subtitle: "Payroll Closing", url: "/Nexora/PayrollClosing", key: "P" },
        { title: "مراقبة الحضور", subtitle: "Attendance Control", url: "/Nexora/AttendanceControl", key: "A" },
        { title: "عمليات الحضور", subtitle: "Attendance Operations", url: "/AttendanceOperations", key: "O" },
        { title: "الموافقات", subtitle: "Approvals", url: "/Nexora/ApprovalsCenter", key: "R" },
        { title: "الخدمة الذاتية", subtitle: "Self Service", url: "/Nexora/SelfServiceCenter", key: "S" },
        { title: "المستندات", subtitle: "Documents", url: "/Nexora/DocumentsCenter", key: "D" },
        { title: "التقارير", subtitle: "Reports", url: "/Nexora/ReportsCenter", key: "T" },
        { title: "محرك القواعد", subtitle: "Rules Engine", url: "/Nexora/RulesEngine", key: "G" },
        { title: "التنبيهات", subtitle: "Notifications", url: "/Nexora/NotificationsCenter", key: "N" },
        { title: "الامتثال", subtitle: "Compliance", url: "/Nexora/ComplianceCenter", key: "M" },
        { title: "التدقيق", subtitle: "Audit", url: "/Nexora/AuditCenter", key: "U" },
        { title: "الإعدادات", subtitle: "Settings", url: "/Nexora/SettingsCenter", key: "X" }
    ];

    function normalize(path) {
        return (path || "").toLowerCase().replace(/\/index$/, "").replace(/\/$/, "");
    }

    function setActiveNav() {
        const current = normalize(location.pathname);
        document.querySelectorAll(".nexora-nav-link[href], .nexora-nav-link").forEach(a => {
            const href = a.getAttribute("href");
            if (!href) return;
            const clean = normalize(href);
            if (clean && (current === clean || current.startsWith(clean + "/"))) {
                a.classList.add("nexora-active");
                a.setAttribute("aria-current", "page");
                const group = a.closest("details");
                if (group) group.open = true;
            }
        });
    }

    function createCommandBar() {
        if (document.querySelector(".nxv2-command-fab")) return;

        const fab = document.createElement("button");
        fab.type = "button";
        fab.className = "nxv2-command-fab";
        fab.title = "Quick Command";
        fab.setAttribute("aria-label", "Open NEXORA command bar");
        fab.textContent = "⌘";

        const overlay = document.createElement("div");
        overlay.className = "nxv2-command-overlay";
        overlay.innerHTML = `
            <div class="nxv2-command-panel" role="dialog" aria-label="NEXORA command bar">
                <div class="nxv2-command-head">
                    <input type="search" placeholder="ابحث عن صفحة أو اكتب اختصار..." aria-label="Search pages" />
                    <button type="button" class="nxv2-command-close" aria-label="Close">×</button>
                </div>
                <div class="nxv2-command-list"></div>
            </div>
        `;

        document.body.appendChild(fab);
        document.body.appendChild(overlay);

        const input = overlay.querySelector("input");
        const list = overlay.querySelector(".nxv2-command-list");
        const close = overlay.querySelector(".nxv2-command-close");

        function render(filter) {
            const q = (filter || "").trim().toLowerCase();
            const filtered = links.filter(item =>
                item.title.toLowerCase().includes(q) ||
                item.subtitle.toLowerCase().includes(q) ||
                item.key.toLowerCase().includes(q)
            );

            list.innerHTML = filtered.map(item => `
                <a class="nxv2-command-item" href="${item.url}">
                    <span>
                        <strong>${item.title}</strong>
                        <span>${item.subtitle}</span>
                    </span>
                    <kbd>${item.key}</kbd>
                </a>
            `).join("") || `<div class="nxv2-command-item"><span><strong>لا توجد نتائج</strong><span>جرّب كلمة ثانية</span></span></div>`;
        }

        function open() {
            render("");
            overlay.classList.add("open");
            setTimeout(() => input.focus(), 30);
        }

        function hide() {
            overlay.classList.remove("open");
        }

        fab.addEventListener("click", open);
        close.addEventListener("click", hide);
        overlay.addEventListener("click", event => {
            if (event.target === overlay) hide();
        });

        input.addEventListener("input", () => render(input.value));

        document.addEventListener("keydown", event => {
            const tag = (event.target && event.target.tagName || "").toLowerCase();
            const typing = tag === "input" || tag === "textarea" || tag === "select";

            if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "k") {
                event.preventDefault();
                open();
                return;
            }

            if (!typing && event.key === "/") {
                event.preventDefault();
                open();
                return;
            }

            if (overlay.classList.contains("open") && event.key === "Escape") {
                hide();
                return;
            }

            if (overlay.classList.contains("open") && event.altKey) {
                const match = links.find(x => x.key.toLowerCase() === event.key.toLowerCase());
                if (match) {
                    location.href = match.url;
                }
            }
        });
    }

    function addProductControls() {
        const controls = document.querySelector(".nexora-controls");
        if (!controls || document.querySelector("[data-nxv2-density]")) return;

        const density = document.createElement("button");
        density.type = "button";
        density.className = "nexora-pill-button";
        density.setAttribute("data-nxv2-density", "toggle");
        density.textContent = localStorage.getItem("NEXORA.V2.Density") === "compact" ? "Comfort" : "Compact";
        controls.insertBefore(density, controls.firstChild);

        const current = localStorage.getItem("NEXORA.V2.Density");
        if (current === "compact") document.body.classList.add("nxv2-compact");

        density.addEventListener("click", () => {
            document.body.classList.toggle("nxv2-compact");
            const compact = document.body.classList.contains("nxv2-compact");
            localStorage.setItem("NEXORA.V2.Density", compact ? "compact" : "comfort");
            density.textContent = compact ? "Comfort" : "Compact";
        });
    }

    function revealCards() {
        const targets = document.querySelectorAll(".nx-card, .hr-final-card, .employee360v2-card, .employee-edit-card, .card, .nx-kpi, .hr-final-kpi, .employee360v2-kpi");
        targets.forEach(el => el.classList.add("nxv2-reveal"));

        if (!("IntersectionObserver" in window)) {
            targets.forEach(el => el.classList.add("nxv2-visible"));
            return;
        }

        const io = new IntersectionObserver(entries => {
            entries.forEach(entry => {
                if (entry.isIntersecting) {
                    entry.target.classList.add("nxv2-visible");
                    io.unobserve(entry.target);
                }
            });
        }, { threshold: .08 });

        targets.forEach(el => io.observe(el));
    }

    function improveMobileSidebar() {
        document.querySelectorAll("[data-sidebar-toggle]").forEach(btn => {
            btn.addEventListener("click", () => {
                document.body.classList.toggle("nexora-sidebar-open");
            });
        });

        document.addEventListener("click", event => {
            if (!document.body.classList.contains("nexora-sidebar-open")) return;
            const sidebar = document.querySelector(".nexora-sidebar");
            const toggle = event.target.closest("[data-sidebar-toggle]");
            if (toggle || (sidebar && sidebar.contains(event.target))) return;
            document.body.classList.remove("nexora-sidebar-open");
        });
    }

    document.addEventListener("DOMContentLoaded", () => {
        setActiveNav();
        createCommandBar();
        addProductControls();
        revealCards();
        improveMobileSidebar();
    });
})();
