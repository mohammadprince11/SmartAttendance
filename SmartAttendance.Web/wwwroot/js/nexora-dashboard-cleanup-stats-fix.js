/* NEXORA Dashboard Cleanup + Stats Cards Fix */
(function () {
    "use strict";

    const removeLabels = [
        "تقديم طلب",
        "تقديم الطلبات",
        "طلب",
        "الموافقات",
        "التقارير"
    ];

    const breakdownTitles = [
        "الموظفون حسب الفرع",
        "الموظفين حسب الفرع",
        "الموظفون حسب القسم",
        "الموظفين حسب القسم",
        "الموظفون حسب البلد",
        "الموظفين حسب البلد",
        "الموظفون حسب الجنس",
        "الموظفين حسب الجنس",
        "الموظفون حسب الجنسية",
        "الموظفين حسب الجنسية",
        "الموظفون حسب نوع العقد",
        "الموظفين حسب نوع العقد"
    ];

    function clean(value) {
        return (value || "").replace(/\s+/g, " ").trim();
    }

    function removeActionButtons() {
        document.querySelectorAll("a, button").forEach(el => {
            if (el.closest(".nexora-sidebar") || el.closest(".nexora-nav")) return;

            const text = clean(el.textContent);
            if (removeLabels.includes(text)) {
                el.remove();
            }
        });

        document.querySelectorAll(".nx-actions, .dashboard-actions, .quick-actions").forEach(el => {
            if (clean(el.textContent) === "") el.remove();
        });
    }

    function findCardForTitle(titleElement) {
        return titleElement.closest(".nx-card, .nexora-card, .card, .nxr-card, section, div");
    }

    function markBreakdownCards() {
        const headings = Array.from(document.querySelectorAll("h1,h2,h3,h4,strong,.card-title,.nx-title,.nx-card-title"));

        headings.forEach(h => {
            const text = clean(h.textContent);
            if (!breakdownTitles.some(t => text.includes(t))) return;

            const card = findCardForTitle(h);
            if (!card || card.classList.contains("nx-compact-breakdown-card")) return;

            card.classList.add("nx-compact-breakdown-card");

            const existingToolbar = card.querySelector(".nx-breakdown-toolbar");
            if (!existingToolbar) {
                const toolbar = document.createElement("div");
                toolbar.className = "nx-breakdown-toolbar";

                const rowCount = card.querySelectorAll("tr, li, .row, .list-group-item").length;
                toolbar.innerHTML = `<span>عرض مختصر قابل للتمرير</span><strong>${rowCount || ""}</strong>`;

                h.insertAdjacentElement("afterend", toolbar);
            }
        });
    }

    function init() {
        removeActionButtons();
        markBreakdownCards();
    }

    document.addEventListener("DOMContentLoaded", init);
    setTimeout(init, 150);
    setTimeout(init, 700);
})();
