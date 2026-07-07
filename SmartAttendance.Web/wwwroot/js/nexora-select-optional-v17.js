(function () {
    "use strict";

    const SELECTOR = 'select:not([multiple]):not([data-nexora-native="true"])';
    const registry = new Map();
    let idCounter = 0;

    function getText(option) {
        return ((option && option.textContent) || (option && option.value) || "-").trim();
    }

    function visibleOptions(select) {
        return Array.from(select.options).filter(option => !option.hidden && !option.disabled);
    }

    function closeAll(except) {
        registry.forEach((state, select) => {
            if (select === except) return;

            state.root.classList.remove("is-open");
            state.button.setAttribute("aria-expanded", "false");
            state.menu.classList.remove("is-open");
        });
    }

    function positionMenu(select) {
        const state = registry.get(select);
        if (!state) return;

        const rect = state.button.getBoundingClientRect();
        const gap = 7;
        const vh = window.innerHeight || document.documentElement.clientHeight;
        const vw = window.innerWidth || document.documentElement.clientWidth;

        state.menu.style.minWidth = Math.max(170, Math.round(rect.width)) + "px";
        state.menu.style.maxWidth = Math.max(170, Math.round(rect.width)) + "px";

        state.menu.style.visibility = "hidden";
        state.menu.classList.add("is-open");

        const mr = state.menu.getBoundingClientRect();

        let top = rect.bottom + gap;

        if (top + mr.height > vh - 12) {
            top = rect.top - mr.height - gap;
        }

        if (top < 12) {
            top = Math.max(12, vh - mr.height - 12);
        }

        let left = rect.left;

        if (left + mr.width > vw - 12) {
            left = vw - mr.width - 12;
        }

        if (left < 12) {
            left = 12;
        }

        state.menu.style.top = Math.round(top) + "px";
        state.menu.style.left = Math.round(left) + "px";
        state.menu.style.visibility = "visible";
    }

    function sync(select) {
        const state = registry.get(select);
        if (!state) return;

        const selected = select.options[select.selectedIndex];
        const hasValue = selected && String(selected.value || "").length > 0;

        state.value.textContent = selected ? getText(selected) : "-";
        state.value.classList.toggle("is-placeholder", !hasValue);
        state.root.classList.toggle("is-disabled", select.disabled);

        state.menu.querySelectorAll(".nxos__option").forEach(item => {
            item.classList.toggle("is-selected", Number(item.dataset.index) === select.selectedIndex);
        });
    }

    function choose(select, optionIndex) {
        const option = select.options[optionIndex];

        if (!option || option.disabled || option.hidden || select.disabled) return;

        select.selectedIndex = optionIndex;

        select.dispatchEvent(new Event("input", { bubbles: true }));
        select.dispatchEvent(new Event("change", { bubbles: true }));

        rebuild(select);
        sync(select);
        closeAll();

        const state = registry.get(select);
        if (state) state.button.focus();
    }

    function markHover(menu, item) {
        menu.querySelectorAll(".nxos__option.is-hovered").forEach(x => x.classList.remove("is-hovered"));
        item.classList.add("is-hovered");
    }

    function rebuild(select) {
        const state = registry.get(select);
        if (!state) return;

        state.menu.innerHTML = "";

        const options = visibleOptions(select);

        if (options.length === 0) {
            const empty = document.createElement("div");
            empty.className = "nxos__option is-disabled";
            empty.textContent = "-";
            state.menu.appendChild(empty);
            sync(select);
            return;
        }

        options.forEach(option => {
            const item = document.createElement("div");
            item.className = "nxos__option";
            item.setAttribute("role", "option");
            item.dataset.index = String(option.index);
            item.dataset.value = option.value;
            item.textContent = getText(option);

            if (option.selected) {
                item.classList.add("is-selected");
            }

            item.addEventListener("mouseenter", () => {
                markHover(state.menu, item);
            });

            item.addEventListener("mousemove", () => {
                markHover(state.menu, item);
            });

            item.addEventListener("mouseleave", () => {
                item.classList.remove("is-hovered");
            });

            item.addEventListener("mousedown", event => {
                event.preventDefault();
                event.stopPropagation();
                choose(select, option.index);
            });

            item.addEventListener("click", event => {
                event.preventDefault();
                event.stopPropagation();
            });

            state.menu.appendChild(item);
        });

        sync(select);
    }

    function open(select) {
        const state = registry.get(select);
        if (!state || select.disabled) return;

        closeAll(select);
        rebuild(select);
        sync(select);

        state.root.classList.add("is-open");
        state.button.setAttribute("aria-expanded", "true");
        state.menu.classList.add("is-open");

        positionMenu(select);

        const selected = state.menu.querySelector(".nxos__option.is-selected");
        if (selected) {
            selected.scrollIntoView({ block: "nearest" });
        }
    }

    function destroyOldShell(select) {
        const next = select.nextElementSibling;

        if (next && next.classList && next.classList.contains("nxos")) {
            next.remove();
        }

        if (select.dataset.nxosId) {
            document.querySelectorAll(".nxos__menu[data-for-select='" + select.dataset.nxosId + "']").forEach(menu => menu.remove());
        }

        select.classList.remove("nxos-native");
        select.removeAttribute("data-nexora-built");
        select.removeAttribute("data-nxos-id");
    }

    function build(select) {
        if (!select || select.multiple || select.dataset.nexoraNative === "true") return;

        if (registry.has(select)) {
            rebuild(select);
            sync(select);
            return;
        }

        if (select.dataset.nexoraBuilt === "1") {
            destroyOldShell(select);
        }

        const id = "nxos_" + (++idCounter);
        select.dataset.nxosId = id;
        select.dataset.nexoraBuilt = "1";

        const root = document.createElement("div");
        root.className = "nxos";

        const button = document.createElement("button");
        button.type = "button";
        button.className = "nxos__button";
        button.setAttribute("aria-haspopup", "listbox");
        button.setAttribute("aria-expanded", "false");

        const value = document.createElement("span");
        value.className = "nxos__value";

        const arrow = document.createElement("span");
        arrow.className = "nxos__arrow";
        arrow.setAttribute("aria-hidden", "true");

        const menu = document.createElement("div");
        menu.className = "nxos__menu";
        menu.setAttribute("role", "listbox");
        menu.setAttribute("data-for-select", id);

        button.appendChild(value);
        button.appendChild(arrow);
        root.appendChild(button);

        select.insertAdjacentElement("afterend", root);
        document.body.appendChild(menu);

        select.classList.add("nxos-native");

        registry.set(select, { root, button, value, arrow, menu });

        rebuild(select);
        sync(select);

        button.addEventListener("mousedown", event => {
            event.preventDefault();
            event.stopPropagation();

            if (root.classList.contains("is-open")) {
                closeAll();
            } else {
                open(select);
            }
        });

        button.addEventListener("keydown", event => {
            if (event.key === "Escape") {
                closeAll();
                return;
            }

            if (event.key === "Enter" || event.key === " " || event.key === "ArrowDown") {
                event.preventDefault();
                open(select);
                return;
            }
        });

        select.addEventListener("change", () => {
            rebuild(select);
            sync(select);
        });
    }

    function refresh(target) {
        if (!target) {
            buildAll();
            return;
        }

        const select = typeof target === "string" ? document.querySelector(target) : target;

        if (!select) return;

        build(select);
        rebuild(select);
        sync(select);

        const state = registry.get(select);

        if (state && state.root.classList.contains("is-open")) {
            positionMenu(select);
        }
    }

    function buildAll() {
        document.querySelectorAll(SELECTOR).forEach(build);
    }

    window.NexoraSelect = {
        refresh: refresh,
        refreshAll: buildAll
    };

    document.addEventListener("mousedown", event => {
        if (event.target.closest(".nxos") || event.target.closest(".nxos__menu")) {
            return;
        }

        closeAll();
    }, true);

    document.addEventListener("keydown", event => {
        if (event.key === "Escape") {
            closeAll();
        }
    });

    window.addEventListener("resize", () => {
        registry.forEach((state, select) => {
            if (state.root.classList.contains("is-open")) {
                positionMenu(select);
            }
        });
    });

    window.addEventListener("scroll", () => {
        registry.forEach((state, select) => {
            if (state.root.classList.contains("is-open")) {
                positionMenu(select);
            }
        });
    }, true);

    document.addEventListener("DOMContentLoaded", () => {
        buildAll();
        setTimeout(buildAll, 250);
        setTimeout(buildAll, 900);
    });
})();