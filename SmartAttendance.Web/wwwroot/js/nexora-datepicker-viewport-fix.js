(function () {
    "use strict";

    var selector = [
        'input[type="date"]',
        'input[type="datetime-local"]',
        'input[type="month"]',
        'input[type="time"]',
        'input[data-date]',
        'input[data-datepicker]',
        '.flatpickr-input'
    ].join(",");

    function isDateTarget(element) {
        return element && element.matches && element.matches(selector);
    }

    function getScrollParent(element) {
        var node = element.parentElement;

        while (node && node !== document.body && node !== document.documentElement) {
            var style = window.getComputedStyle(node);
            var overflowY = style.overflowY;

            if ((overflowY === "auto" || overflowY === "scroll") && node.scrollHeight > node.clientHeight + 4) {
                return node;
            }

            node = node.parentElement;
        }

        return document.scrollingElement || document.documentElement;
    }

    function ensureSafeViewport(input) {
        if (!input) return;

        var rect = input.getBoundingClientRect();
        var viewportHeight = window.innerHeight || document.documentElement.clientHeight || 800;

        var safeTop = Math.round(viewportHeight * 0.22);
        var safeBottom = Math.round(viewportHeight * 0.52);

        var needsMove =
            rect.top < safeTop ||
            rect.bottom > safeBottom ||
            (viewportHeight - rect.bottom) < 380;

        if (!needsMove) {
            return;
        }

        var scrollParent = getScrollParent(input);
        var parentRect = scrollParent === document.scrollingElement || scrollParent === document.documentElement
            ? { top: 0 }
            : scrollParent.getBoundingClientRect();

        var currentScroll = scrollParent.scrollTop;
        var desiredTop = Math.round(viewportHeight * 0.34);
        var delta = rect.top - desiredTop;

        scrollParent.scrollTo({
            top: currentScroll + delta,
            behavior: "smooth"
        });

        window.setTimeout(function () {
            try {
                input.scrollIntoView({
                    block: "center",
                    inline: "nearest",
                    behavior: "smooth"
                });
            } catch (_) {
                input.scrollIntoView(false);
            }
        }, 80);
    }

    function repositionKnownCalendars() {
        var calendars = document.querySelectorAll([
            ".flatpickr-calendar.open",
            ".air-datepicker",
            ".datepicker-dropdown",
            ".ui-datepicker",
            ".daterangepicker",
            ".bootstrap-datetimepicker-widget",
            ".tempus-dominus-widget",
            ".nxr-datepicker",
            ".nexora-datepicker"
        ].join(","));

        calendars.forEach(function (calendar) {
            var rect = calendar.getBoundingClientRect();
            var viewportHeight = window.innerHeight || document.documentElement.clientHeight || 800;

            calendar.style.zIndex = "999999";
            calendar.style.maxHeight = "min(420px, 70dvh)";
            calendar.style.overflowY = "auto";

            if (rect.bottom > viewportHeight - 16) {
                var overflow = rect.bottom - viewportHeight + 24;
                var currentTop = parseFloat(calendar.style.top || "0");

                if (!Number.isNaN(currentTop) && currentTop > 0) {
                    calendar.style.top = Math.max(12, currentTop - overflow) + "px";
                } else {
                    calendar.style.transform = "translateY(-" + overflow + "px)";
                }
            }
        });
    }

    function handleBeforeOpen(event) {
        var target = event.target;

        if (!isDateTarget(target)) {
            target = event.target && event.target.closest ? event.target.closest(selector) : null;
        }

        if (!target) return;

        ensureSafeViewport(target);

        window.setTimeout(repositionKnownCalendars, 80);
        window.setTimeout(repositionKnownCalendars, 180);
        window.setTimeout(repositionKnownCalendars, 320);
    }

    document.addEventListener("pointerdown", handleBeforeOpen, true);
    document.addEventListener("mousedown", handleBeforeOpen, true);
    document.addEventListener("touchstart", handleBeforeOpen, true);
    document.addEventListener("focusin", handleBeforeOpen, true);
    document.addEventListener("click", handleBeforeOpen, true);

    document.addEventListener("DOMContentLoaded", function () {
        document.querySelectorAll(selector).forEach(function (input) {
            input.addEventListener("focus", function () {
                ensureSafeViewport(input);
                window.setTimeout(repositionKnownCalendars, 120);
            }, true);
        });
    });

    window.addEventListener("resize", repositionKnownCalendars);
    window.addEventListener("scroll", function () {
        window.setTimeout(repositionKnownCalendars, 30);
    }, true);
})();