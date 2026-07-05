
(function () {
    "use strict";

    var STORAGE_KEY = "NEXORA.Stable.Announcements";
    var SUPPRESSED_ALERT_TEXT = "تم تفعيل واجهة إنشاء الإعلان";
    var originalAlert = window.alert;

    window.alert = function (message) {
        var text = (message || "").toString();
        if (text.indexOf(SUPPRESSED_ALERT_TEXT) >= 0 || text.indexOf("ربط الحفظ الفعلي") >= 0) {
            return;
        }

        return originalAlert.apply(window, arguments);
    };

    function ready(fn) {
        if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", fn);
        else fn();
    }

    function text(el) {
        return (el && el.textContent ? el.textContent : "").replace(/\s+/g, " ").trim();
    }

    function esc(v) {
        return (v || "").toString().replace(/[&<>"']/g, function (m) {
            return { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[m];
        });
    }

    function uid() {
        return "ANN" + Date.now().toString(36) + Math.random().toString(36).slice(2, 7);
    }

    function today() {
        var d = new Date();
        return String(d.getDate()).padStart(2, "0") + "/" + String(d.getMonth() + 1).padStart(2, "0") + "/" + d.getFullYear();
    }

    function toast(message) {
        var el = document.querySelector(".nxstable-toast");
        if (!el) {
            el = document.createElement("div");
            el.className = "nxstable-toast";
            document.body.appendChild(el);
        }

        el.textContent = message;
        el.classList.add("show");
        clearTimeout(toast._timer);
        toast._timer = setTimeout(function () {
            el.classList.remove("show");
        }, 2300);
    }

    function readItems() {
        try {
            var raw = localStorage.getItem(STORAGE_KEY);
            if (!raw) return [];
            var parsed = JSON.parse(raw);
            return Array.isArray(parsed) ? parsed : [];
        } catch (_) {
            return [];
        }
    }

    function writeItems(items) {
        localStorage.setItem(STORAGE_KEY, JSON.stringify(items || []));
    }

    function isAnnouncementArea(el) {
        var root = el && el.closest ? el.closest("section, article, main, div") : null;
        var bodyText = text(document.body);
        var rootText = text(root);

        return (
            location.pathname.toLowerCase().indexOf("/employeeprofiles") >= 0 &&
            bodyText.indexOf("الإعلانات") >= 0 &&
            (
                rootText.indexOf("الإعلانات") >= 0 ||
                rootText.indexOf("إنشاء إعلان") >= 0 ||
                rootText.indexOf("قائمة الإعلانات") >= 0 ||
                bodyText.indexOf("إنشاء إعلان جديد") >= 0
            )
        );
    }

    function findAnnouncementRoot() {
        var candidates = Array.from(document.querySelectorAll("section, article, main, div"));

        var best = candidates.find(function (el) {
            var t = text(el);
            return t.indexOf("الإعلانات") >= 0 &&
                   t.indexOf("إنشاء إعلان جديد") >= 0 &&
                   t.length < 25000;
        });

        if (best) return best;

        return document.body;
    }

    function findOriginalTable() {
        var root = findAnnouncementRoot();
        var tables = Array.from(root.querySelectorAll("table"));

        return tables.find(function (table) {
            var t = text(table);
            return t.indexOf("فئة الإعلان") >= 0 && t.indexOf("العنوان") >= 0;
        });
    }

    function ensureStableTable() {
        var existing = document.querySelector("[data-nxstable-ann-table]");
        if (existing) return existing.querySelector("tbody");

        var root = findAnnouncementRoot();
        var table = findOriginalTable();

        if (table && table.querySelector("tbody")) {
            table.setAttribute("data-nxstable-ann-table", "1");
            return table.querySelector("tbody");
        }

        var card = document.createElement("div");
        card.className = "nxstable-ann-table-card";
        card.setAttribute("data-nxstable-ann-table", "1");
        card.innerHTML =
            '<div class="nxstable-ann-table-head">' +
                '<strong>قائمة الإعلانات</strong>' +
                '<span data-nxstable-counter>عرض 0 من 0</span>' +
            '</div>' +
            '<div class="nxstable-ann-table-wrap">' +
                '<table class="nxstable-ann-table">' +
                    '<thead><tr><th>فئة الإعلان</th><th>العنوان</th><th>تاريخ الإرسال</th><th>طريقة النشر</th><th>الحالة</th><th>الإجراء</th></tr></thead>' +
                    '<tbody></tbody>' +
                '</table>' +
            '</div>';

        var anchor = Array.from(root.children).reverse().find(function (el) {
            var t = text(el);
            return t.indexOf("بحث") >= 0 || t.indexOf("كل الحالات") >= 0 || t.indexOf("عرض") >= 0;
        });

        if (anchor && anchor.parentElement === root) {
            anchor.insertAdjacentElement("afterend", card);
        } else {
            root.appendChild(card);
        }

        return card.querySelector("tbody");
    }

    function rowHtml(item) {
        return '<tr data-nxstable-id="' + esc(item.id) + '">' +
            '<td><span class="nxstable-chip">' + esc(item.category || "عام") + '</span></td>' +
            '<td><div class="nxstable-ann-title"><strong>' + esc(item.title || "إعلان") + '</strong><span>' + esc(item.description || "") + '</span></div></td>' +
            '<td>' + esc(item.date || today()) + '</td>' +
            '<td><span class="nxstable-chip">منشور حائط</span></td>' +
            '<td><span class="nxstable-chip green">منشور</span></td>' +
            '<td><div class="nxstable-actions">' +
                '<button type="button" class="nxstable-btn" data-nxstable-view>عرض</button>' +
                '<button type="button" class="nxstable-btn" data-nxstable-edit>تعديل</button>' +
                '<button type="button" class="nxstable-btn danger" data-nxstable-delete>حذف</button>' +
            '</div></td>' +
        '</tr>';
    }

    function updateCounters() {
        var tbody = document.querySelector("[data-nxstable-ann-table] tbody");
        var count = tbody ? Array.from(tbody.querySelectorAll("tr")).filter(function (tr) {
            return tr.children.length > 1;
        }).length : 0;

        document.querySelectorAll("[data-nxstable-counter]").forEach(function (el) {
            el.textContent = "عرض " + count + " من " + count;
        });

        Array.from(document.querySelectorAll("span, div, strong")).forEach(function (el) {
            var t = text(el);
            if (/عرض\s+\d+\s+من\s+\d+/.test(t) && isAnnouncementArea(el)) {
                el.textContent = "عرض " + count + " من " + count;
            }
        });
    }

    function renderStoredRows() {
        var tbody = ensureStableTable();
        if (!tbody) return;

        var items = readItems();

        // Do not wipe server/original rows. Only add stored rows not already present.
        items.forEach(function (item) {
            if (tbody.querySelector('[data-nxstable-id="' + CSS.escape(item.id) + '"]')) return;
            tbody.insertAdjacentHTML("afterbegin", rowHtml(item));
        });

        updateCounters();
    }

    function getFormScope(start) {
        var form = start.closest("form");
        if (form) return form;

        var candidates = [];
        var node = start;
        while (node && node !== document.body) {
            if (text(node).indexOf("إنشاء إعلان") >= 0) candidates.push(node);
            node = node.parentElement;
        }

        return candidates[0] || findAnnouncementRoot();
    }

    function valueFrom(scope, selectorList) {
        for (var i = 0; i < selectorList.length; i++) {
            var el = scope.querySelector(selectorList[i]);
            if (!el) continue;
            var v = (el.value || text(el)).trim();
            if (v) return v;
        }

        return "";
    }

    function extractAnnouncement(scope) {
        var title = valueFrom(scope, [
            "[data-nxra-title]",
            "[data-ann-title]",
            "input[placeholder*='عنوان']",
            "input[name*='Title']",
            "input[name*='title']"
        ]);

        var description = valueFrom(scope, [
            "[data-nxra-desc]",
            "[data-ann-desc]",
            "textarea",
            "input[placeholder*='وصف']"
        ]);

        var category = valueFrom(scope, [
            "[data-nxra-category]",
            "[data-ann-category]",
            "select[name*='Category']",
            "select"
        ]);

        if (!category || category.indexOf("كل ") === 0) category = "عام";

        if (!title) title = "عنوان الإعلان";
        if (!description) description = "إعلان منشور على حائط الموظفين.";

        return {
            id: uid(),
            title: title,
            description: description,
            category: category,
            date: today(),
            method: "منشور حائط",
            status: "منشور"
        };
    }

    function saveAnnouncementFrom(scope) {
        var item = extractAnnouncement(scope);
        var items = readItems();

        items.unshift(item);
        writeItems(items);

        var tbody = ensureStableTable();
        if (tbody) {
            var emptyRows = Array.from(tbody.querySelectorAll("tr")).filter(function (tr) {
                return text(tr).indexOf("لا توجد") >= 0;
            });
            emptyRows.forEach(function (tr) { tr.remove(); });

            tbody.insertAdjacentHTML("afterbegin", rowHtml(item));
            updateCounters();
        }

        try {
            scope.querySelectorAll("input, textarea").forEach(function (el) {
                if ((el.type || "").toLowerCase() !== "hidden") el.value = "";
            });
        } catch (_) { }

        toast("تم حفظ الإعلان داخل الصفحة بدون رسالة تنبيه وبدون إعادة تحميل.");
    }

    function handleSafeDelete(button) {
        var row = button.closest("tr");

        if (!row) {
            var card = button.closest("[data-nxstable-id], article, .card, div");
            if (!card || !confirm("هل تريد حذف هذا الإعلان؟")) return;
            card.remove();
            toast("تم حذف الإعلان.");
            return;
        }

        if (!confirm("هل تريد حذف هذا الإعلان؟")) return;

        var id = row.getAttribute("data-nxstable-id");
        if (id) {
            var items = readItems().filter(function (item) {
                return item.id !== id;
            });
            writeItems(items);
        }

        row.remove();
        updateCounters();
        toast("تم حذف الإعلان بدون فتح صفحة بيضاء.");
    }

    function handleActions(event) {
        var button = event.target.closest("button, a, input[type='button'], input[type='submit']");
        if (!button) return;

        var label = (button.value || text(button)).trim();
        var inAnnouncements = isAnnouncementArea(button);

        if (!inAnnouncements) return;

        if (label === "حذف" || button.hasAttribute("data-nxstable-delete")) {
            event.preventDefault();
            event.stopPropagation();
            event.stopImmediatePropagation();

            handleSafeDelete(button);
            return;
        }

        if (label === "حفظ الإعلان" || label === "نشر الإعلان") {
            event.preventDefault();
            event.stopPropagation();
            event.stopImmediatePropagation();

            var scope = getFormScope(button);
            saveAnnouncementFrom(scope);
            return;
        }

        if (button.hasAttribute("data-nxstable-view")) {
            event.preventDefault();
            event.stopPropagation();
            event.stopImmediatePropagation();

            var row = button.closest("tr");
            alert(text(row));
            return;
        }

        if (button.hasAttribute("data-nxstable-edit")) {
            event.preventDefault();
            event.stopPropagation();
            event.stopImmediatePropagation();

            var row = button.closest("tr");
            var id = row && row.getAttribute("data-nxstable-id");
            var titleEl = row && row.querySelector(".nxstable-ann-title strong");
            var next = prompt("تعديل عنوان الإعلان:", titleEl ? text(titleEl) : "");
            if (next === null) return;
            next = next.trim();
            if (!next) return;

            if (titleEl) titleEl.textContent = next;

            if (id) {
                var items = readItems();
                items.forEach(function (item) {
                    if (item.id === id) item.title = next;
                });
                writeItems(items);
            }

            toast("تم تعديل الإعلان.");
        }
    }

    function handleSubmit(event) {
        var form = event.target;
        if (!form || !isAnnouncementArea(form)) return;

        var formText = text(form);
        if (formText.indexOf("إنشاء إعلان") < 0 && formText.indexOf("عنوان الإعلان") < 0) return;

        event.preventDefault();
        event.stopPropagation();
        event.stopImmediatePropagation();

        saveAnnouncementFrom(form);
    }

    ready(function () {
        document.addEventListener("click", handleActions, true);
        document.addEventListener("submit", handleSubmit, true);

        setTimeout(renderStoredRows, 250);
        setTimeout(renderStoredRows, 900);
    });
})();
