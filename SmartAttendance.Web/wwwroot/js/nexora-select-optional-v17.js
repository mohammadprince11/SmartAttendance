// NEXORA Optional Select V17.1
// Only converts <select data-nexora-select="true">.
// Normal selects are untouched.

(() => {
    const SELECTOR = 'select[data-nexora-select="true"]:not([multiple]):not([data-nexora-built="1"])';

    function textOf(option) {
        return (option?.textContent || option?.value || "لا شيء").trim();
    }

    function closeAll(except = null) {
        document.querySelectorAll(".nxos.is-open").forEach((dropdown) => {
            if (dropdown !== except) {
                dropdown.classList.remove("is-open");
                dropdown.querySelector(".nxos__button")?.setAttribute("aria-expanded", "false");
            }
        });
    }

    function rebuildOptions(select, dropdown) {
        const menu = dropdown.querySelector(".nxos__menu");
        if (!menu) return;

        menu.innerHTML = "";

        Array.from(select.options).forEach((option, index) => {
            const item = document.createElement("div");
            item.className = "nxos__option";
            item.dataset.index = String(index);
            item.setAttribute("role", "option");
            item.textContent = textOf(option);

            if (option.disabled) {
                item.classList.add("is-disabled");
                item.setAttribute("aria-disabled", "true");
            }

            item.addEventListener("click", () => {
                if (option.disabled || select.disabled) return;

                select.selectedIndex = index;
                select.value = option.value;

                select.dispatchEvent(new Event("input", { bubbles: true }));
                select.dispatchEvent(new Event("change", { bubbles: true }));

                sync(select, dropdown);
                closeAll();
                dropdown.querySelector(".nxos__button")?.focus();
            });

            menu.appendChild(item);
        });

        dropdown.dataset.optionCount = String(select.options.length);
    }

    function sync(select, dropdown) {
        const value = dropdown.querySelector(".nxos__value");
        const selected = select.options[select.selectedIndex];

        if (value) value.textContent = selected ? textOf(selected) : "لا شيء";

        dropdown.classList.toggle("is-disabled", select.disabled);

        dropdown.querySelectorAll(".nxos__option").forEach((item) => {
            item.classList.toggle("is-selected", Number(item.dataset.index) === select.selectedIndex);
        });
    }

    function openDropdown(select, dropdown) {
        closeAll(dropdown);

        if (Number(dropdown.dataset.optionCount || "0") !== select.options.length) {
            rebuildOptions(select, dropdown);
        }

        dropdown.classList.add("is-open");
        dropdown.querySelector(".nxos__button")?.setAttribute("aria-expanded", "true");
        sync(select, dropdown);

        const selected = dropdown.querySelector(".nxos__option.is-selected");
        if (selected) selected.scrollIntoView({ block: "nearest" });
    }

    function markParents(dropdown) {
        let node = dropdown.parentElement;
        let depth = 0;

        while (node && node !== document.body && depth < 6) {
            node.classList.add("nxos-ready");
            node = node.parentElement;
            depth++;
        }
    }

    function build(select) {
        if (!select || select.multiple || select.dataset.nexoraBuilt === "1") return;

        select.dataset.nexoraBuilt = "1";

        const dropdown = document.createElement("div");
        dropdown.className = "nxos";
        dropdown.dataset.for = select.name || select.id || "";

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

        button.appendChild(value);
        button.appendChild(arrow);
        dropdown.appendChild(button);
        dropdown.appendChild(menu);

        select.insertAdjacentElement("afterend", dropdown);
        select.classList.add("nxos-native");
        markParents(dropdown);

        rebuildOptions(select, dropdown);
        sync(select, dropdown);

        button.addEventListener("click", (event) => {
            event.preventDefault();
            event.stopPropagation();

            if (select.disabled) return;

            if (dropdown.classList.contains("is-open")) {
                closeAll();
            } else {
                openDropdown(select, dropdown);
            }
        });

        button.addEventListener("keydown", (event) => {
            const key = event.key;

            if (key === "Escape") {
                closeAll();
                return;
            }

            if (!["ArrowDown", "ArrowUp", "Enter", " "].includes(key)) return;

            event.preventDefault();

            if (!dropdown.classList.contains("is-open")) {
                openDropdown(select, dropdown);
                return;
            }

            const items = Array.from(dropdown.querySelectorAll(".nxos__option:not(.is-disabled)"));
            if (!items.length) return;

            let currentIndex = items.findIndex((item) => item.classList.contains("is-active"));
            if (currentIndex < 0) {
                currentIndex = items.findIndex((item) => item.classList.contains("is-selected"));
            }

            if (key === "ArrowDown") currentIndex = Math.min(items.length - 1, currentIndex + 1);
            if (key === "ArrowUp") currentIndex = Math.max(0, currentIndex - 1);

            items.forEach((item) => item.classList.remove("is-active"));

            const current = items[currentIndex >= 0 ? currentIndex : 0];
            current.classList.add("is-active");
            current.scrollIntoView({ block: "nearest" });

            if (key === "Enter" || key === " ") current.click();
        });

        select.addEventListener("change", () => {
            if (Number(dropdown.dataset.optionCount || "0") !== select.options.length) {
                rebuildOptions(select, dropdown);
            }
            sync(select, dropdown);
        });

        select.addEventListener("focus", () => button.focus());
    }

    function buildAll() {
        document.querySelectorAll(SELECTOR).forEach(build);
    }

    document.addEventListener("click", (event) => {
        if (!event.target.closest(".nxos")) closeAll();
    });

    document.addEventListener("keydown", (event) => {
        if (event.key === "Escape") closeAll();
    });

    window.addEventListener("resize", closeAll, { passive: true });
    window.addEventListener("scroll", closeAll, true);

    document.addEventListener("DOMContentLoaded", () => {
        buildAll();
        setTimeout(buildAll, 250);
        setTimeout(buildAll, 900);
    });

    new MutationObserver(() => {
        window.requestAnimationFrame(buildAll);
    }).observe(document.documentElement, {
        childList: true,
        subtree: true
    });
})();

