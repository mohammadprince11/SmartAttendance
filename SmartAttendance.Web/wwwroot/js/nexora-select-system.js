/* NEXORA_FIX15C_GLOBAL_SELECT_SYSTEM_START */
(function () {
    "use strict";

    var openState = null;
    var refreshToken = 0;
    var uid = 0;

    function closest(element, selector) {
        return element && element.closest ? element.closest(selector) : null;
    }

    function isNativeOnly(select) {
        if (!select) return true;
        if (select.multiple) return true;
        if (select.size && select.size > 1) return true;
        if (select.dataset.nexoraNativeOnly === "true") return true;
        if (select.dataset.nexoraNative === "true") return true;
        if (select.classList.contains("nxcs-skip")) return true;
        if (select.classList.contains("nexora-native-only")) return true;
        if (closest(select, "[data-nexora-native-selects='true']")) return true;
        return false;
    }

    function selectedOption(select) {
        if (!select) return null;
        return select.options[select.selectedIndex] || select.options[0] || null;
    }

    function optionText(option) {
        if (!option) return "";
        return (option.textContent || option.label || option.value || "").trim();
    }

    function setImportantStyle(element, name, value) {
        try {
            element.style.setProperty(name, value, "important");
        } catch (_) {
            element.style[name] = value;
        }
    }

    function hideNative(select) {
        select.classList.remove("nxos-native");
        select.classList.add("nxcs-native");
        select.dataset.nxcsEnhanced = "true";
        select.setAttribute("aria-hidden", "true");
        select.tabIndex = -1;

        setImportantStyle(select, "position", "absolute");
        setImportantStyle(select, "inset-inline-start", "-10000px");
        setImportantStyle(select, "left", "-10000px");
        setImportantStyle(select, "top", "auto");
        setImportantStyle(select, "width", "1px");
        setImportantStyle(select, "height", "1px");
        setImportantStyle(select, "min-width", "1px");
        setImportantStyle(select, "min-height", "1px");
        setImportantStyle(select, "max-width", "1px");
        setImportantStyle(select, "max-height", "1px");
        setImportantStyle(select, "opacity", "0");
        setImportantStyle(select, "pointer-events", "none");
        setImportantStyle(select, "overflow", "hidden");
        setImportantStyle(select, "clip-path", "inset(50%)");
        setImportantStyle(select, "margin", "0");
        setImportantStyle(select, "padding", "0");
        setImportantStyle(select, "border", "0");
        setImportantStyle(select, "z-index", "-1");
    }

    function removeLegacyShells() {
        document.querySelectorAll(".nxos__menu, .nxos").forEach(function (element) {
            element.remove();
        });

        document.querySelectorAll("select.nxos-native, select[data-nexora-built='1']").forEach(function (select) {
            select.classList.remove("nxos-native");
            delete select.dataset.nexoraBuilt;
            delete select.dataset.nxosId;
        });
    }

    function removeOrphanCustomShells() {
        document.querySelectorAll(".nxcs-select").forEach(function (wrapper) {
            var previous = wrapper.previousElementSibling;
            if (!previous || previous.tagName !== "SELECT" || previous._nxcsWrapper !== wrapper) {
                wrapper.remove();
            }
        });
    }

    function cleanupNear(select) {
        if (!select) return;

        var next = select.nextElementSibling;
        while (next && next.classList && (next.classList.contains("nxcs-select") || next.classList.contains("nxos"))) {
            var candidate = next;
            next = next.nextElementSibling;

            if (select._nxcsWrapper !== candidate) {
                candidate.remove();
            }
        }

        if (select._nxcsWrapper && !document.body.contains(select._nxcsWrapper)) {
            delete select._nxcsWrapper;
            delete select.dataset.nxcsEnhanced;
            select.classList.remove("nxcs-native");
        }
    }

    function sync(select) {
        if (!select || !select._nxcsWrapper) return;

        var wrapper = select._nxcsWrapper;
        var trigger = wrapper.querySelector(".nxcs-trigger");
        var value = wrapper.querySelector(".nxcs-value");
        var option = selectedOption(select);

        if (value) {
            value.textContent = optionText(option);
            value.title = optionText(option);
        }

        if (trigger) {
            trigger.disabled = !!select.disabled;
            trigger.setAttribute("aria-disabled", select.disabled ? "true" : "false");
        }

        wrapper.classList.toggle("is-disabled", !!select.disabled);
        wrapper.dataset.nxcsValue = select.value || "";
        wrapper.dataset.nxcsCount = String(select.options.length);
        wrapper.dataset.nxcsDisabled = select.disabled ? "true" : "false";

        hideNative(select);
    }

    function buildPanel(select, wrapper, trigger) {
        var panel = document.createElement("div");
        panel.className = "nxcs-panel";
        panel.dir = document.documentElement.dir || "rtl";
        panel.id = "nxcs-panel-" + (++uid);
        panel.setAttribute("role", "listbox");

        Array.prototype.forEach.call(select.options, function (option, index) {
            var item = document.createElement("button");
            item.type = "button";
            item.className = "nxcs-option";
            item.setAttribute("role", "option");
            item.dataset.value = option.value;
            item.textContent = optionText(option);
            item.disabled = !!option.disabled;

            if (index === select.selectedIndex) {
                item.classList.add("is-selected");
                item.setAttribute("aria-selected", "true");
            }

            item.addEventListener("click", function (event) {
                event.preventDefault();
                event.stopPropagation();

                if (item.disabled) return;

                select.selectedIndex = index;
                select.value = option.value;
                select.dispatchEvent(new Event("input", { bubbles: true }));
                select.dispatchEvent(new Event("change", { bubbles: true }));
                sync(select);
                closeOpen();
            });

            panel.appendChild(item);
        });

        trigger.setAttribute("aria-controls", panel.id);
        return panel;
    }

    function placePanel(state) {
        if (!state || !state.panel || !state.trigger) return;

        var triggerRect = state.trigger.getBoundingClientRect();
        var viewportWidth = document.documentElement.clientWidth || window.innerWidth;
        var viewportHeight = document.documentElement.clientHeight || window.innerHeight;
        var gap = 8;
        var edge = 12;

        var width = Math.max(triggerRect.width, 180);
        width = Math.min(width, viewportWidth - (edge * 2));

        var spaceBelow = Math.max(0, viewportHeight - triggerRect.bottom - gap - edge);
        var spaceAbove = Math.max(0, triggerRect.top - gap - edge);

        var preferBelow = spaceBelow >= 220 || spaceBelow >= spaceAbove;
        var availableSpace = preferBelow ? spaceBelow : spaceAbove;

        if (availableSpace < 160 && spaceAbove > spaceBelow) {
            preferBelow = false;
            availableSpace = spaceAbove;
        }

        var maxPanelHeight = Math.max(150, Math.min(340, availableSpace, viewportHeight - (edge * 2)));

        state.panel.style.visibility = "hidden";
        state.panel.style.display = "block";
        state.panel.style.width = Math.round(width) + "px";
        state.panel.style.maxHeight = Math.round(maxPanelHeight) + "px";

        var measuredHeight = state.panel.getBoundingClientRect().height || maxPanelHeight;
        var height = Math.min(measuredHeight, maxPanelHeight);

        var left = document.documentElement.dir === "rtl"
            ? triggerRect.right - width
            : triggerRect.left;

        left = Math.max(edge, Math.min(left, viewportWidth - width - edge));

        var top = preferBelow
            ? triggerRect.bottom + gap
            : triggerRect.top - height - gap;

        top = Math.max(edge, Math.min(top, viewportHeight - height - edge));

        state.panel.style.left = Math.round(left) + "px";
        state.panel.style.top = Math.round(top) + "px";
        state.panel.style.visibility = "visible";
    }

    function closeOpen() {
        if (!openState) return;

        if (openState.wrapper) {
            openState.wrapper.classList.remove("is-open");
        }

        if (openState.trigger) {
            openState.trigger.setAttribute("aria-expanded", "false");
        }

        if (openState.panel) {
            openState.panel.remove();
        }

        openState = null;
    }

    function openSelect(select) {
        if (!select || !select._nxcsWrapper || select.disabled) return;

        if (openState && openState.select === select) {
            closeOpen();
            return;
        }

        closeOpen();

        var wrapper = select._nxcsWrapper;
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

    function enhance(select) {
        if (!select || isNativeOnly(select)) return;

        cleanupNear(select);

        if (select.dataset.nxcsEnhanced === "true" && select._nxcsWrapper && document.body.contains(select._nxcsWrapper)) {
            sync(select);
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

        select._nxcsWrapper = wrapper;
        hideNative(select);

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
            sync(select);
        });

        sync(select);
    }

    function refreshAll() {
        removeLegacyShells();
        removeOrphanCustomShells();

        document.querySelectorAll("select").forEach(function (select) {
            enhance(select);
        });

        document.querySelectorAll("select[data-nxcs-enhanced='true']").forEach(function (select) {
            sync(select);
        });
    }

    function scheduleRefresh() {
        if (refreshToken) return;

        refreshToken = window.requestAnimationFrame(function () {
            refreshToken = 0;
            refreshAll();
        });
    }

    document.addEventListener("click", function (event) {
        if (!openState) return;

        if (openState.panel && openState.panel.contains(event.target)) return;
        if (openState.wrapper && openState.wrapper.contains(event.target)) return;

        closeOpen();
    }, true);

    document.addEventListener("keydown", function (event) {
        if (event.key === "Escape") closeOpen();
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
                    needsRefresh = true;
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
            attributeFilter: ["disabled", "class", "style"]
        });
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", refreshAll);
    } else {
        refreshAll();
    }

    setTimeout(refreshAll, 50);
    setTimeout(refreshAll, 200);
    setTimeout(refreshAll, 700);
    setTimeout(refreshAll, 1500);

    window.NexoraRefreshSelectSystem = refreshAll;
})();
/* NEXORA_FIX15C_GLOBAL_SELECT_SYSTEM_END */