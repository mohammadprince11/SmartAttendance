/*
 * ZYNORA rendered-page adapter.
 *
 * Historical classes remain available to page scripts. The adapter only adds
 * the canonical zy-* vocabulary consumed by zynora-unified-pages.css, including
 * for controls and messages inserted after the initial page render.
 */
(function () {
    "use strict";

    var ROOT_SELECTOR = ".zy-ui-contract";
    var NON_TEXT_INPUT_TYPES = ["checkbox", "radio", "hidden", "file", "color", "range", "image"];
    var PAGE_HEADERS = [
        "page-header", "hrms-page-header", "zhr-header", "nx-setup-header",
        "zynora-page-header", "ps-head", "ats-page-header", "dat-page-header"
    ];

    function classNames(element) {
        return Array.prototype.slice.call(element.classList);
    }

    function hasFragment(classes, fragments) {
        return classes.some(function (name) {
            var normalized = name.toLowerCase();
            return fragments.some(function (fragment) { return normalized.indexOf(fragment) !== -1; });
        });
    }

    function hasExact(classes, candidates) {
        return classes.some(function (name) { return candidates.indexOf(name.toLowerCase()) !== -1; });
    }

    function add(element, className) {
        if (!element.classList.contains(className)) element.classList.add(className);
    }

    function isManagedWidget(classes) {
        return hasFragment(classes, [
            "nxcal", "nxcs", "datepicker", "timepicker", "checkbox", "radio",
            "switch", "toggle", "pill", "chip", "stepper", "pagination", "pager", "bnav"
        ]);
    }

    function addButton(element, forcedVariant) {
        var classes = classNames(element);
        var existingVariant = classes.some(function (name) {
            return name.indexOf("zy-btn--") === 0 && name !== "zy-btn--sm";
        });
        add(element, "zy-btn");

        if (!existingVariant) {
            var variant = forcedVariant;
            if (!variant) {
                variant = hasFragment(classes, ["danger", "delete", "remove", "reject"]) ? "danger"
                    : hasFragment(classes, ["primary", "apply", "save", "create", "add", "submit"]) ? "primary"
                    : hasFragment(classes, ["ghost", "link", "back", "cancel", "close"]) ? "ghost"
                    : "secondary";
            }
            add(element, "zy-btn--" + variant);
        }

        if (hasFragment(classes, ["icon", "mini", "small", "-sm", "pager", "page-link", "close"])) {
            add(element, "zy-btn--sm");
        }
    }

    function addSemanticVariant(element, prefix, classes) {
        var variant = hasFragment(classes, ["danger", "error", "reject", "absent", "inactive"]) ? "danger"
            : hasFragment(classes, ["warning", "warn", "late", "pending"]) ? "warning"
            : hasFragment(classes, ["success", "ok", "approved", "active", "present"]) ? "success"
            : hasFragment(classes, ["info"]) ? "info"
            : null;

        if (variant) add(element, prefix + (prefix === "zy-alert" ? "-" : "--") + variant);
    }

    function enrich(element) {
        if (!element || element.nodeType !== 1) return;
        if (element.closest("[data-zy-preserve]")) return;

        var tag = element.tagName.toLowerCase();
        var type = (element.getAttribute("type") || "").toLowerCase();
        var classes = classNames(element);

        if (tag === "h1") add(element, "zy-page-title");
        if (tag === "h2") add(element, "zy-section-title");
        if (tag === "label") add(element, "zy-label");
        if (tag === "form") add(element, "zy-form");
        if (tag === "select") add(element, "zy-select");
        if (tag === "textarea") add(element, "zy-textarea");
        if (tag === "table") add(element, "zy-table");

        if (tag === "input") {
            if ((type === "submit" || type === "button" || type === "reset") && !isManagedWidget(classes)) {
                addButton(element, type === "submit" ? "primary" : "secondary");
            } else if (NON_TEXT_INPUT_TYPES.indexOf(type) === -1) {
                add(element, "zy-input");
            }
        }

        if (tag === "button" && !isManagedWidget(classes)) {
            addButton(element, type === "submit" ? "primary" : null);
        }
        if (hasExact(classes, PAGE_HEADERS)) add(element, "zy-page-header");

        if ((tag === "a" || tag === "button" || tag === "input")
            && !isManagedWidget(classes) && hasFragment(classes, ["btn", "button"])) {
            addButton(element, null);
        }

        var isCard = classes.some(function (name) {
            var value = name.toLowerCase();
            return (value === "card" || value.slice(-5) === "-card" || value.slice(-6) === "__card")
                && value.indexOf("grid") === -1 && value.indexOf("header") === -1 && value.indexOf("body") === -1;
        });
        if (isCard) add(element, "zy-card");

        if (hasFragment(classes, ["filter-bar"]) || classes.some(function (name) {
            return name.toLowerCase().slice(-11) === "filter-card";
        })) add(element, "zy-filter-bar");

        if (hasFragment(classes, ["table-responsive", "table-wrap"])) add(element, "zy-table-wrap");
        if (hasFragment(classes, ["pagination"]) || classes.some(function (name) {
            return name.toLowerCase().slice(-6) === "-pager";
        })) add(element, "zy-pagination");

        var isEmpty = classes.some(function (name) {
            var value = name.toLowerCase();
            return value === "empty-state" || value.slice(-6) === "-empty" || value.slice(-7) === "__empty";
        });
        if (isEmpty) add(element, "zy-empty");

        var isAlert = classes.some(function (name) {
            var value = name.toLowerCase();
            return value === "alert" || value.indexOf("-alert") !== -1 || value.indexOf("__alert") !== -1;
        });
        if (isAlert) {
            add(element, "zy-alert");
            addSemanticVariant(element, "zy-alert", classes);
        }

        if (hasFragment(classes, ["badge"]) || classes.some(function (name) {
            return name.toLowerCase().slice(-7) === "-status";
        })) {
            add(element, "zy-badge");
            addSemanticVariant(element, "zy-badge", classes);
        }

        var isTabs = classes.some(function (name) {
            var value = name.toLowerCase();
            return value === "tabs" || value.slice(-5) === "-tabs" || value.slice(-6) === "__tabs";
        });
        if (isTabs) add(element, "zy-tabs");

        var isTab = classes.some(function (name) {
            var value = name.toLowerCase();
            return value === "tab" || value.slice(-4) === "-tab" || value.slice(-5) === "__tab";
        });
        if (isTab) add(element, "zy-tab");
    }

    function enrichTree(root) {
        enrich(root);
        root.querySelectorAll("*").forEach(enrich);
        root.setAttribute("data-zy-contract", "v1");
    }

    function observe(root) {
        var observer = new MutationObserver(function (mutations) {
            mutations.forEach(function (mutation) {
                mutation.addedNodes.forEach(function (node) {
                    if (node.nodeType === 1) enrichTree(node);
                });
            });
        });
        observer.observe(root, { childList: true, subtree: true });
    }

    function initialize() {
        document.querySelectorAll(ROOT_SELECTOR).forEach(function (root) {
            enrichTree(root);
            observe(root);
        });
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initialize, { once: true });
    } else {
        initialize();
    }
}());
