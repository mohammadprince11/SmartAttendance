(function () {
    "use strict";

    function insideNavbar(el) {
        return !!(el && el.closest(
            'aside, nav, .sidebar, .side-bar, .app-sidebar, .layout-sidebar, .nx-sidebar, .nexora-sidebar, .nx-navbar, .nexora-navbar, .nx-menu, .nexora-menu, [role="navigation"]'
        ));
    }

    function isScrollable(el) {
        if (!el || el === document || el === window) return false;

        const style = window.getComputedStyle(el);
        const overflowY = style.overflowY;

        const allowsScroll =
            overflowY === "auto" ||
            overflowY === "scroll" ||
            overflowY === "overlay";

        return allowsScroll && el.scrollHeight > el.clientHeight + 2;
    }

    function findScrollable(start) {
        let el = start;

        while (el && el !== document.body && el !== document.documentElement) {
            if (isScrollable(el)) return el;
            el = el.parentElement;
        }

        const page = document.scrollingElement || document.documentElement;
        if (page && page.scrollHeight > page.clientHeight + 2) return page;

        return null;
    }

    document.addEventListener("wheel", function (e) {
        if (insideNavbar(e.target)) {
            return;
        }

        const dropdown = e.target.closest(
            '[role="listbox"], .dropdown-menu, .select-menu, .select-dropdown, .nx-select-menu, .nexora-select-menu, .nx-dropdown-menu, .nexora-dropdown-menu, .choices__list--dropdown, .choices__list[aria-expanded], .select2-results__options'
        );

        const scrollable = dropdown && isScrollable(dropdown)
            ? dropdown
            : findScrollable(e.target);

        if (!scrollable) return;

        const before = scrollable.scrollTop;
        scrollable.scrollTop += e.deltaY;

        if (scrollable.scrollTop !== before) {
            e.preventDefault();
            e.stopPropagation();
        }
    }, { capture: true, passive: false });

    function unlockBodyScroll() {
        document.documentElement.style.overflowY = "auto";
        document.body.style.overflowY = "auto";
        document.body.style.height = "auto";
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", unlockBodyScroll);
    } else {
        unlockBodyScroll();
    }

    window.addEventListener("load", unlockBodyScroll);
})();