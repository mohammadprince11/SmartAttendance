/* NEXORA_FIX13A_GLOBAL_CUSTOM_SELECT_SYSTEM_START */
(function () {
    "use strict";

    var SELECTOR = [
        "select[data-nexora-select='true']",
        "select.nxr-native-select",
        "select.nxr-select",
        "select.nxr-filter-select",
        "select.form-control",
        "select.form-select",
        ".nxr-documents-modal select",
        ".nxr-document-row select",
        "select[data-document-type]",
        "select[data-document-required]",
        "select[data-document-select]",
        "select[name^='InitialDocumentTypes']",
        "select[name^='InitialDocumentRequired']",
        "select:not([multiple]):not([size])"
    ].join(",");

    var openState = null;
    var scheduleToken = 0;

    function isEligible(select) {
        if (!select || select.tagName !== "SELECT") return false;
        if (select.multiple) return false;
        if (select.size && select.size > 1) return false;
        if (select.dataset.nexoraNativeOnly === "true") return false;
        if (select.closest("[data-nexora-select-skip='true']")) return false;
        return true;
    }

    function optionText(option) {
        if (!option) return "\u2014";
        var text = (option.textContent || "").trim();
        return text || "\u2014";
    }

    function getSelectedOption(select) {
        if (!select) return null;
        if (select.selectedIndex >= 0 && select.options[select.selectedIndex]) {
            return select.options[select.selectedIndex];
        }
        return select.options.length ? select.options[0] : null;
    }

    function closeOpen() {
        if (!openState) return;

        openState.wrapper.classList.remove("is-open");
        openState.trigger.setAttribute("aria-expanded", "false");

        if (openState.panel && openState.panel.parentNode) {
            openState.panel.parentNode.removeChild(openState.panel);
        }

        openState = null;
    }

    function placePanel(state) {
        if (!state || !state.panel || !state.trigger) return;

        var rect = state.trigger.getBoundingClientRect();
        var viewportWidth = document.documentElement.clientWidth || window.innerWidth;
        var viewportHeight = document.documentElement.clientHeight || window.innerHeight;
        var gap = 8;
        var width = Math.max(160, Math.round(rect.width));
        var left = Math.round(rect.left);
        var top = Math.round(rect.bottom + gap);

        if (left + width > viewportWidth - 12) {
            left = Math.max(12, viewportWidth - width - 12);
        }

        var remainingBottom = viewportHeight - rect.bottom - gap - 12;
        var remainingTop = rect.top - gap - 12;

        if (remainingBottom < 170 && remainingTop > remainingBottom) {
            state.panel.style.maxHeight = Math.max(140, Math.min(320, remainingTop)) + "px";
            top = Math.round(rect.top - gap);
            state.panel.style.transform = "translateY(-100%)";
        } else {
            state.panel.style.maxHeight = Math.max(140, Math.min(320, remainingBottom)) + "px";
            state.panel.style.transform = "none";
        }

        state.panel.style.width = width + "px";
        state.panel.style.left = left + "px";
        state.panel.style.top = top + "px";
    }

    function buildPanel(select, wrapper, trigger) {
        var panel = document.createElement("div");
        panel.className = "nxcs-panel";
        panel.setAttribute("role", "listbox");

        Array.prototype.forEach.call(select.options, function (option, index) {
            if (option.hidden) return;

            var item = document.createElement("button");
            item.type = "button";
            item.className = "nxcs-option";
            item.setAttribute("role", "option");
            item.dataset.value = option.value;
            item.dataset.index = String(index);
            item.textContent = optionText(option);

            if (option.disabled) {
                item.classList.add("is-disabled");
                item.disabled = true;
            }

            if (option.selected) {
                item.classList.add("is-selected");
                item.setAttribute("aria-selected", "true");
            } else {
                item.setAttribute("aria-selected", "false");
            }

            item.addEventListener("click", function (event) {
                event.preventDefault();
                event.stopPropagation();

                if (option.disabled) return;

                select.selectedIndex = index;
                select.value = option.value;

                select.dispatchEvent(new Event("input", { bubbles: true }));
                select.dispatchEvent(new Event("change", { bubbles: true }));

                syncOne(select);
                closeOpen();
                trigger.focus();
            });

            panel.appendChild(item);
        });

        return panel;
    }

    function openSelect(select) {
        var wrapper = select._nxcsWrapper;
        if (!wrapper || select.disabled) return;

        if (openState && openState.select === select) {
            closeOpen();
            return;
        }

        closeOpen();

        var trigger = wrapper.querySelector(".nxcs-trigger");
        var panel = buildPanel(select, wrapper, trigger);

        document.body.appendChild(panel);

        openState = {
            select: select,
            wrapper: wrapper,
            trigger: trigger,
            panel: panel
        };

        wrapper.classList.add("is-open");
        trigger.setAttribute("aria-expanded", "true");
        placePanel(openState);

        var selected = panel.querySelector(".nxcs-option.is-selected");
        if (selected) {
            selected.scrollIntoView({ block: "nearest" });
        }
    }

    function syncOne(select) {
        if (!select || !select._nxcsWrapper) return;

        var wrapper = select._nxcsWrapper;
        var trigger = wrapper.querySelector(".nxcs-trigger");
        var value = wrapper.querySelector(".nxcs-value");
        var selected = getSelectedOption(select);

        if (value) {
            value.textContent = optionText(selected);
            value.title = optionText(selected);
        }

        if (trigger) {
            trigger.disabled = select.disabled;
            trigger.setAttribute("aria-disabled", select.disabled ? "true" : "false");
        }

        wrapper.classList.toggle("is-disabled", !!select.disabled);
        wrapper.dataset.nxcsValue = select.value || "";
        wrapper.dataset.nxcsCount = String(select.options.length);
        wrapper.dataset.nxcsDisabled = select.disabled ? "true" : "false";

        if (openState && openState.select === select) {
            closeOpen();
        }
    }

    function enhance(select) {
        if (!isEligible(select)) return;
        if (select.dataset.nxcsEnhanced === "true") {
            syncOne(select);
            return;
        }

        var wrapper = document.createElement("span");
        wrapper.className = "nxcs-select";
        wrapper.dir = document.documentElement.dir || "rtl";

        var trigger = document.createElement("button");
        trigger.type = "button";
        trigger.className = "nxcs-trigger";
        trigger.setAttribute("aria-haspopup", "listbox");
        trigger.setAttribute("aria-expanded", "false");

        var value = document.createElement("span");
        value.className = "nxcs-value";

        var caret = document.createElement("span");
        caret.className = "nxcs-caret";
        caret.setAttribute("aria-hidden", "true");
        caret.textContent = "\u25BE";

        trigger.appendChild(value);
        trigger.appendChild(caret);
        wrapper.appendChild(trigger);

        select.classList.add("nxcs-native");
        select.dataset.nxcsEnhanced = "true";
        select._nxcsWrapper = wrapper;

        if (select.parentNode) {
            select.parentNode.insertBefore(wrapper, select.nextSibling);
        }

        trigger.addEventListener("click", function (event) {
            event.preventDefault();
            event.stopPropagation();
            openSelect(select);
        });

        trigger.addEventListener("keydown", function (event) {
            if (event.key === "Enter" || event.key === " " || event.key === "ArrowDown") {
                event.preventDefault();
                openSelect(select);
            }

            if (event.key === "Escape") {
                event.preventDefault();
                closeOpen();
            }
        });

        select.addEventListener("change", function () {
            syncOne(select);
        });

        syncOne(select);
    }

    function refreshAll() {
        document.querySelectorAll(SELECTOR).forEach(function (select) {
            enhance(select);
        });

        document.querySelectorAll("select[data-nxcs-enhanced='true']").forEach(function (select) {
            if (!select._nxcsWrapper || !document.body.contains(select)) return;
            var value = select.value || "";
            var count = String(select.options.length);
            var disabled = select.disabled ? "true" : "false";

            if (
                select._nxcsWrapper.dataset.nxcsValue !== value ||
                select._nxcsWrapper.dataset.nxcsCount !== count ||
                select._nxcsWrapper.dataset.nxcsDisabled !== disabled
            ) {
                syncOne(select);
            }
        });
    }

    function scheduleRefresh() {
        if (scheduleToken) return;

        scheduleToken = window.requestAnimationFrame(function () {
            scheduleToken = 0;
            refreshAll();
        });
    }

    document.addEventListener("click", function (event) {
        if (!openState) return;

        if (
            openState.wrapper.contains(event.target) ||
            (openState.panel && openState.panel.contains(event.target))
        ) {
            return;
        }

        closeOpen();
    }, true);

    document.addEventListener("keydown", function (event) {
        if (event.key === "Escape") {
            closeOpen();
        }
    }, true);

    window.addEventListener("resize", function () {
        if (openState) placePanel(openState);
    }, true);

    window.addEventListener("scroll", function () {
        if (openState) placePanel(openState);
    }, true);

    if (window.MutationObserver) {
        var observer = new MutationObserver(function (mutations) {
            var needsRefresh = false;

            mutations.forEach(function (mutation) {
                if (mutation.type === "childList") {
                    mutation.addedNodes.forEach(function (node) {
                        if (!node || node.nodeType !== 1) return;
                        if (node.matches && node.matches("select")) needsRefresh = true;
                        if (node.querySelector && node.querySelector("select")) needsRefresh = true;
                    });
                }

                if (mutation.type === "attributes" && mutation.target && mutation.target.tagName === "SELECT") {
                    needsRefresh = true;
                }
            });

            if (needsRefresh) scheduleRefresh();
        });

        observer.observe(document.documentElement, {
            childList: true,
            subtree: true,
            attributes: true,
            attributeFilter: ["disabled"]
        });
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", refreshAll);
    } else {
        refreshAll();
    }

    window.NexoraRefreshSelectSystem = refreshAll;
})();
/* NEXORA_FIX13A_GLOBAL_CUSTOM_SELECT_SYSTEM_END */