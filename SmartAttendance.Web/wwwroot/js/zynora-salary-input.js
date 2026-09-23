(() => {
    "use strict";

    const selector = "input[data-zynora-salary-format]";

    function normalizeDigits(value) {
        const arabic = "٠١٢٣٤٥٦٧٨٩";
        const persian = "۰۱۲۳۴۵۶۷۸۹";
        return String(value ?? "")
            .replace(/[٠-٩]/g, ch => String(arabic.indexOf(ch)))
            .replace(/[۰-۹]/g, ch => String(persian.indexOf(ch)));
    }

    function rawValue(value) {
        let raw = normalizeDigits(value)
            .replace(/[,\s]/g, "")
            .replace(/[^\d.]/g, "");

        const dot = raw.indexOf(".");
        if (dot >= 0) {
            raw = raw.slice(0, dot + 1) +
                raw.slice(dot + 1).replace(/\./g, "");
        }
        return raw;
    }

    function formatValue(value) {
        const raw = rawValue(value);
        if (!raw) return "";

        const hasDecimal = raw.includes(".");
        const parts = raw.split(".");
        let whole = parts[0] || "0";
        whole = whole.replace(/^0+(?=\d)/, "");
        whole = whole.replace(/\B(?=(\d{3})+(?!\d))/g, ",");

        if (!hasDecimal) return whole;

        const decimals = (parts[1] || "").slice(0, 4);
        return whole + "." + decimals;
    }

    const forms = new Set();

    document.querySelectorAll(selector).forEach(input => {
        const serverRaw = input.getAttribute("data-zynora-salary-raw");
        input.value = formatValue(
            serverRaw !== null && serverRaw.trim() !== ""
                ? serverRaw
                : input.value);

        input.addEventListener("input", () => {
            input.value = formatValue(input.value);
            input.setSelectionRange(input.value.length, input.value.length);
        });

        if (input.form) forms.add(input.form);
    });
    forms.forEach(form => {
        form.addEventListener("submit", () => {
            form.querySelectorAll(selector).forEach(input => {
                input.value = rawValue(input.value);
            });
        });
    });
})();
