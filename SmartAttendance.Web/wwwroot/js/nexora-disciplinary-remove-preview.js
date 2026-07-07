(function () {
    "use strict";

    function cleanText(value) {
        return (value || "").replace(/\s+/g, " ").trim().toLowerCase();
    }

    function hasPreviewText(el) {
        const text = cleanText(el.textContent);

        const previewArabic = "\u0645\u0639\u0627\u064a\u0646\u0629 \u0634\u0643\u0644 \u0627\u0644\u0641\u0648\u0631\u0645\u0629";

        return (
            text.includes(cleanText(previewArabic)) ||
            text.includes("disciplinary notice") ||
            text.includes("nexora hr")
        );
    }

    function findPreviewCard(el) {
        let current = el;

        for (let i = 0; i < 12 && current && current !== document.body; i++) {
            const txt = cleanText(current.textContent);

            const hasPreview =
                txt.includes("disciplinary notice") ||
                txt.includes("nexora hr") ||
                txt.includes(cleanText("\u0645\u0639\u0627\u064a\u0646\u0629 \u0634\u0643\u0644 \u0627\u0644\u0641\u0648\u0631\u0645\u0629"));

            const hasFormFields = current.querySelectorAll("input, textarea, select").length > 0;
            const hasSaveButton = txt.includes("\u062d\u0641\u0638") || txt.includes("save");

            if (hasPreview && !hasFormFields && !hasSaveButton) {
                return current;
            }

            current = current.parentElement;
        }

        return null;
    }

    function removePreview() {
        if (!location.pathname.toLowerCase().includes("disciplinaryrules")) {
            return;
        }

        const candidates = Array.from(document.querySelectorAll("div, section, article, h1, h2, h3, h4, h5, h6"));

        for (const el of candidates) {
            if (!hasPreviewText(el)) continue;

            const card = findPreviewCard(el);
            if (card) {
                card.remove();
                console.log("NEXORA disciplinary preview removed.");
                return;
            }
        }
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", removePreview);
    } else {
        removePreview();
    }

    setTimeout(removePreview, 300);
    setTimeout(removePreview, 1000);
})();