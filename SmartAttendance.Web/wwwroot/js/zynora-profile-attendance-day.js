(function () {
    "use strict";

    var modal = document.querySelector("[data-z360-day-modal]");
    if (!modal) return;
    if (modal.parentElement !== document.body) document.body.appendChild(modal);

    var employeeId = modal.dataset.employeeId || "";
    var lastTrigger = null;
    var currentDate = "";
    var fields = {
        subtitle: modal.querySelector("[data-z360-day-subtitle]"),
        date: modal.querySelector("[data-z360-day-date]"),
        day: modal.querySelector("[data-z360-day-name]"),
        status: modal.querySelector("[data-z360-day-status]"),
        checkIn: modal.querySelector("[data-z360-day-checkin]"),
        checkOut: modal.querySelector("[data-z360-day-checkout]"),
        device: modal.querySelector("[data-z360-day-device]"),
        shift: modal.querySelector("[data-z360-day-shift]"),
        dayKind: modal.querySelector("[data-z360-day-kind]"),
        adjusted: modal.querySelector("[data-z360-day-adjusted]"),
        pending: modal.querySelector("[data-z360-day-pending]"),
        pendingRef: modal.querySelector("[data-z360-pending-ref]"),
        pendingLink: modal.querySelector("[data-z360-pending-link]")
    };

    var requestSelect = modal.querySelector("[data-z360-request-type]");
    var requestTime = modal.querySelector("[data-z360-request-time]");
    function allTimePickers() { return modal.querySelectorAll("[data-z360-time-picker]"); }
    var timePickerTemplate = requestTime && requestTime.querySelector("[data-z360-time-picker]");
    var isArabicUi = ((document.documentElement.lang || "").toLowerCase().startsWith("ar") || document.documentElement.dir === "rtl");
    var requestShift = modal.querySelector("[data-z360-request-shift]");
    var requestShiftSelect = requestShift && requestShift.querySelector("select");
    var requestAttachment = modal.querySelector("[data-z360-request-attachment]");
    var attachmentInput = requestAttachment && requestAttachment.querySelector("input[type='file']");
    var attachmentLabel = modal.querySelector("[data-z360-request-attachment-label]");
    var pairList = modal.querySelector("[data-z360-pair-list]");
    var pairAdd = modal.querySelector("[data-z360-pair-add]");

    function setText(element, value) {
        if (element) element.textContent = value || "—";
    }

    function setPanel(name) {
        modal.querySelectorAll("[data-z360-day-panel]").forEach(function (panel) {
            panel.hidden = panel.dataset.z360DayPanel !== name;
        });
        modal.querySelectorAll("[data-z360-day-action]").forEach(function (button) {
            var active = button.dataset.z360DayAction === name;
            button.classList.toggle("is-active", active);
            button.setAttribute("aria-pressed", active ? "true" : "false");
        });
    }

    function normalizeDigits(value) {
        var arabic = "٠١٢٣٤٥٦٧٨٩", persian = "۰۱۲۳۴۵۶۷۸۹";
        return String(value || "").replace(/[٠-٩۰-۹]/g, function (ch) {
            var i = arabic.indexOf(ch);
            return String(i >= 0 ? i : persian.indexOf(ch));
        });
    }

    function parseTimeText(text) {
        var raw = normalizeDigits(text).trim();
        if (!raw) return "";
        var period = null;
        if (/(?:صباح(?:اً|ا)?|ص)\s*$/i.test(raw)) { period = "AM"; raw = raw.replace(/(?:صباح(?:اً|ا)?|ص)\s*$/i, "").trim(); }
        else if (/(?:مساء(?:ً|ا)?|م)\s*$/i.test(raw)) { period = "PM"; raw = raw.replace(/(?:مساء(?:ً|ا)?|م)\s*$/i, "").trim(); }
        else if (/(?:a\.?m\.?)\s*$/i.test(raw)) { period = "AM"; raw = raw.replace(/(?:a\.?m\.?)\s*$/i, "").trim(); }
        else if (/(?:p\.?m\.?)\s*$/i.test(raw)) { period = "PM"; raw = raw.replace(/(?:p\.?m\.?)\s*$/i, "").trim(); }
        raw = raw.replace(/[٫\.]/g, ":").replace(/\s+/g, "");
        var m = /^(\d{1,2}):(\d{1,2})$/.exec(raw);
        if (!m) return null;
        var h = parseInt(m[1], 10), min = parseInt(m[2], 10);
        if (min < 0 || min > 59) return null;
        if (period) {
            if (h < 1 || h > 12) return null;
            if (period === "PM" && h < 12) h += 12;
            if (period === "AM" && h === 12) h = 0;
        } else if (h < 0 || h > 23) return null;
        return String(h).padStart(2, "0") + ":" + String(min).padStart(2, "0");
    }

    function formatTimeValue(value24, picker) {
        var m = /^(\d{1,2}):(\d{2})/.exec(value24 || "");
        if (!m) return "";
        var h24 = parseInt(m[1], 10), mm = parseInt(m[2], 10);
        var h12 = h24 % 12 || 12;
        var period = h24 >= 12 ? picker.dataset.pmLabel : picker.dataset.amLabel;
        return String(h12).padStart(2, "0") + ":" + String(mm).padStart(2, "0") + " " + period;
    }

    function markSelectedSuggestion(picker, value24) {
        picker.querySelectorAll("[data-z360-time-option]").forEach(function (button) {
            var selected = button.dataset.value === value24;
            button.classList.toggle("is-selected", selected);
            button.setAttribute("aria-selected", selected ? "true" : "false");
        });
    }

    function setPickerValue(picker, value24, normalizeText) {
        var value = picker.querySelector("[data-z360-time-value]");
        var text = picker.querySelector("[data-z360-time-text]");
        if (!value || !text) return;
        value.value = value24 || "";
        if (normalizeText) text.value = value24 ? formatTimeValue(value24, picker) : "";
        picker.classList.toggle("has-value", !!value24);
        picker.classList.remove("is-invalid");
        markSelectedSuggestion(picker, value24 || "");
    }

    function commitTimeText(picker, normalizeText) {
        var text = picker.querySelector("[data-z360-time-text]");
        if (!text) return false;
        var parsed = parseTimeText(text.value);
        if (parsed === "") { setPickerValue(picker, "", normalizeText); return true; }
        if (!parsed) { picker.classList.add("is-invalid"); return false; }
        setPickerValue(picker, parsed, normalizeText);
        return true;
    }

    function closeTimePicker(picker) {
        var popover = picker.querySelector("[data-z360-time-popover]");
        var trigger = picker.querySelector("[data-z360-time-trigger]");
        if (popover) {
            popover.hidden = true;
            popover.style.removeProperty("top");
            popover.style.removeProperty("left");
            popover.style.removeProperty("width");
            popover.style.removeProperty("max-height");
        }
        if (trigger) trigger.setAttribute("aria-expanded", "false");
        picker.classList.remove("is-open", "open-up");
    }

    function closeAllTimePickers(except) {
        allTimePickers().forEach(function (picker) {
            if (picker !== except) closeTimePicker(picker);
        });
    }

    function resetTimePicker(picker) {
        var text = picker.querySelector("[data-z360-time-text]");
        if (text) text.value = "";
        setPickerValue(picker, "", false);
        closeTimePicker(picker);
    }

    function configureTimePickers(enabled) {
        var pickers = requestTime ? requestTime.querySelectorAll("[data-z360-time-picker]") : [];
        pickers.forEach(function (picker) {
            picker.dataset.enabled = enabled ? "true" : "false";
            picker.dataset.required = enabled ? "true" : "false";
            var trigger = picker.querySelector("[data-z360-time-trigger]");
            var text = picker.querySelector("[data-z360-time-text]");
            if (trigger) trigger.disabled = !enabled;
            if (text) text.disabled = !enabled;
            if (!enabled) resetTimePicker(picker);
        });
    }

    function openTimePicker(picker) {
        if (!picker || picker.dataset.enabled !== "true") return;
        var popover = picker.querySelector("[data-z360-time-popover]");
        var trigger = picker.querySelector("[data-z360-time-trigger]");
        if (!popover || !trigger) return;
        var willOpen = popover.hidden;
        closeAllTimePickers(picker);
        if (!willOpen) { closeTimePicker(picker); return; }

        popover.hidden = false;
        picker.classList.add("is-open");
        trigger.setAttribute("aria-expanded", "true");

        var rect = picker.getBoundingClientRect();
        var gap = 12;
        var width = Math.min(Math.max(rect.width, 280), window.innerWidth - (gap * 2));
        popover.style.width = width + "px";
        popover.style.left = Math.max(gap, Math.min(rect.right - width, window.innerWidth - width - gap)) + "px";
        popover.style.maxHeight = Math.max(220, window.innerHeight - 32) + "px";

        var popHeight = popover.offsetHeight || 320;
        var below = window.innerHeight - rect.bottom - gap;
        var above = rect.top - gap;
        var openUp = popHeight > below && above > below;
        picker.classList.toggle("open-up", openUp);
        var top = openUp
            ? Math.max(gap, rect.top - popHeight - 8)
            : Math.min(window.innerHeight - popHeight - gap, rect.bottom + 8);
        popover.style.top = Math.max(gap, top) + "px";

        var selected = popover.querySelector(".z360-time-suggestion.is-selected");
        if (selected) selected.scrollIntoView({ block: "center" });
    }

    function syncRequestFields() {
        if (!requestSelect) return;
        var option = requestSelect.options[requestSelect.selectedIndex];
        var needsTime = !!option && option.dataset.needsTime === "true";
        var needsShift = !!option && option.dataset.needsShift === "true";
        var needsAttachment = !!option && option.dataset.attachmentRequired === "true";

        if (requestTime) {
            requestTime.hidden = !needsTime;
            requestTime.style.display = needsTime ? "grid" : "none";
            configureTimePickers(needsTime);
        }
        if (requestShift) {
            requestShift.hidden = !needsShift;
            requestShift.style.display = needsShift ? "block" : "none";
        }
        if (requestShiftSelect) {
            requestShiftSelect.required = needsShift;
            if (!needsShift) requestShiftSelect.value = "";
        }
        if (requestAttachment) {
            requestAttachment.hidden = !needsAttachment;
            requestAttachment.style.display = needsAttachment ? "block" : "none";
        }
        if (attachmentInput) {
            attachmentInput.required = needsAttachment;
            if (!needsAttachment) attachmentInput.value = "";
        }
        if (attachmentLabel) {
            var label = option && option.dataset.attachmentLabel;
            attachmentLabel.textContent = label ? "المرفق — " + label : "المرفق";
        }
    }

    function setPickerFrom24(picker, value24) {
        var match = /^(\d{1,2}):(\d{2})/.exec(value24 || "");
        var normalized = match
            ? String(parseInt(match[1], 10)).padStart(2, "0") + ":" + String(parseInt(match[2], 10)).padStart(2, "0")
            : "";
        setPickerValue(picker, normalized, true);
    }

    function hydrateTimeHosts(root) {
        if (!timePickerTemplate) return;
        root.querySelectorAll("[data-z360-generated-time]").forEach(function (host) {
            var picker = timePickerTemplate.cloneNode(true);
            var hidden = picker.querySelector("[data-z360-time-value]");
            var text = picker.querySelector("[data-z360-time-text]");
            var trigger = picker.querySelector("[data-z360-time-trigger]");
            picker.dataset.enabled = "true";
            picker.dataset.required = host.dataset.required === "true" ? "true" : "false";
            picker.dataset.amLabel = isArabicUi ? "ص" : "AM";
            picker.dataset.pmLabel = isArabicUi ? "م" : "PM";
            if (hidden) hidden.name = host.dataset.name || "";
            if (text) text.disabled = false;
            if (trigger) trigger.disabled = false;
            setPickerFrom24(picker, host.dataset.value || "");
            host.replaceWith(picker);
        });
    }

    hydrateTimeHosts(modal);

    function pairRow(pair, index) {
        var row = document.createElement("div");
        row.className = "z360-punch-pair-row";
        row.innerHTML =
            '<input type="hidden" name="punchPairs[' + index + '].Id" value="' + (pair.id || 0) + '">' +
            '<label><span>الدخول</span><div data-z360-generated-time data-name="punchPairs[' + index + '].CheckIn" data-value="' + (pair.checkIn || '') + '"></div></label>' +
            '<label><span>الخروج</span><div data-z360-generated-time data-name="punchPairs[' + index + '].CheckOut" data-value="' + (pair.checkOut || '') + '"></div></label>' +
            '<button type="button" class="z360-pair-remove" aria-label="حذف زوج البصمات">×</button>';
        hydrateTimeHosts(row);
        return row;
    }
    function renumberPairs() {
        if (!pairList) return;
        pairList.querySelectorAll(".z360-punch-pair-row").forEach(function (row, index) {
            row.querySelectorAll("input[name]").forEach(function (input) {
                input.name = input.name.replace(/punchPairs\[\d+\]/, "punchPairs[" + index + "]");
            });
        });
    }

    function addPair(pair) {
        if (!pairList) return;
        var index = pairList.querySelectorAll(".z360-punch-pair-row").length;
        pairList.appendChild(pairRow(pair || {}, index));
    }

    async function loadPairs() {
        if (!pairList || !currentDate || !employeeId) return;
        pairList.innerHTML = '<div class="z360-pair-loading">جاري تحميل بصمات اليوم…</div>';
        try {
            var params = new URLSearchParams({ handler: "DayPunchPairs", id: employeeId, date: currentDate });
            var response = await fetch(window.location.pathname + "?" + params.toString(), {
                credentials: "same-origin",
                headers: { "Accept": "application/json" }
            });
            if (!response.ok) throw new Error("load failed");
            var data = await response.json();
            if (!data.ok) throw new Error(data.message || "load failed");
            pairList.innerHTML = "";
            (data.pairs || []).forEach(addPair);
            if (!data.pairs || data.pairs.length === 0) addPair({});
        } catch (_) {
            pairList.innerHTML = '<div class="z360-pair-error">تعذر تحميل بصمات هذا اليوم. أعد فتح النافذة.</div>';
        }
    }
    function openDay(trigger) {
        lastTrigger = trigger;
        modal.querySelectorAll("form").forEach(function (form) { form.reset(); });
        currentDate = trigger.dataset.date || "";
        var day = trigger.dataset.day || "";
        var pendingRef = trigger.dataset.pendingRef || "";

        setText(fields.subtitle, currentDate + (day ? " · " + day : ""));
        setText(fields.date, currentDate);
        setText(fields.day, day);
        setText(fields.status, trigger.dataset.status);
        setText(fields.checkIn, trigger.dataset.checkin);
        setText(fields.checkOut, trigger.dataset.checkout);
        setText(fields.device, trigger.dataset.device);
        setText(fields.shift, trigger.dataset.shift);
        setText(fields.dayKind, trigger.dataset.daykind);

        if (fields.adjusted) fields.adjusted.hidden = trigger.dataset.adjusted !== "true";
        if (fields.pending) fields.pending.hidden = !pendingRef;
        setText(fields.pendingRef, pendingRef);
        if (fields.pendingLink && pendingRef) {
            fields.pendingLink.href = "/MissingPunchRequests?Search=" + encodeURIComponent(pendingRef);
        }

        modal.querySelectorAll("[data-z360-day-form-date]").forEach(function (input) {
            input.value = currentDate;
        });
        if (pairList) pairList.innerHTML = "";
        setPanel(null);
        syncRequestFields();
        modal.hidden = false;
        document.body.classList.add("z360-day-modal-open");
        var closeButton = modal.querySelector("[data-z360-day-close]");
        if (closeButton) window.setTimeout(function () { closeButton.focus(); }, 20);
    }
    function closeDay() {
        closeAllTimePickers();
        modal.hidden = true;
        document.body.classList.remove("z360-day-modal-open");
        setPanel(null);
        if (lastTrigger) lastTrigger.focus();
    }

    document.querySelectorAll("[data-z360-day-open]").forEach(function (row) {
        row.addEventListener("click", function (event) {
            if (event.target.closest("a,button,input,select,textarea,label")) return;
            openDay(row);
        });
        row.addEventListener("keydown", function (event) {
            if (event.key === "Enter" || event.key === " ") {
                event.preventDefault();
                openDay(row);
            }
        });
    });

    modal.querySelectorAll("[data-z360-day-action]").forEach(function (button) {
        button.addEventListener("click", function () {
            var name = button.dataset.z360DayAction || null;
            setPanel(name);
            if (name === "request") syncRequestFields();
            if (name === "edit") loadPairs();
            var panel = modal.querySelector('[data-z360-day-panel="' + name + '"]');
            var first = panel && panel.querySelector("input:not([type='hidden']), textarea, select");
            if (first) window.setTimeout(function () { first.focus(); }, 20);
        });
    });

    if (requestSelect) requestSelect.addEventListener("change", syncRequestFields);

    modal.addEventListener("click", function (event) {
        var trigger = event.target.closest && event.target.closest("[data-z360-time-trigger]");
        if (trigger) {
            event.preventDefault();
            openTimePicker(trigger.closest("[data-z360-time-picker]"));
            return;
        }

        var option = event.target.closest && event.target.closest("[data-z360-time-option]");
        if (option) {
            event.preventDefault();
            var picker = option.closest("[data-z360-time-picker]");
            if (!picker) return;
            setPickerValue(picker, option.dataset.value || "", true);
            closeTimePicker(picker);
            var textInput = picker.querySelector("[data-z360-time-text]");
            if (textInput) textInput.focus();
            return;
        }

        if (!event.target.closest("[data-z360-time-picker]")) closeAllTimePickers();
    });

    modal.addEventListener("focusin", function (event) {
        var text = event.target.closest && event.target.closest("[data-z360-time-text]");
        if (!text) return;
        var picker = text.closest("[data-z360-time-picker]");
        if (picker && picker.dataset.enabled === "true") openTimePicker(picker);
    });

    modal.addEventListener("input", function (event) {
        var text = event.target.closest && event.target.closest("[data-z360-time-text]");
        if (!text) return;
        var picker = text.closest("[data-z360-time-picker]");
        if (!picker) return;
        picker.classList.remove("is-invalid");
        var parsed = parseTimeText(text.value);
        var hidden = picker.querySelector("[data-z360-time-value]");
        if (hidden) hidden.value = parsed || "";
        markSelectedSuggestion(picker, parsed || "");
        picker.classList.toggle("has-value", !!parsed);
    });

    modal.addEventListener("change", function (event) {
        var text = event.target.closest && event.target.closest("[data-z360-time-text]");
        if (!text) return;
        commitTimeText(text.closest("[data-z360-time-picker]"), true);
    });

    modal.addEventListener("keydown", function (event) {
        var text = event.target.closest && event.target.closest("[data-z360-time-text]");
        if (!text) return;
        var picker = text.closest("[data-z360-time-picker]");
        if (event.key === "Enter") {
            event.preventDefault();
            if (commitTimeText(picker, true)) closeTimePicker(picker);
        } else if (event.key === "ArrowDown") {
            event.preventDefault();
            openTimePicker(picker);
        }
    });

    var requestForm = requestSelect && requestSelect.closest("form");
    if (requestForm) requestForm.addEventListener("submit", function (event) {
        var selected = requestSelect.options[requestSelect.selectedIndex];
        var needsTime = !!selected && selected.dataset.needsTime === "true";
        if (!needsTime) return;
        var pickers = Array.from(requestTime ? requestTime.querySelectorAll("[data-z360-time-picker]") : []);
        var invalid = pickers.find(function (picker) { return !commitTimeText(picker, true); });
        var missing = invalid || pickers.find(function (picker) {
            var value = picker.querySelector("[data-z360-time-value]");
            return !value || !value.value;
        });
        if (!missing) return;
        event.preventDefault();
        missing.classList.add("is-invalid");
        var text = missing.querySelector("[data-z360-time-text]");
        if (text) text.focus();
        openTimePicker(missing);
    });

    modal.querySelectorAll("form").forEach(function (form) {
        form.addEventListener("submit", function (event) {
            var required = Array.from(form.querySelectorAll('[data-z360-time-picker][data-required="true"]'));
            var invalid = required.find(function (picker) { return !commitTimeText(picker, true); });
            var missing = invalid || required.find(function (picker) {
                var value = picker.querySelector("[data-z360-time-value]");
                return !value || !value.value;
            });
            if (!missing) return;
            event.preventDefault();
            missing.classList.add("is-invalid");
            var text = missing.querySelector("[data-z360-time-text]");
            if (text) text.focus();
            openTimePicker(missing);
        });
    });

    if (pairAdd) pairAdd.addEventListener("click", function () {
        addPair({});
        renumberPairs();
    });

    if (pairList) pairList.addEventListener("click", function (event) {
        var remove = event.target.closest("[data-z360-pair-remove], .z360-pair-remove");
        if (!remove) return;
        var row = remove.closest(".z360-punch-pair-row");
        if (!row) return;
        var rows = pairList.querySelectorAll(".z360-punch-pair-row");
        if (rows.length <= 1) {
            row.querySelectorAll("[data-z360-time-picker]").forEach(function (picker) { resetTimePicker(picker); });
            return;
        }
        row.remove();
        renumberPairs();
    });

    modal.querySelectorAll("[data-z360-day-close]").forEach(function (button) {
        button.addEventListener("click", closeDay);
    });
    modal.addEventListener("click", function (event) {
        if (event.target === modal) closeDay();
    });
    document.addEventListener("keydown", function (event) {
        if (event.key !== "Escape" || modal.hidden) return;
        var openPopover = modal.querySelector("[data-z360-time-popover]:not([hidden])");
        if (openPopover) {
            closeTimePicker(openPopover.closest("[data-z360-time-picker]"));
            return;
        }
        closeDay();
    });
})();
