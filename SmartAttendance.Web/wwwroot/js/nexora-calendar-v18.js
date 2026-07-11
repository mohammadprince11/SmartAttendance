// NEXORA Calendar V18.5
// Arrows-only month navigation. Display format: yyyy-MM-dd.

(() => {
    const SELECTOR = 'input[type="date"]:not([data-nexora-native-date="true"]):not([data-nexora-calendar-built="1"])';
    const MONTHS_AR = ["كانون الثاني", "شباط", "آذار", "نيسان", "أيار", "حزيران", "تموز", "آب", "أيلول", "تشرين الأول", "تشرين الثاني", "كانون الأول"];
    const WEEK_AR = ["أحد", "اثن", "ثلا", "أرب", "خمي", "جمع", "سبت"];

    function pad(n) {
        return String(n).padStart(2, "0");
    }

    function toIso(date) {
        return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
    }

    function parseIso(value) {
        if (!value) return null;
        const parts = value.split("-").map(Number);
        if (parts.length !== 3 || parts.some(Number.isNaN)) return null;
        return new Date(parts[0], parts[1] - 1, parts[2]);
    }

    function displayValue(value) {
        return value || "yyyy-mm-dd";
    }

    function closeAll(except = null) {
        document.querySelectorAll(".nxcal.is-open").forEach((picker) => {
            if (picker !== except) {
                picker.classList.remove("is-open");
                picker.querySelector(".nxcal__button")?.setAttribute("aria-expanded", "false");
            }
        });
    }

    function monthStart(date) {
        return new Date(date.getFullYear(), date.getMonth(), 1);
    }

    function sameDate(a, b) {
        return a && b &&
            a.getFullYear() === b.getFullYear() &&
            a.getMonth() === b.getMonth() &&
            a.getDate() === b.getDate();
    }

    function markParents(node) {
        let current = node.parentElement;
        let depth = 0;

        while (current && current !== document.body && depth < 6) {
            current.classList.add("nxcal-ready");
            current = current.parentElement;
            depth++;
        }
    }

    function build(input) {
        if (!input || input.dataset.nexoraCalendarBuilt === "1") return;

        input.dataset.nexoraCalendarBuilt = "1";

        const initialDate = parseIso(input.value) || new Date();
        const state = {
            view: monthStart(initialDate)
        };

        const picker = document.createElement("div");
        picker.className = "nxcal";

        const button = document.createElement("button");
        button.type = "button";
        button.className = "nxcal__button";
        button.setAttribute("aria-haspopup", "dialog");
        button.setAttribute("aria-expanded", "false");

        const icon = document.createElement("span");
        icon.className = "nxcal__icon";
        icon.textContent = "◷";

        const value = document.createElement("span");
        value.className = "nxcal__value";

        const spacer = document.createElement("span");
        spacer.className = "nxcal__spacer";

        const panel = document.createElement("div");
        panel.className = "nxcal__panel";
        panel.setAttribute("role", "dialog");

        button.appendChild(icon);
        button.appendChild(value);
        button.appendChild(spacer);
        picker.appendChild(button);
        picker.appendChild(panel);

        input.insertAdjacentElement("afterend", picker);
        input.classList.add("nxcal-native");
        markParents(picker);

        function setValue(iso) {
            input.value = iso || "";
            input.dispatchEvent(new Event("input", { bubbles: true }));
            input.dispatchEvent(new Event("change", { bubbles: true }));
            syncButton();
        }

        function syncButton() {
            value.textContent = displayValue(input.value);
        }


        function positionPanel() {
            if (!picker.classList.contains("is-open")) return;

            const buttonRect = button.getBoundingClientRect();
            const viewportHeight = document.documentElement.clientHeight || window.innerHeight;
            const edge = 12;
            const gap = 9;
            const spaceBelow = Math.max(0, viewportHeight - buttonRect.bottom - gap - edge);
            const spaceAbove = Math.max(0, buttonRect.top - gap - edge);
            const preferAbove = spaceBelow < 360 && spaceAbove > spaceBelow;
            const availableSpace = preferAbove ? spaceAbove : spaceBelow;
            const maxHeight = Math.max(160, Math.min(420, availableSpace));

            panel.style.setProperty("top", preferAbove ? "auto" : "calc(100% + 9px)", "important");
            panel.style.setProperty("bottom", preferAbove ? "calc(100% + 9px)" : "auto", "important");
            panel.style.setProperty("max-height", Math.round(maxHeight) + "px", "important");
            panel.style.setProperty("overflow-y", "auto", "important");
            picker.classList.toggle("is-above", preferAbove);
        }
function changeMonth(delta) {
            state.view = new Date(state.view.getFullYear(), state.view.getMonth() + delta, 1);
            render();
        }

        function render() {
            const selected = parseIso(input.value);
            const today = new Date();

            panel.innerHTML = "";

            const head = document.createElement("div");
            head.className = "nxcal__head";

            const prevBtn = document.createElement("button");
            prevBtn.type = "button";
            prevBtn.className = "nxcal__nav";
            prevBtn.textContent = "‹";
            prevBtn.title = "الشهر السابق";

            const month = document.createElement("div");
            month.className = "nxcal__month";
            month.textContent = `${MONTHS_AR[state.view.getMonth()]} ${state.view.getFullYear()}`;

            const nextBtn = document.createElement("button");
            nextBtn.type = "button";
            nextBtn.className = "nxcal__nav";
            nextBtn.textContent = "›";
            nextBtn.title = "الشهر التالي";

            prevBtn.addEventListener("click", (event) => {
                event.preventDefault();
                event.stopPropagation();
                changeMonth(-1);
            });

            nextBtn.addEventListener("click", (event) => {
                event.preventDefault();
                event.stopPropagation();
                changeMonth(1);
            });

            head.appendChild(prevBtn);
            head.appendChild(month);
            head.appendChild(nextBtn);

            const week = document.createElement("div");
            week.className = "nxcal__week";

            WEEK_AR.forEach((w) => {
                const el = document.createElement("div");
                el.className = "nxcal__weekday";
                el.textContent = w;
                week.appendChild(el);
            });

            const grid = document.createElement("div");
            grid.className = "nxcal__grid";

            const first = new Date(state.view.getFullYear(), state.view.getMonth(), 1);
            const start = new Date(first);
            start.setDate(first.getDate() - first.getDay());

            for (let i = 0; i < 42; i++) {
                const day = new Date(start);
                day.setDate(start.getDate() + i);

                const cell = document.createElement("button");
                cell.type = "button";
                cell.className = "nxcal__day";
                cell.textContent = String(day.getDate());

                if (day.getMonth() !== state.view.getMonth()) cell.classList.add("is-muted");
                if (sameDate(day, today)) cell.classList.add("is-today");
                if (sameDate(day, selected)) cell.classList.add("is-selected");

                cell.addEventListener("click", (event) => {
                    event.preventDefault();
                    event.stopPropagation();

                    setValue(toIso(day));
                    state.view = monthStart(day);
                    render();
                    closeAll();
                    button.focus();
                });

                grid.appendChild(cell);
            }

            const foot = document.createElement("div");
            foot.className = "nxcal__foot";

            const clear = document.createElement("button");
            clear.type = "button";
            clear.className = "nxcal__action";
            clear.textContent = "مسح";

            const todayBtn = document.createElement("button");
            todayBtn.type = "button";
            todayBtn.className = "nxcal__action";
            todayBtn.textContent = "اليوم";

            clear.addEventListener("click", (event) => {
                event.preventDefault();
                event.stopPropagation();

                setValue("");
                render();
                closeAll();
                button.focus();
            });

            todayBtn.addEventListener("click", (event) => {
                event.preventDefault();
                event.stopPropagation();

                const d = new Date();
                state.view = monthStart(d);
                setValue(toIso(d));
                render();
                closeAll();
                button.focus();
            });

            foot.appendChild(clear);
            foot.appendChild(todayBtn);

            panel.appendChild(head);
            panel.appendChild(week);
            panel.appendChild(grid);
            panel.appendChild(foot);
        }

        button.addEventListener("click", (event) => {
            event.preventDefault();
            event.stopPropagation();

            if (input.disabled || input.readOnly) return;

            if (picker.classList.contains("is-open")) {
                closeAll();
            } else {
                closeAll(picker);

                const selected = parseIso(input.value);
                if (selected) state.view = monthStart(selected);

                render();
                picker.classList.add("is-open");
                button.setAttribute("aria-expanded", "true");
                window.requestAnimationFrame(positionPanel);
            }
        });

        button.addEventListener("keydown", (event) => {
            if (event.key === "Escape") {
                closeAll();
            }

            if (event.key === "ArrowLeft") {
                event.preventDefault();
                if (!picker.classList.contains("is-open")) {
                    button.click();
                } else {
                    changeMonth(-1);
                }
            }

            if (event.key === "ArrowRight") {
                event.preventDefault();
                if (!picker.classList.contains("is-open")) {
                    button.click();
                } else {
                    changeMonth(1);
                }
            }
        });

        input.addEventListener("change", syncButton);
        input.addEventListener("input", syncButton);
        input.addEventListener("focus", () => button.focus());

        syncButton();
        render();
    }

    function buildAll() {
        document.querySelectorAll(SELECTOR).forEach(build);
    }

    document.addEventListener("click", (event) => {
        if (!event.target.closest(".nxcal")) closeAll();
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
