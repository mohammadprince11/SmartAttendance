/*
 * Shared HRMS UI behaviours (Kayan-style): tabs + slide-over panel.
 * Declarative, no dependencies. Works on any page that uses the markup:
 *
 * Tabs:
 *   <div class="hrms-tabs">
 *     <button class="hrms-tab active" data-hrms-tab="company">هيكلية الشركة</button>
 *     <button class="hrms-tab" data-hrms-tab="chart">الهيكل الهرمي</button>
 *   </div>
 *   <div class="hrms-tabpanel active" data-hrms-tabpanel="company">...</div>
 *   <div class="hrms-tabpanel" data-hrms-tabpanel="chart">...</div>
 *   (optional: wrap a tabs group in data-hrms-tabs-group to scope switching)
 *
 * Slide-over:
 *   <button data-hrms-open="panelId">فتح</button>
 *   <div class="hrms-slideover" id="panelId"> ... <button data-hrms-close>×</button> </div>
 *   (a backdrop is created automatically; ?hash or programmatic HrmsUI.open(id))
 */
(function () {
    "use strict";

    function activateTab(tab) {
        var key = tab.getAttribute("data-hrms-tab");
        if (!key) return;

        var group = tab.closest("[data-hrms-tabs-group]") || document;

        group.querySelectorAll(".hrms-tab[data-hrms-tab]").forEach(function (t) {
            if (t.closest("[data-hrms-tabs-group]") === tab.closest("[data-hrms-tabs-group]")) {
                t.classList.toggle("active", t === tab);
            }
        });

        group.querySelectorAll(".hrms-tabpanel[data-hrms-tabpanel]").forEach(function (p) {
            p.classList.toggle("active", p.getAttribute("data-hrms-tabpanel") === key);
        });

        try {
            if (tab.hasAttribute("data-hrms-tab-remember")) {
                history.replaceState(null, "", "#tab=" + key);
            }
        } catch (e) { /* ignore */ }
    }

    function ensureBackdrop() {
        var backdrop = document.querySelector(".hrms-slideover-backdrop");
        if (!backdrop) {
            backdrop = document.createElement("div");
            backdrop.className = "hrms-slideover-backdrop";
            backdrop.addEventListener("click", closeAll);
            document.body.appendChild(backdrop);
        }
        return backdrop;
    }

    function open(id) {
        var panel = document.getElementById(id);
        if (!panel) return;
        ensureBackdrop().classList.add("open");
        panel.classList.add("open");
        document.body.style.overflow = "hidden";
        var focusable = panel.querySelector("input, select, textarea, button, [tabindex]");
        if (focusable) { try { focusable.focus(); } catch (e) { /* ignore */ } }
    }

    function closeAll() {
        document.querySelectorAll(".hrms-slideover.open").forEach(function (p) {
            p.classList.remove("open");
        });
        var backdrop = document.querySelector(".hrms-slideover-backdrop");
        if (backdrop) backdrop.classList.remove("open");
        document.body.style.overflow = "";
    }

    document.addEventListener("click", function (e) {
        var tab = e.target.closest(".hrms-tab[data-hrms-tab]");
        if (tab) { activateTab(tab); return; }

        var opener = e.target.closest("[data-hrms-open]");
        if (opener) {
            e.preventDefault();
            open(opener.getAttribute("data-hrms-open"));
            return;
        }

        if (e.target.closest("[data-hrms-close]")) {
            e.preventDefault();
            closeAll();
        }
    });

    document.addEventListener("keydown", function (e) {
        if (e.key === "Escape") closeAll();
    });

    // Client-side table paginator: any <table data-hrms-paginate="N"> paired with
    // <div class="hrms-pager" data-hrms-pager-for="tableId" data-hrms-search="inputId">.
    function setupPaginatedTable(table) {
        var size = parseInt(table.getAttribute("data-hrms-paginate"), 10) || 10;
        var pager = document.querySelector('[data-hrms-pager-for="' + table.id + '"]');
        var searchInput = pager && pager.getAttribute("data-hrms-search")
            ? document.getElementById(pager.getAttribute("data-hrms-search")) : null;
        if (!table.tBodies.length) return;
        var rows = Array.prototype.slice.call(table.tBodies[0].rows);
        var page = 1;

        function filtered() {
            var q = (searchInput && searchInput.value || "").trim().toLowerCase();
            if (!q) return rows;
            return rows.filter(function (r) {
                return (r.getAttribute("data-search") || r.textContent).toLowerCase().indexOf(q) !== -1;
            });
        }

        function render() {
            var fr = filtered();
            var pages = Math.max(1, Math.ceil(fr.length / size));
            if (page > pages) page = pages;
            if (page < 1) page = 1;
            rows.forEach(function (r) { r.style.display = "none"; });
            fr.slice((page - 1) * size, page * size).forEach(function (r) { r.style.display = ""; });
            if (!pager) return;
            var start = fr.length ? (page - 1) * size + 1 : 0;
            var end = Math.min(page * size, fr.length);
            var html = '<span class="hrms-pager-info">عرض ' + start + "–" + end + " من " + fr.length + "</span>";
            html += '<span class="hrms-pager-controls">';
            html += '<button type="button" class="hrms-pager-btn" data-go="prev"' + (page <= 1 ? " disabled" : "") + ">‹</button>";
            for (var p = 1; p <= pages; p++) {
                if (p === 1 || p === pages || Math.abs(p - page) <= 1) {
                    html += '<button type="button" class="hrms-pager-btn' + (p === page ? " active" : "") + '" data-go="' + p + '">' + p + "</button>";
                } else if (Math.abs(p - page) === 2) {
                    html += '<span class="hrms-pager-info">…</span>';
                }
            }
            html += '<button type="button" class="hrms-pager-btn" data-go="next"' + (page >= pages ? " disabled" : "") + ">›</button></span>";
            pager.innerHTML = html;
        }

        if (pager) {
            pager.addEventListener("click", function (e) {
                var b = e.target.closest("[data-go]");
                if (!b || b.disabled) return;
                var g = b.getAttribute("data-go");
                if (g === "prev") page--;
                else if (g === "next") page++;
                else page = parseInt(g, 10);
                render();
            });
        }
        if (searchInput) {
            searchInput.addEventListener("input", function () { page = 1; render(); });
        }
        render();
    }

    // Restore active tab from hash on load (#tab=key) + wire paginated tables.
    document.addEventListener("DOMContentLoaded", function () {
        var m = /[#&]tab=([^&]+)/.exec(location.hash);
        if (m) {
            var tab = document.querySelector('.hrms-tab[data-hrms-tab="' + CSS.escape(m[1]) + '"]');
            if (tab) activateTab(tab);
        }
        document.querySelectorAll("table[data-hrms-paginate]").forEach(setupPaginatedTable);
    });

    window.HrmsUI = { open: open, close: closeAll, activateTab: activateTab };
})();
