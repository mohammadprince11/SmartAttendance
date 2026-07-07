(function () {
    "use strict";

    var inputSelector = 'input[data-nexora-date-field="true"]';
    var activeInput = null;
    var currentYear = null;
    var currentMonth = null;
    var viewMode = "days";
    var yearPageStart = null;

    var minYear = 1950;
    var maxYear = 2050;

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

    var shortMonthNames = [
        "\u0643\u0627\u0646\u0648\u0646 2",
        "\u0634\u0628\u0627\u0637",
        "\u0622\u0630\u0627\u0631",
        "\u0646\u064a\u0633\u0627\u0646",
        "\u0623\u064a\u0627\u0631",
        "\u062d\u0632\u064a\u0631\u0627\u0646",
        "\u062a\u0645\u0648\u0632",
        "\u0622\u0628",
        "\u0623\u064a\u0644\u0648\u0644",
        "\u062a\u0634\u0631\u064a\u0646 1",
        "\u062a\u0634\u0631\u064a\u0646 2",
        "\u0643\u0627\u0646\u0648\u0646 1"
    ];

    var dayNames = [
        "\u0623\u062d\u062f",
        "\u0627\u062b\u0646",
        "\u062b\u0644\u062b",
        "\u0623\u0631\u0628",
        "\u062e\u0645\u0633",
        "\u062c\u0645\u0639",
        "\u0633\u0628\u062a"
    ];

    function pad(number) {
        return String(number).padStart(2, "0");
    }

    function clampYear(year) {
        return Math.max(minYear, Math.min(maxYear, year));
    }

    function formatDate(date) {
        return date.getFullYear() + "-" + pad(date.getMonth() + 1) + "-" + pad(date.getDate());
    }

    function parseDate(value) {
        if (!value) return null;

        var match = String(value).trim().match(/^(\d{4})-(\d{1,2})-(\d{1,2})$/);
        if (!match) return null;

        var year = Number(match[1]);
        var month = Number(match[2]) - 1;
        var day = Number(match[3]);

        var date = new Date(year, month, day);

        if (
            date.getFullYear() !== year ||
            date.getMonth() !== month ||
            date.getDate() !== day
        ) {
            return null;
        }

        return date;
    }

    function getYearPageStart(year) {
        var start = Math.floor(year / 12) * 12;
        if (start < minYear) start = minYear;
        if (start > maxYear - 11) start = maxYear - 11;
        return start;
    }

    function createPicker() {
        var existing = document.querySelector(".nx-edit-datepicker");
        if (existing) return existing;

        var picker = document.createElement("div");
        picker.className = "nx-edit-datepicker";
        picker.setAttribute("dir", "rtl");

        picker.innerHTML =
            '<div class="nx-edit-datepicker__head nx-edit-datepicker__head--zoom">' +
                '<button type="button" class="nx-edit-datepicker__nav" data-nx-date-prev aria-label="Previous">\u2039</button>' +
                '<div class="nx-edit-datepicker__zoom-title">' +
                    '<button type="button" class="nx-edit-datepicker__month-btn" data-nx-date-month-btn></button>' +
                    '<button type="button" class="nx-edit-datepicker__year-btn" data-nx-date-year-btn title="Year zoom out"></button>' +
                '</div>' +
                '<button type="button" class="nx-edit-datepicker__nav" data-nx-date-next aria-label="Next">\u203a</button>' +
            '</div>' +
            '<div class="nx-edit-datepicker__week" data-nx-date-week></div>' +
            '<div class="nx-edit-datepicker__grid" data-nx-date-grid></div>' +
            '<div class="nx-edit-datepicker__year-grid" data-nx-year-grid></div>' +
            '<div class="nx-edit-datepicker__month-grid" data-nx-month-grid></div>' +
            '<div class="nx-edit-datepicker__footer">' +
                '<button type="button" data-nx-date-today>\u0627\u0644\u064a\u0648\u0645</button>' +
                '<button type="button" data-nx-date-clear>\u0645\u0633\u062d</button>' +
            '</div>';

        document.body.appendChild(picker);

        picker.querySelector("[data-nx-date-prev]").addEventListener("click", function () {
            if (viewMode === "years") {
                yearPageStart = Math.max(minYear, yearPageStart - 12);
            } else if (viewMode === "months") {
                currentYear = clampYear(currentYear - 1);
            } else {
                currentMonth--;

                if (currentMonth < 0) {
                    currentMonth = 11;
                    currentYear = clampYear(currentYear - 1);
                }
            }

            render();
            positionPicker();
        });

        picker.querySelector("[data-nx-date-next]").addEventListener("click", function () {
            if (viewMode === "years") {
                yearPageStart = Math.min(maxYear - 11, yearPageStart + 12);
            } else if (viewMode === "months") {
                currentYear = clampYear(currentYear + 1);
            } else {
                currentMonth++;

                if (currentMonth > 11) {
                    currentMonth = 0;
                    currentYear = clampYear(currentYear + 1);
                }
            }

            render();
            positionPicker();
        });

        picker.querySelector("[data-nx-date-year-btn]").addEventListener("click", function () {
            viewMode = "years";
            yearPageStart = getYearPageStart(currentYear);
            render();
            positionPicker();
        });

        picker.querySelector("[data-nx-date-month-btn]").addEventListener("click", function () {
            viewMode = "months";
            render();
            positionPicker();
        });

        picker.querySelector("[data-nx-date-today]").addEventListener("click", function () {
            setInputDate(new Date());
        });

        picker.querySelector("[data-nx-date-clear]").addEventListener("click", function () {
            if (!activeInput) return;

            activeInput.value = "";
            activeInput.dispatchEvent(new Event("input", { bubbles: true }));
            activeInput.dispatchEvent(new Event("change", { bubbles: true }));
            closePicker();
        });

        picker.addEventListener("mousedown", function (event) {
            event.preventDefault();
            event.stopPropagation();
        });

        picker.addEventListener("click", function (event) {
            event.stopPropagation();
        });

        return picker;
    }

    function renderWeek() {
        var picker = createPicker();
        var week = picker.querySelector("[data-nx-date-week]");

        if (week.dataset.ready === "1") return;

        week.innerHTML = "";

        dayNames.forEach(function (name) {
            var item = document.createElement("span");
            item.textContent = name;
            week.appendChild(item);
        });

        week.dataset.ready = "1";
    }

    function setVisibleMode(mode) {
        var picker = createPicker();

        picker.querySelector("[data-nx-date-week]").style.display = mode === "days" ? "" : "none";
        picker.querySelector("[data-nx-date-grid]").style.display = mode === "days" ? "" : "none";
        picker.querySelector("[data-nx-year-grid]").style.display = mode === "years" ? "grid" : "none";
        picker.querySelector("[data-nx-month-grid]").style.display = mode === "months" ? "grid" : "none";
    }

    function renderHeader() {
        var picker = createPicker();
        var monthBtn = picker.querySelector("[data-nx-date-month-btn]");
        var yearBtn = picker.querySelector("[data-nx-date-year-btn]");

        if (viewMode === "years") {
            monthBtn.textContent = String(yearPageStart) + " - " + String(yearPageStart + 11);
            yearBtn.textContent = "\u0627\u0644\u0633\u0646\u0648\u0627\u062A";
        } else if (viewMode === "months") {
            monthBtn.textContent = "\u0627\u062E\u062A\u0631 \u0627\u0644\u0634\u0647\u0631";
            yearBtn.textContent = String(currentYear);
        } else {
            monthBtn.textContent = monthNames[currentMonth];
            yearBtn.textContent = String(currentYear);
        }
    }

    function renderDays() {
        if (!activeInput) return;

        var picker = createPicker();
        var grid = picker.querySelector("[data-nx-date-grid]");
        var selected = parseDate(activeInput.value);
        var today = new Date();

        renderWeek();
        grid.innerHTML = "";

        var firstDay = new Date(currentYear, currentMonth, 1);
        var startDay = firstDay.getDay();
        var daysInMonth = new Date(currentYear, currentMonth + 1, 0).getDate();
        var prevDays = new Date(currentYear, currentMonth, 0).getDate();

        for (var i = 0; i < 42; i++) {
            var dayNumber;
            var date;
            var muted = false;

            if (i < startDay) {
                dayNumber = prevDays - startDay + i + 1;
                date = new Date(currentYear, currentMonth - 1, dayNumber);
                muted = true;
            } else if (i >= startDay + daysInMonth) {
                dayNumber = i - startDay - daysInMonth + 1;
                date = new Date(currentYear, currentMonth + 1, dayNumber);
                muted = true;
            } else {
                dayNumber = i - startDay + 1;
                date = new Date(currentYear, currentMonth, dayNumber);
            }

            var button = document.createElement("button");
            button.type = "button";
            button.className = "nx-edit-datepicker__day";
            button.textContent = String(dayNumber);
            button.dataset.date = formatDate(date);

            if (muted) button.classList.add("is-muted");

            if (
                date.getFullYear() === today.getFullYear() &&
                date.getMonth() === today.getMonth() &&
                date.getDate() === today.getDate()
            ) {
                button.classList.add("is-today");
            }

            if (
                selected &&
                date.getFullYear() === selected.getFullYear() &&
                date.getMonth() === selected.getMonth() &&
                date.getDate() === selected.getDate()
            ) {
                button.classList.add("is-selected");
            }

            button.addEventListener("click", function () {
                setInputValue(this.dataset.date);
            });

            grid.appendChild(button);
        }
    }

    function renderYears() {
        var picker = createPicker();
        var grid = picker.querySelector("[data-nx-year-grid]");
        grid.innerHTML = "";

        for (var year = yearPageStart; year < yearPageStart + 12; year++) {
            var button = document.createElement("button");
            button.type = "button";
            button.className = "nx-edit-datepicker__year-cell";
            button.textContent = String(year);
            button.dataset.year = String(year);

            if (year < minYear || year > maxYear) {
                button.disabled = true;
                button.classList.add("is-disabled");
            }

            if (year === currentYear) {
                button.classList.add("is-selected");
            }

            button.addEventListener("click", function () {
                currentYear = Number(this.dataset.year);
                viewMode = "days";
                render();
                positionPicker();
            });

            grid.appendChild(button);
        }
    }

    function renderMonths() {
        var picker = createPicker();
        var grid = picker.querySelector("[data-nx-month-grid]");
        grid.innerHTML = "";

        shortMonthNames.forEach(function (name, index) {
            var button = document.createElement("button");
            button.type = "button";
            button.className = "nx-edit-datepicker__month-cell";
            button.textContent = name;
            button.dataset.month = String(index);

            if (index === currentMonth) {
                button.classList.add("is-selected");
            }

            button.addEventListener("click", function () {
                currentMonth = Number(this.dataset.month);
                viewMode = "days";
                render();
                positionPicker();
            });

            grid.appendChild(button);
        });
    }

    function render() {
        renderHeader();
        setVisibleMode(viewMode);

        if (viewMode === "years") {
            renderYears();
        } else if (viewMode === "months") {
            renderMonths();
        } else {
            renderDays();
        }
    }

    function setInputDate(date) {
        setInputValue(formatDate(date));
    }

    function setInputValue(value) {
        if (!activeInput) return;

        activeInput.value = value;
        activeInput.dispatchEvent(new Event("input", { bubbles: true }));
        activeInput.dispatchEvent(new Event("change", { bubbles: true }));
        closePicker();
    }

    function positionPicker() {
        if (!activeInput) return;

        var picker = createPicker();
        picker.style.visibility = "hidden";
        picker.classList.add("is-open");

        var rect = activeInput.getBoundingClientRect();
        var pickerRect = picker.getBoundingClientRect();
        var gap = 8;
        var viewportWidth = window.innerWidth || document.documentElement.clientWidth;
        var viewportHeight = window.innerHeight || document.documentElement.clientHeight;

        var topBelow = rect.bottom + gap;
        var topAbove = rect.top - pickerRect.height - gap;
        var top;

        if (topBelow + pickerRect.height <= viewportHeight - 12) {
            top = topBelow;
        } else if (topAbove >= 12) {
            top = topAbove;
        } else {
            top = Math.max(12, viewportHeight - pickerRect.height - 12);
        }

        var left = rect.left;

        if (left + pickerRect.width > viewportWidth - 12) {
            left = viewportWidth - pickerRect.width - 12;
        }

        if (left < 12) {
            left = 12;
        }

        picker.style.top = Math.round(top) + "px";
        picker.style.left = Math.round(left) + "px";
        picker.style.visibility = "visible";
    }

    function openPicker(input) {
        activeInput = input;

        var parsed = parseDate(input.value);
        var base = parsed || new Date();

        currentYear = clampYear(base.getFullYear());
        currentMonth = base.getMonth();
        viewMode = "days";
        yearPageStart = getYearPageStart(currentYear);

        render();
        positionPicker();
    }

    function closePicker() {
        var picker = createPicker();
        picker.classList.remove("is-open");
        activeInput = null;
        viewMode = "days";
    }

    function normalizeManualValue(input) {
        var value = String(input.value || "").trim();
        if (!value) return;

        value = value.replace(/[\/.]/g, "-");

        var match = value.match(/^(\d{4})-(\d{1,2})-(\d{1,2})$/);
        if (!match) return;

        var date = parseDate(match[1] + "-" + pad(match[2]) + "-" + pad(match[3]));
        if (!date) return;

        input.value = formatDate(date);
    }

    function bind(input) {
        if (!input || input.dataset.nexoraDateReady === "1") return;

        input.dataset.nexoraDateReady = "1";
        input.setAttribute("autocomplete", "off");
        input.setAttribute("inputmode", "numeric");
        input.setAttribute("placeholder", "yyyy-mm-dd");

        input.addEventListener("focus", function () {
            openPicker(input);
        });

        input.addEventListener("click", function () {
            openPicker(input);
        });

        input.addEventListener("keydown", function (event) {
            if (event.key === "Escape") {
                closePicker();
            }

            if (event.key === "ArrowDown") {
                event.preventDefault();
                openPicker(input);
            }
        });

        input.addEventListener("blur", function () {
            window.setTimeout(function () {
                normalizeManualValue(input);
            }, 120);
        });
    }

    function bindAll() {
        document.querySelectorAll(inputSelector).forEach(bind);
    }

    document.addEventListener("mousedown", function (event) {
        var picker = document.querySelector(".nx-edit-datepicker");

        if (!picker) return;

        if (
            picker.contains(event.target) ||
            event.target.closest(inputSelector)
        ) {
            return;
        }

        closePicker();
    }, true);

    document.addEventListener("keydown", function (event) {
        if (event.key === "Escape") {
            closePicker();
        }
    });

    window.addEventListener("resize", function () {
        if (activeInput) positionPicker();
    });

    window.addEventListener("scroll", function () {
        if (activeInput) positionPicker();
    }, true);

    document.addEventListener("DOMContentLoaded", function () {
        bindAll();
    });
})();