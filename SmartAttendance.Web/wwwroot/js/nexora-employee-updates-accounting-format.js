(function () {
    "use strict";

    function isEmployeeUpdatesPage() {
        return location.pathname.toLowerCase().includes("/employeeupdates");
    }

    function normalizeDigits(value) {
        return String(value || "")
            .replace(/[Ù -Ù©]/g, function (d) { return "Ù Ù¡Ù¢Ù£Ù¤Ù¥Ù¦Ù§Ù¨Ù©".indexOf(d); })
            .replace(/[Û°-Û¹]/g, function (d) { return "Û°Û±Û²Û³Û´ÛµÛ¶Û·Û¸Û¹".indexOf(d); });
    }

    function cleanMoney(value) {
        return normalizeDigits(value)
            .replace(/IQD/ig, "")
            .replace(/Ø¯\.?Ø¹\.?/g, "")
            .replace(/[^\d.-]/g, "");
    }

    function formatMoney(value) {
        var cleaned = cleanMoney(value);

        if (!cleaned || cleaned === "-" || cleaned === ".") {
            return "";
        }

        var number = Number(cleaned);
        if (!isFinite(number)) {
            return "";
        }

        return new Intl.NumberFormat("en-US", {
            maximumFractionDigits: 0
        }).format(number) + " IQD";
    }

    function isAccountingInput(input) {
        if (!input || !input.name) return false;

        var name = input.name.toLowerCase();

        return (
            name === "basicsalary" ||
            name.indexOf("allowance") >= 0 ||
            name.indexOf("deduction") >= 0 ||
            name.indexOf("salary") >= 0
        );
    }

    function prepareInput(input) {
        if (!isAccountingInput(input)) return;

        input.classList.add("nxupd-accounting-input");
        input.inputMode = "numeric";
        input.autocomplete = "off";

        if (input.type !== "text") {
            input.dataset.originalType = input.type || "text";
            input.type = "text";
        }

        if (input.dataset.nxAccountingReady === "true") {
            if (document.activeElement !== input) {
                input.value = formatMoney(input.value);
            }
            return;
        }

        input.dataset.nxAccountingReady = "true";

        input.addEventListener("focus", function () {
            input.value = cleanMoney(input.value);
            setTimeout(function () {
                try {
                    input.setSelectionRange(input.value.length, input.value.length);
                } catch (_) { }
            }, 0);
        });

        input.addEventListener("input", function () {
            input.value = cleanMoney(input.value);
        });

        input.addEventListener("blur", function () {
            input.value = formatMoney(input.value);
        });

        input.value = formatMoney(input.value);
    }

    function formatCurrentValues() {
        document.querySelectorAll(".nxupd-field small, .nxupd-field-current, .nxupd-current").forEach(function (el) {
            var text = el.textContent || "";

            if (!/Ø§Ù„Ù‚ÙŠÙ…Ø© Ø§Ù„Ø­Ø§Ù„ÙŠØ©/i.test(text)) return;
            if (!/\d/.test(text)) return;

            var cleanedText = text.replace(/IQD/ig, "").trim();

            el.textContent = cleanedText.replace(/(\d[\d,]*)/g, function (match) {
                return formatMoney(match);
            });
        });
    }

    function formatAll() {
        if (!isEmployeeUpdatesPage()) return;

        document.querySelectorAll("input").forEach(prepareInput);
        formatCurrentValues();
    }

    function stripBeforeSubmit() {
        if (!isEmployeeUpdatesPage()) return;

        document.querySelectorAll("input.nxupd-accounting-input").forEach(function (input) {
            input.value = cleanMoney(input.value);
        });
    }

    document.addEventListener("submit", stripBeforeSubmit, true);

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", formatAll);
    } else {
        formatAll();
    }

    setTimeout(formatAll, 100);
    setTimeout(formatAll, 400);
    setTimeout(formatAll, 900);

    window.NexoraFormatEmployeeUpdatesAccounting = formatAll;
})();