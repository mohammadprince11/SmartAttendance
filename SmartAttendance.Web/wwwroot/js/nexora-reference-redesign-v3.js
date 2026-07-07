/* NEXORA Reference Redesign V3 */
(function () {
    function normalize(value) {
        return (value || "").toLowerCase().replace(/\/index$/, "").replace(/\/$/, "");
    }

    function activeNav() {
        const current = normalize(location.pathname);
        document.querySelectorAll(".nexora-nav-link[href]").forEach(a => {
            const href = normalize(a.getAttribute("href"));
            if (!href) return;
            if (current === href || current.startsWith(href + "/")) {
                a.classList.add("nexora-active");
                a.setAttribute("aria-current", "page");
                const details = a.closest("details");
                if (details) details.open = true;
            }
        });
    }

    function enhanceSearchShortcuts() {
        document.addEventListener("keydown", event => {
            if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "k") {
                event.preventDefault();
                const input = document.querySelector("input[type='search'], input[name='SearchTerm'], .nxr-input");
                if (input) input.focus();
            }
        });
    }

    document.addEventListener("DOMContentLoaded", function () {
        activeNav();
        enhanceSearchShortcuts();
    });
})();

