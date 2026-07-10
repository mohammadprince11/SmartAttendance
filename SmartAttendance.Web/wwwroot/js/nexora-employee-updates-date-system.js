(function () {
    "use strict";

    var activeInput = null;
    var currentYear = 0;
    var currentMonth = 0;
    var panel = null;

    var monthNames = [
        "\u0643\u0627\u0646\u0648\u0646 \u0627\u0644\u062b\u0627\u0646\u064a",
        "\u0634\u0628\u0627\u0637",
        "\u0622\u0630\u0627\u0631",
        "\u0646\u064a\u0633\u0627\u0646",
        "\u0623\u064a\u0627\u0631",
        "\u062d\u0632\u064a\u0631\u0627\u0646",
        "\u062a\u0645\u0648\u0632",
        "\u0622\u0628",
        "\u0623\u064a\u0644\u0648\u0644",
        "\u062a\u0634\u0631\u064a\u0646 \u0627\u0644\u0623\u0648\u0644",
        "\u062a\u0634\u0631\u064a\u0646 \u0627\u0644\u062b\u0627\u0646\u064a",
        "\u0643\u0627\u0646\u0648\u0646 \u0627\u0644\u0623\u0648\u0644"
    ];

    var weekDays = [
        "\u0623\u062d\u062f",
        "\u0627\u062b\u0646",
        "\u062b\u0644\u0627",
        "\u0623\u0631\u0628",
        "\u062e\u0645\u064a",
        "\u062c\u0645\u0639",
        "\u0633\u0628\u062a"
    ];

    function isEmployeeUpdatesPage() {
        return location.pathname.toLowerCase().includes("/employeeupdates");
    }

    function pad(number) {
        return String(number).padStart(2, "0");
    }

    function toIso(date) {
        return date.getFullYear() + "-" + pad(date.getMonth() + 1) + "-" + pad(date.getDate());
    }

    function parseIso(value) {
        var match = String(value || "").match(/^(\d{4})-(\d{2})-(\d{2})$/);
        if (!match) return null;

        var year = Number(match[1]);
        var month = Number(match[2]) - 1;
        var day = Number(match[3]);
        var date = new Date(year, month, day);

        if (date.getFullYear() !== year || date.getMonth() !== month || date.getDate() !== day) {
            return null;
        }

        return date;
    }

    function dateFromInput(input) {
        return parseIso(input && input.value) || new Date();
    }

    function dispatchDateChange(input) {
        input.dispatchEvent(new Event("input", { bubbles: true }));
        input.dispatchEvent(new Event("change", { bubbles: true }));
    }

    function ensurePanel() {
        if (panel) return panel;

        panel = document.createElement("div");
        panel.className = "nxupd-date-panel";
        panel.hidden = true;
        panel.dir = "rtl";
        panel.innerHTML = [
            '<div class="nxupd-date-head">',
            '  <button type="button" class="nxupd-date-nav" data-nxupd-date-prev aria-label="Previous month"></button>',
            '  <div class="nxupd-date-title" data-nxupd-date-title></div>',
            '  <button type="button" class="nxupd-date-nav" data-nxupd-date-next aria-label="Next month"></button>',
            '</div>',
            '<div class="nxupd-date-weekdays"></div>',
            '<div class="nxupd-date-days"></div>',
            '<div class="nxupd-date-actions">',
            '  <button type="button" data-nxupd-date-clear></button>',
            '  <button type="button" class="primary" data-nxupd-date-today></button>',
            '</div>'
        ].join("");

        document.body.appendChild(panel);

        panel.querySelector("[data-nxupd-date-prev]").textContent = "\u2039";
        panel.querySelector("[data-nxupd-date-next]").textContent = "\u203a";
        panel.querySelector("[data-nxupd-date-clear]").textContent = "\u0645\u0633\u062d";
        panel.querySelector("[data-nxupd-date-today]").textContent = "\u0627\u0644\u064a\u0648\u0645";

        var weekdays = panel.querySelector(".nxupd-date-weekdays");
        weekDays.forEach(function (day) {
            var span = document.createElement("span");
            span.textContent = day;
            weekdays.appendChild(span);
        });

        panel.addEventListener("mousedown", function (event) {
            event.preventDefault();
        });

        panel.querySelector("[data-nxupd-date-prev]").addEventListener("click", function () {
            currentMonth -= 1;
            if (currentMonth < 0) {
                currentMonth = 11;
                currentYear -= 1;
            }
            renderPanel();
            placePanel();
        });

        panel.querySelector("[data-nxupd-date-next]").addEventListener("click", function () {
            currentMonth += 1;
            if (currentMonth > 11) {
                currentMonth = 0;
                currentYear += 1;
            }
            renderPanel();
            placePanel();
        });

        panel.querySelector("[data-nxupd-date-today]").addEventListener("click", function () {
            if (!activeInput) return;
            activeInput.value = toIso(new Date());
            dispatchDateChange(activeInput);
            closePanel();
        });

        panel.querySelector("[data-nxupd-date-clear]").addEventListener("click", function () {
            if (!activeInput) return;
            activeInput.value = "";
            dispatchDateChange(activeInput);
            closePanel();
        });

        return panel;
    }

    function renderPanel() {
        if (!activeInput) return;

        var root = ensurePanel();
        var title = root.querySelector("[data-nxupd-date-title]");
        var days = root.querySelector(".nxupd-date-days");
        var selected = parseIso(activeInput.value);

        title.textContent = monthNames[currentMonth] + " " + currentYear;
        days.innerHTML = "";

        var first = new Date(currentYear, currentMonth, 1);
        var start = new Date(currentYear, currentMonth, 1 - first.getDay());

        for (var i = 0; i < 42; i++) {
            var date = new Date(start.getFullYear(), start.getMonth(), start.getDate() + i);
            var button = document.createElement("button");
            button.type = "button";
            button.className = "nxupd-date-day";
            button.textContent = String(date.getDate());
            button.dataset.value = toIso(date);

            if (date.getMonth() !== currentMonth) {
                button.classList.add("is-muted");
            }

            if (
                selected &&
                selected.getFullYear() === date.getFullYear() &&
                selected.getMonth() === date.getMonth() &&
                selected.getDate() === date.getDate()
            ) {
                button.classList.add("is-selected");
            }

            button.addEventListener("click", function () {
                if (!activeInput) return;
                activeInput.value = this.dataset.value;
                dispatchDateChange(activeInput);
                closePanel();
            });

            days.appendChild(button);
        }
    }

    function placePanel() {
        if (!panel || !activeInput || panel.hidden) return;

        var rect = activeInput.getBoundingClientRect();
        var viewportW = document.documentElement.clientWidth || window.innerWidth;
        var viewportH = document.documentElement.clientHeight || window.innerHeight;
        var gap = 8;

        panel.style.visibility = "hidden";
        panel.style.display = "block";

        var width = Math.min(320, viewportW - 24);
        panel.style.width = width + "px";

        var panelRect = panel.getBoundingClientRect();
        var height = panelRect.height || 360;

        var left = rect.right - width;
        left = Math.max(12, Math.min(left, viewportW - width - 12));

        var spaceBelow = viewportH - rect.bottom - gap - 12;
        var spaceAbove = rect.top - gap - 12;
        var top = (spaceBelow >= height || spaceBelow >= spaceAbove)
            ? rect.bottom + gap
            : rect.top - height - gap;

        top = Math.max(12, Math.min(top, viewportH - height - 12));

        panel.style.left = Math.round(left) + "px";
        panel.style.top = Math.round(top) + "px";
        panel.style.visibility = "visible";
    }

    function openPanel(input) {
        activeInput = input;

        var date = dateFromInput(input);
        currentYear = date.getFullYear();
        currentMonth = date.getMonth();

        ensurePanel();
        panel.hidden = false;
        renderPanel();
        placePanel();
    }

    function closePanel() {
        if (panel) {
            panel.hidden = true;
        }
        activeInput = null;
    }

    function isDateField(input) {
        if (!input) return false;

        var joined = [
            input.name || "",
            input.placeholder || "",
            input.closest(".nxupd-field") ? input.closest(".nxupd-field").textContent : ""
        ].join(" ");

        return input.type === "date" || /date|hire|birth|effective|\u062a\u0627\u0631\u064a\u062e|\u0645\u0628\u0627\u0634\u0631\u0629|\u0645\u064a\u0644\u0627\u062f|\u0633\u0631\u064a\u0627\u0646/i.test(joined);
    }

    function enhance(input) {
        if (!isDateField(input)) return;

        if (input.dataset.nxupdDateReady !== "true") {
            input.dataset.nxupdDateReady = "true";
            input.dataset.originalType = input.type || "text";

            try {
                input.type = "text";
            } catch (_) { }

            input.classList.add("nxupd-date-input");
            input.autocomplete = "off";
            input.inputMode = "numeric";
            input.placeholder = input.placeholder || "yyyy-mm-dd";

            input.addEventListener("click", function (event) {
                event.preventDefault();
                openPanel(input);
            });

            input.addEventListener("focus", function () {
                openPanel(input);
            });

            input.addEventListener("keydown", function (event) {
                if (event.key === "Escape") {
                    closePanel();
                    input.blur();
                }

                if (event.key === "ArrowDown" || event.key === "Enter" || event.key === " ") {
                    event.preventDefault();
                    openPanel(input);
                }
            });
        }

        input.classList.add("nxupd-date-input");
        try {
            if (input.type !== "text") input.type = "text";
        } catch (_) { }
    }

    function refresh() {
        if (!isEmployeeUpdatesPage()) return;
        document.querySelectorAll(".nxupd-page input").forEach(enhance);
    }

    document.addEventListener("mousedown", function (event) {
        if (!panel || panel.hidden) return;
        if (panel.contains(event.target)) return;
        if (activeInput && activeInput === event.target) return;
        closePanel();
    }, true);

    document.addEventListener("keydown", function (event) {
        if (event.key === "Escape") closePanel();
    }, true);

    window.addEventListener("resize", placePanel, true);
    window.addEventListener("scroll", placePanel, true);

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", refresh);
    } else {
        refresh();
    }

    setTimeout(refresh, 100);
    setTimeout(refresh, 400);
    setTimeout(refresh, 900);

    window.NexoraEmployeeUpdatesRefreshDatePicker = refresh;
})();