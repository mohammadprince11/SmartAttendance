// NEXORA Month Picker V1
// شقيق nexora-calendar-v18.js: يرقّي input[type="month"] لنفس الغلاف البصري (.nxcal)
// بلوحة شهر/سنة بلا شبكة أيام. سبب وجوده: محرك التقويم القانوني يلتقط input[type="date"]
// فقط، فكانت حقول الشهر تظهر بعنصر المتصفح الأصلي (LTR وبكروم مختلف) — مخالفة للستايل.
// يعيد استخدام أصناف nexora-calendar-v18.css كما هي فلا CSS جديد.
// NEXORA_MONTH_PICKER_V1_START

(() => {
    const SELECTOR = 'input[type="month"]:not([data-nexora-native-month="true"]):not([data-nexora-month-built="1"])';
    const MONTHS_AR = ["كانون الثاني", "شباط", "آذار", "نيسان", "أيار", "حزيران", "تموز", "آب", "أيلول", "تشرين الأول", "تشرين الثاني", "كانون الأول"];

    function pad(n) {
        return String(n).padStart(2, "0");
    }

    function parseValue(value) {
        if (!value) return null;
        const parts = String(value).split("-").map(Number);
        if (parts.length < 2 || parts.some(Number.isNaN)) return null;
        return { year: parts[0], month: parts[1] - 1 };
    }

    function toValue(year, month) {
        return `${year}-${pad(month + 1)}`;
    }

    function displayValue(value) {
        const parsed = parseValue(value);
        return parsed ? `${MONTHS_AR[parsed.month]} ${parsed.year}` : "اختر الشهر";
    }

    function closeAll(except = null) {
        document.querySelectorAll(".nxcal.is-open").forEach((picker) => {
            if (picker !== except) {
                picker.classList.remove("is-open");
                picker.querySelector(".nxcal__button")?.setAttribute("aria-expanded", "false");
            }
        });
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
        if (!input || input.dataset.nexoraMonthBuilt === "1") return;

        input.dataset.nexoraMonthBuilt = "1";

        const today = new Date();
        const initial = parseValue(input.value) || { year: today.getFullYear(), month: today.getMonth() };
        const state = { year: initial.year };

        let chooserMode = null;
        let yearPageStart = state.year - 5;

        const picker = document.createElement("div");
        picker.className = "nxcal nxcal--month";
        picker.dataset.nexoraMonthPickerVersion = "1";

        const button = document.createElement("button");
        button.type = "button";
        button.className = "nxcal__button";
        button.dataset.nexoraHover = "off";
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

        function setValue(next) {
            input.value = next || "";
            syncButton();
            input.dispatchEvent(new Event("input", { bubbles: true }));
            input.dispatchEvent(new Event("change", { bubbles: true }));
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
            const preferAbove = spaceBelow < 300 && spaceAbove > spaceBelow;
            const availableSpace = preferAbove ? spaceAbove : spaceBelow;
            const maxHeight = Math.max(160, Math.min(420, availableSpace));

            panel.style.setProperty("top", preferAbove ? "auto" : "calc(100% + 9px)", "important");
            panel.style.setProperty("bottom", preferAbove ? "calc(100% + 9px)" : "auto", "important");
            panel.style.setProperty("max-height", Math.round(maxHeight) + "px", "important");
            panel.style.setProperty("overflow-y", "auto", "important");
            picker.classList.toggle("is-above", preferAbove);
        }

        function render() {
            const selected = parseValue(input.value);

            panel.innerHTML = "";

            const head = document.createElement("div");
            head.className = "nxcal__head";

            const prevBtn = document.createElement("button");
            prevBtn.type = "button";
            prevBtn.className = "nxcal__nav";
            prevBtn.textContent = "‹";
            prevBtn.title = "السنة السابقة";

            const period = document.createElement("div");
            period.className = "nxcal__period";

            const yearBtn = document.createElement("button");
            yearBtn.type = "button";
            yearBtn.className = "nxcal__period-button nxcal__year-button";
            yearBtn.textContent = String(state.year);
            yearBtn.title = "اختيار السنة";
            yearBtn.dataset.nexoraHover = "off";
            yearBtn.setAttribute("aria-expanded", chooserMode === "year" ? "true" : "false");

            period.appendChild(yearBtn);

            const nextBtn = document.createElement("button");
            nextBtn.type = "button";
            nextBtn.className = "nxcal__nav";
            nextBtn.textContent = "›";
            nextBtn.title = "السنة التالية";

            prevBtn.addEventListener("click", (event) => {
                event.preventDefault();
                event.stopPropagation();
                chooserMode = null;
                state.year -= 1;
                render();
            });

            nextBtn.addEventListener("click", (event) => {
                event.preventDefault();
                event.stopPropagation();
                chooserMode = null;
                state.year += 1;
                render();
            });

            yearBtn.addEventListener("click", (event) => {
                event.preventDefault();
                event.stopPropagation();

                if (chooserMode !== "year") yearPageStart = state.year - 5;
                chooserMode = chooserMode === "year" ? null : "year";
                render();
            });

            head.appendChild(prevBtn);
            head.appendChild(period);
            head.appendChild(nextBtn);
            panel.appendChild(head);

            if (chooserMode === "year") {
                const chooser = document.createElement("div");
                chooser.className = "nxcal__chooser nxcal__year-chooser";

                const rangeHead = document.createElement("div");
                rangeHead.className = "nxcal__year-range-head";

                const previousRange = document.createElement("button");
                previousRange.type = "button";
                previousRange.className = "nxcal__range-nav";
                previousRange.textContent = "‹";
                previousRange.title = "السنوات السابقة";
                previousRange.dataset.nexoraHover = "off";

                const rangeLabel = document.createElement("div");
                rangeLabel.className = "nxcal__year-range-label";
                rangeLabel.textContent = `${yearPageStart} - ${yearPageStart + 11}`;

                const nextRange = document.createElement("button");
                nextRange.type = "button";
                nextRange.className = "nxcal__range-nav";
                nextRange.textContent = "›";
                nextRange.title = "السنوات التالية";
                nextRange.dataset.nexoraHover = "off";

                previousRange.addEventListener("click", (event) => {
                    event.preventDefault();
                    event.stopPropagation();
                    yearPageStart -= 12;
                    render();
                });

                nextRange.addEventListener("click", (event) => {
                    event.preventDefault();
                    event.stopPropagation();
                    yearPageStart += 12;
                    render();
                });

                rangeHead.appendChild(previousRange);
                rangeHead.appendChild(rangeLabel);
                rangeHead.appendChild(nextRange);

                const yearGrid = document.createElement("div");
                yearGrid.className = "nxcal__year-grid";

                for (let yearOffset = 0; yearOffset < 12; yearOffset++) {
                    const year = yearPageStart + yearOffset;
                    const option = document.createElement("button");
                    option.type = "button";
                    option.className = "nxcal__choice nxcal__year-choice";
                    option.textContent = String(year);
                    option.dataset.nexoraHover = "off";

                    if (year === state.year) option.classList.add("is-selected");

                    option.addEventListener("click", (event) => {
                        event.preventDefault();
                        event.stopPropagation();
                        state.year = year;
                        chooserMode = null;
                        render();
                    });

                    yearGrid.appendChild(option);
                }

                chooser.appendChild(rangeHead);
                chooser.appendChild(yearGrid);
                panel.appendChild(chooser);
                return;
            }

            const chooser = document.createElement("div");
            chooser.className = "nxcal__chooser nxcal__month-chooser";

            const monthGrid = document.createElement("div");
            monthGrid.className = "nxcal__month-grid";

            MONTHS_AR.forEach((monthName, monthIndex) => {
                const option = document.createElement("button");
                option.type = "button";
                option.className = "nxcal__choice";
                option.textContent = monthName;
                option.dataset.nexoraHover = "off";

                const isCurrent = monthIndex === today.getMonth() && state.year === today.getFullYear();
                if (isCurrent) option.classList.add("is-today");
                if (selected && selected.year === state.year && selected.month === monthIndex) {
                    option.classList.add("is-selected");
                }

                option.addEventListener("click", (event) => {
                    event.preventDefault();
                    event.stopPropagation();

                    setValue(toValue(state.year, monthIndex));
                    closeAll();
                    button.focus();
                });

                monthGrid.appendChild(option);
            });

            chooser.appendChild(monthGrid);
            panel.appendChild(chooser);

            const foot = document.createElement("div");
            foot.className = "nxcal__foot";

            const clear = document.createElement("button");
            clear.type = "button";
            clear.className = "nxcal__action";
            clear.textContent = "مسح";

            const currentBtn = document.createElement("button");
            currentBtn.type = "button";
            currentBtn.className = "nxcal__action";
            currentBtn.textContent = "الشهر الحالي";

            clear.addEventListener("click", (event) => {
                event.preventDefault();
                event.stopPropagation();
                setValue("");
                closeAll();
                button.focus();
            });

            currentBtn.addEventListener("click", (event) => {
                event.preventDefault();
                event.stopPropagation();
                const now = new Date();
                state.year = now.getFullYear();
                setValue(toValue(now.getFullYear(), now.getMonth()));
                closeAll();
                button.focus();
            });

            foot.appendChild(clear);
            foot.appendChild(currentBtn);
            panel.appendChild(foot);

            panel.querySelectorAll("button").forEach((control) => {
                control.dataset.nexoraHover = "off";
            });
        }

        button.addEventListener("click", (event) => {
            event.preventDefault();
            event.stopPropagation();

            if (input.disabled || input.readOnly) return;

            if (picker.classList.contains("is-open")) {
                closeAll();
            } else {
                closeAll(picker);

                const selected = parseValue(input.value);
                if (selected) state.year = selected.year;

                chooserMode = null;
                yearPageStart = state.year - 5;
                render();
                picker.classList.add("is-open");
                button.setAttribute("aria-expanded", "true");
                window.requestAnimationFrame(positionPanel);
            }
        });

        button.addEventListener("keydown", (event) => {
            if (event.key === "Escape") closeAll();
        });

        input.addEventListener("change", syncButton);
        input.addEventListener("input", syncButton);
        input.addEventListener("focus", () => button.focus());

        syncButton();
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
// NEXORA_MONTH_PICKER_V1_END
})();
