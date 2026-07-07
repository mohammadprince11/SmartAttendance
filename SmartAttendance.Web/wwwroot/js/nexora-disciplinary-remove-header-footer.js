(function () {
    "use strict";

    function textOf(el) {
        return (el && el.textContent ? el.textContent : "")
            .replace(/\s+/g, " ")
            .trim()
            .toLowerCase();
    }

    function ownTextOf(el) {
        if (!el) return "";
        let value = "";
        el.childNodes.forEach(function (node) {
            if (node.nodeType === Node.TEXT_NODE) {
                value += node.textContent || "";
            }
        });
        return value.replace(/\s+/g, " ").trim().toLowerCase();
    }

    function isHeaderFooterText(el) {
        const all = textOf(el);
        const own = ownTextOf(el);

        const headerArabic = "\u0647\u064a\u062f\u0631 \u0627\u0644\u0641\u0648\u0631\u0645\u0629";
        const footerArabic = "\u0641\u0648\u062a\u0631 \u0627\u0644\u0641\u0648\u0631\u0645\u0629";

        return (
            own === "header" ||
            own === "footer" ||
            all === "header" ||
            all === "footer" ||
            own === headerArabic ||
            own === footerArabic ||
            all === headerArabic ||
            all === footerArabic
        );
    }

    function findCard(el) {
        let current = el;

        for (let i = 0; i < 12 && current && current !== document.body; i++) {
            const hasTextarea = current.querySelectorAll("textarea").length > 0;
            const hasButton = current.querySelectorAll("button, a.btn, input[type='submit']").length > 0;

            if (hasTextarea && !hasButton) {
                return current;
            }

            current = current.parentElement;
        }

        return null;
    }

    function removeBlocks() {
        if (!location.pathname.toLowerCase().includes("disciplinaryrules")) {
            return;
        }

        const elements = Array.from(document.querySelectorAll(
            "label, legend, h1, h2, h3, h4, h5, h6, strong, b, span, div"
        ));

        const removed = new Set();

        elements.forEach(function (el) {
            if (!isHeaderFooterText(el)) return;

            const card = findCard(el);
            if (!card || removed.has(card)) return;

            card.remove();
            removed.add(card);
        });

        console.log("NEXORA removed disciplinary header/footer blocks:", removed.size);
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", removeBlocks);
    } else {
        removeBlocks();
    }

    setTimeout(removeBlocks, 300);
    setTimeout(removeBlocks, 1000);
})();