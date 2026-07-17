(() => {
    "use strict";

    const PAGE_SELECTOR = ".z360-page";
    const SCROLLER_SELECTOR = "main.nexora-content";
    const OFFSET = 14;

    function resetRootScroll() {
        document.documentElement.scrollTop = 0;
        document.body.scrollTop = 0;
        window.scrollTo({
            top: 0,
            left: 0,
            behavior: "auto"
        });
    }

    function resolveTarget(hash, page) {
        if (!hash || hash === "#") {
            return null;
        }

        try {
            const id = decodeURIComponent(hash.slice(1));
            const target = document.getElementById(id);

            return target && page.contains(target)
                ? target
                : null;
        }
        catch (_) {
            return null;
        }
    }

    function scrollInsideContent(hash, behavior, updateHistory) {
        const page = document.querySelector(PAGE_SELECTOR);
        const scroller = document.querySelector(SCROLLER_SELECTOR);

        if (!page || !scroller) {
            return false;
        }

        const target = resolveTarget(hash, page);

        if (!target) {
            return false;
        }

        resetRootScroll();

        const scrollerRect = scroller.getBoundingClientRect();
        const targetRect = target.getBoundingClientRect();

        const targetTop =
            scroller.scrollTop +
            targetRect.top -
            scrollerRect.top -
            OFFSET;

        scroller.scrollTo({
            top: Math.max(0, targetTop),
            behavior: behavior || "smooth"
        });

        if (updateHistory) {
            history.replaceState(
                null,
                "",
                `${location.pathname}${location.search}${hash}`
            );
        }

        requestAnimationFrame(resetRootScroll);
        setTimeout(resetRootScroll, 100);
        setTimeout(resetRootScroll, 400);

        return true;
    }

    function bind() {
        const page = document.querySelector(PAGE_SELECTOR);

        if (!page || page.dataset.z360AnchorNavigationReady === "true") {
            return;
        }

        page.dataset.z360AnchorNavigationReady = "true";

        page.addEventListener(
            "click",
            event => {
                const link = event.target.closest('a[href^="#"]');

                if (!link || !page.contains(link)) {
                    return;
                }

                const hash = link.getAttribute("href");

                if (!resolveTarget(hash, page)) {
                    return;
                }

                event.preventDefault();
                event.stopPropagation();

                scrollInsideContent(hash, "smooth", true);
            },
            true
        );

        window.addEventListener("hashchange", () => {
            scrollInsideContent(location.hash, "smooth", false);
        });

        if (location.hash) {
            setTimeout(() => {
                scrollInsideContent(location.hash, "auto", false);
            }, 0);
        }
        else {
            resetRootScroll();
        }
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", bind, { once: true });
    }
    else {
        bind();
    }
})();