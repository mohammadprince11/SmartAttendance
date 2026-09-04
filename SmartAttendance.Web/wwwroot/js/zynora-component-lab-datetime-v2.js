(() => {
    "use strict";

    function initZynoraDateTimeLab() {

        const root = document.getElementById("datetime");

        if (!root) {
            console.warn("ZYNORA DateTime Lab: #datetime not found.");
            return;
        }

        const MONTHS = [
            "كانون الثاني",
            "شباط",
            "آذار",
            "نيسان",
            "أيار",
            "حزيران",
            "تموز",
            "آب",
            "أيلول",
            "تشرين الأول",
            "تشرين الثاني",
            "كانون الأول"
        ];

        const pad = value =>
            String(value).padStart(2, "0");


        /* =====================================================
           SHARED OPEN / CLOSE
           ===================================================== */

        function closeAll(except = null) {

            root.querySelectorAll(".zdt2-field").forEach(field => {

                if (field === except) {
                    return;
                }

                field.classList.remove("is-open");

                const panel =
                    field.querySelector(".zdt2-popover");

                const trigger =
                    field.querySelector(".zdt2-trigger");

                if (panel) {
                    panel.hidden = true;
                }

                if (trigger) {
                    trigger.setAttribute(
                        "aria-expanded",
                        "false"
                    );
                }
            });
        }


        function wireField(field) {

            if (!field) {
                return;
            }

            const trigger =
                field.querySelector(".zdt2-trigger");

            const panel =
                field.querySelector(".zdt2-popover");

            if (!trigger || !panel) {
                return;
            }

            trigger.setAttribute(
                "aria-expanded",
                "false"
            );

            panel.hidden = true;

            trigger.addEventListener("click", event => {

                event.preventDefault();
                event.stopPropagation();

                const shouldOpen =
                    !field.classList.contains("is-open");

                closeAll(field);

                field.classList.toggle(
                    "is-open",
                    shouldOpen
                );

                panel.hidden =
                    !shouldOpen;

                trigger.setAttribute(
                    "aria-expanded",
                    shouldOpen
                        ? "true"
                        : "false"
                );
            });

            panel.addEventListener("click", event => {
                event.stopPropagation();
            });
        }


        document.addEventListener("click", event => {

            if (
                !event.target.closest(
                    "#datetime .zdt2-field"
                )
            ) {
                closeAll();
            }
        });


        document.addEventListener("keydown", event => {

            if (event.key === "Escape") {
                closeAll();
            }
        });



        /* =====================================================
           DATE PICKER
           ===================================================== */

        const dateField =
            root.querySelector(".zdt2-date");

        if (dateField) {

            wireField(dateField);

            const valueEl =
                dateField.querySelector(".zdt2-value");

            const monthLabel =
                dateField.querySelector(
                    "[data-date-month-label]"
                );

            const yearLabel =
                dateField.querySelector(
                    "[data-date-year-label]"
                );

            const weekEl =
                dateField.querySelector(".zdt2-week");

            const daysEl =
                dateField.querySelector(
                    "[data-date-days]"
                );

            const prevBtn =
                dateField.querySelector(
                    "[data-date-prev]"
                );

            const nextBtn =
                dateField.querySelector(
                    "[data-date-next]"
                );

            const todayBtn =
                dateField.querySelector(
                    "[data-date-today]"
                );

            const clearBtn =
                dateField.querySelector(
                    "[data-date-clear]"
                );


            const today =
                new Date();

            let viewYear =
                today.getFullYear();

            let viewMonth =
                today.getMonth();

            let selectedDate =
                null;

            let mode =
                "days";

            let yearPageStart =
                viewYear - 5;


            function formatDate(date) {

                return (
                    date.getFullYear() +
                    "-" +
                    pad(date.getMonth() + 1) +
                    "-" +
                    pad(date.getDate())
                );
            }


            function sameDay(a, b) {

                return (
                    a &&
                    b &&
                    a.getFullYear() === b.getFullYear() &&
                    a.getMonth() === b.getMonth() &&
                    a.getDate() === b.getDate()
                );
            }


            function syncDateHeader() {

                if (monthLabel) {
                    monthLabel.textContent =
                        MONTHS[viewMonth];
                }

                if (yearLabel) {
                    yearLabel.textContent =
                        String(viewYear);
                }
            }


            function clearDateGrid(className) {

                daysEl.innerHTML = "";

                daysEl.className =
                    className;
            }


            /* -----------------------------------------------
               DAYS
               ----------------------------------------------- */

            function renderDays() {

                mode = "days";

                syncDateHeader();

                weekEl.hidden = false;

                clearDateGrid(
                    "zdt2-days"
                );


                const firstDay =
                    new Date(
                        viewYear,
                        viewMonth,
                        1
                    );

                const firstWeekDay =
                    firstDay.getDay();

                const gridStart =
                    new Date(
                        viewYear,
                        viewMonth,
                        1 - firstWeekDay
                    );


                for (let i = 0; i < 42; i++) {

                    const current =
                        new Date(
                            gridStart.getFullYear(),
                            gridStart.getMonth(),
                            gridStart.getDate() + i
                        );


                    const button =
                        document.createElement(
                            "button"
                        );

                    button.type =
                        "button";

                    button.className =
                        "zdt2-day";

                    button.textContent =
                        String(
                            current.getDate()
                        );


                    if (
                        current.getMonth() !==
                        viewMonth
                    ) {
                        button.classList.add(
                            "is-muted"
                        );
                    }


                    if (
                        sameDay(
                            current,
                            today
                        )
                    ) {
                        button.classList.add(
                            "is-today"
                        );
                    }


                    if (
                        sameDay(
                            current,
                            selectedDate
                        )
                    ) {
                        button.classList.add(
                            "is-selected"
                        );
                    }


                    button.addEventListener(
                        "click",
                        event => {

                            event.preventDefault();
                            event.stopPropagation();

                            selectedDate =
                                new Date(current);

                            viewYear =
                                selectedDate.getFullYear();

                            viewMonth =
                                selectedDate.getMonth();

                            valueEl.textContent =
                                formatDate(
                                    selectedDate
                                );

                            renderDays();

                            closeAll();
                        }
                    );


                    daysEl.appendChild(
                        button
                    );
                }
            }


            /* -----------------------------------------------
               MONTH CHOOSER
               ----------------------------------------------- */

            function renderDateMonths() {

                mode = "months";

                syncDateHeader();

                weekEl.hidden = true;

                clearDateGrid(
                    "zdt2-date-month-grid"
                );


                MONTHS.forEach(
                    (name, index) => {

                        const button =
                            document.createElement(
                                "button"
                            );

                        button.type =
                            "button";

                        button.className =
                            "zdt2-date-month-choice";

                        button.textContent =
                            name;


                        if (
                            index ===
                            viewMonth
                        ) {
                            button.classList.add(
                                "is-selected"
                            );
                        }


                        if (
                            viewYear === today.getFullYear() &&
                            index === today.getMonth()
                        ) {
                            button.classList.add(
                                "is-today"
                            );
                        }


                        button.addEventListener(
                            "click",
                            event => {

                                event.preventDefault();
                                event.stopPropagation();

                                viewMonth =
                                    index;

                                renderDays();
                            }
                        );


                        daysEl.appendChild(
                            button
                        );
                    }
                );
            }


            /* -----------------------------------------------
               YEAR CHOOSER
               ----------------------------------------------- */

            function renderYears() {

                mode = "years";

                syncDateHeader();

                weekEl.hidden = true;

                clearDateGrid(
                    "zdt2-date-year-area"
                );


                const range =
                    document.createElement(
                        "div"
                    );

                range.className =
                    "zdt2-year-range";


                const previous =
                    document.createElement(
                        "button"
                    );

                previous.type =
                    "button";

                previous.className =
                    "zdt2-year-range-nav";

                previous.textContent =
                    "‹";


                const rangeTitle =
                    document.createElement(
                        "strong"
                    );

                rangeTitle.textContent =
                    `${yearPageStart} – ${yearPageStart + 11}`;


                const next =
                    document.createElement(
                        "button"
                    );

                next.type =
                    "button";

                next.className =
                    "zdt2-year-range-nav";

                next.textContent =
                    "›";


                previous.addEventListener(
                    "click",
                    event => {

                        event.preventDefault();
                        event.stopPropagation();

                        yearPageStart -=
                            12;

                        renderYears();
                    }
                );


                next.addEventListener(
                    "click",
                    event => {

                        event.preventDefault();
                        event.stopPropagation();

                        yearPageStart +=
                            12;

                        renderYears();
                    }
                );


                range.append(
                    previous,
                    rangeTitle,
                    next
                );


                const grid =
                    document.createElement(
                        "div"
                    );

                grid.className =
                    "zdt2-date-year-grid";


                for (
                    let i = 0;
                    i < 12;
                    i++
                ) {

                    const year =
                        yearPageStart + i;


                    const button =
                        document.createElement(
                            "button"
                        );

                    button.type =
                        "button";

                    button.className =
                        "zdt2-date-year-choice";

                    button.textContent =
                        String(year);


                    if (
                        year ===
                        viewYear
                    ) {
                        button.classList.add(
                            "is-selected"
                        );
                    }


                    if (
                        year ===
                        today.getFullYear()
                    ) {
                        button.classList.add(
                            "is-today"
                        );
                    }


                    button.addEventListener(
                        "click",
                        event => {

                            event.preventDefault();
                            event.stopPropagation();

                            viewYear =
                                year;

                            /*
                             * السنة -> الشهر -> اليوم
                             */
                            renderDateMonths();
                        }
                    );


                    grid.appendChild(
                        button
                    );
                }


                daysEl.append(
                    range,
                    grid
                );
            }


            /* -----------------------------------------------
               CLICK MONTH / YEAR
               ----------------------------------------------- */

            if (monthLabel) {

                monthLabel.addEventListener(
                    "click",
                    event => {

                        event.preventDefault();
                        event.stopPropagation();

                        renderDateMonths();
                    }
                );
            }


            if (yearLabel) {

                yearLabel.addEventListener(
                    "click",
                    event => {

                        event.preventDefault();
                        event.stopPropagation();

                        yearPageStart =
                            viewYear - 5;

                        renderYears();
                    }
                );
            }


            /* -----------------------------------------------
               MAIN ARROWS
               ----------------------------------------------- */

            prevBtn?.addEventListener(
                "click",
                event => {

                    event.preventDefault();
                    event.stopPropagation();


                    if (mode === "years") {

                        yearPageStart -= 12;

                        renderYears();

                        return;
                    }


                    if (mode === "months") {

                        viewYear--;

                        renderDateMonths();

                        return;
                    }


                    viewMonth--;


                    if (viewMonth < 0) {

                        viewMonth = 11;

                        viewYear--;
                    }


                    renderDays();
                }
            );


            nextBtn?.addEventListener(
                "click",
                event => {

                    event.preventDefault();
                    event.stopPropagation();


                    if (mode === "years") {

                        yearPageStart += 12;

                        renderYears();

                        return;
                    }


                    if (mode === "months") {

                        viewYear++;

                        renderDateMonths();

                        return;
                    }


                    viewMonth++;


                    if (viewMonth > 11) {

                        viewMonth = 0;

                        viewYear++;
                    }


                    renderDays();
                }
            );


            /* -----------------------------------------------
               TODAY / CLEAR
               ----------------------------------------------- */

            todayBtn?.addEventListener(
                "click",
                event => {

                    event.preventDefault();
                    event.stopPropagation();

                    selectedDate =
                        new Date();

                    viewYear =
                        selectedDate.getFullYear();

                    viewMonth =
                        selectedDate.getMonth();

                    valueEl.textContent =
                        formatDate(
                            selectedDate
                        );

                    renderDays();

                    closeAll();
                }
            );


            clearBtn?.addEventListener(
                "click",
                event => {

                    event.preventDefault();
                    event.stopPropagation();

                    selectedDate =
                        null;

                    valueEl.textContent =
                        "اختر التاريخ";

                    renderDays();

                    closeAll();
                }
            );


            renderDays();
        }



        /* =====================================================
           MONTH PICKER
           ===================================================== */

        const monthField =
            root.querySelector(".zdt2-month");

        if (monthField) {

            wireField(monthField);

            const valueEl =
                monthField.querySelector(
                    ".zdt2-value"
                );

            const yearEl =
                monthField.querySelector(
                    "[data-month-year]"
                );

            const grid =
                monthField.querySelector(
                    "[data-month-grid]"
                );

            const prevBtn =
                monthField.querySelector(
                    "[data-month-prev]"
                );

            const nextBtn =
                monthField.querySelector(
                    "[data-month-next]"
                );

            const clearBtn =
                monthField.querySelector(
                    "[data-month-clear]"
                );

            const currentBtn =
                monthField.querySelector(
                    "[data-month-current]"
                );


            const now =
                new Date();

            let year =
                now.getFullYear();

            let selectedYear =
                null;

            let selectedMonth =
                null;


            function renderMonthPicker() {

                yearEl.textContent =
                    String(year);

                grid.innerHTML =
                    "";


                MONTHS.forEach(
                    (name, index) => {

                        const button =
                            document.createElement(
                                "button"
                            );

                        button.type =
                            "button";

                        button.className =
                            "zdt2-month-option";

                        button.textContent =
                            name;


                        if (
                            selectedYear === year &&
                            selectedMonth === index
                        ) {
                            button.classList.add(
                                "is-selected"
                            );
                        }


                        button.addEventListener(
                            "click",
                            event => {

                                event.preventDefault();
                                event.stopPropagation();

                                selectedYear =
                                    year;

                                selectedMonth =
                                    index;

                                valueEl.textContent =
                                    `${name} ${year}`;

                                renderMonthPicker();

                                closeAll();
                            }
                        );


                        grid.appendChild(
                            button
                        );
                    }
                );
            }


            prevBtn?.addEventListener(
                "click",
                event => {

                    event.preventDefault();
                    event.stopPropagation();

                    year--;

                    renderMonthPicker();
                }
            );


            nextBtn?.addEventListener(
                "click",
                event => {

                    event.preventDefault();
                    event.stopPropagation();

                    year++;

                    renderMonthPicker();
                }
            );


            currentBtn?.addEventListener(
                "click",
                event => {

                    event.preventDefault();
                    event.stopPropagation();

                    const current =
                        new Date();

                    year =
                        current.getFullYear();

                    selectedYear =
                        year;

                    selectedMonth =
                        current.getMonth();

                    valueEl.textContent =
                        `${MONTHS[selectedMonth]} ${year}`;

                    renderMonthPicker();

                    closeAll();
                }
            );


            clearBtn?.addEventListener(
                "click",
                event => {

                    event.preventDefault();
                    event.stopPropagation();

                    selectedYear =
                        null;

                    selectedMonth =
                        null;

                    valueEl.textContent =
                        "اختر الشهر";

                    renderMonthPicker();

                    closeAll();
                }
            );


            renderMonthPicker();
        }



        /* =====================================================
           TIME PICKER
           ===================================================== */

        const timeField =
            root.querySelector(".zdt2-time");

        if (timeField) {

            wireField(timeField);

            const valueEl =
                timeField.querySelector(
                    ".zdt2-value"
                );

            const preview =
                timeField.querySelector(
                    "[data-time-preview]"
                );

            const hoursEl =
                timeField.querySelector(
                    "[data-time-hours]"
                );

            const minutesEl =
                timeField.querySelector(
                    "[data-time-minutes]"
                );

            const clearBtn =
                timeField.querySelector(
                    "[data-time-clear]"
                );

            const nowBtn =
                timeField.querySelector(
                    "[data-time-now]"
                );


            let selectedHour =
                null;

            let selectedMinute =
                null;


            function updateTimeValue() {

                if (
                    selectedHour === null ||
                    selectedMinute === null
                ) {

                    preview.textContent =
                        "--:--";

                    return;
                }


                const formatted =
                    `${pad(selectedHour)}:${pad(selectedMinute)}`;


                preview.textContent =
                    formatted;

                valueEl.textContent =
                    formatted;
            }


            function renderTime() {

                hoursEl.innerHTML =
                    "";

                minutesEl.innerHTML =
                    "";


                for (
                    let hour = 0;
                    hour < 24;
                    hour++
                ) {

                    const button =
                        document.createElement(
                            "button"
                        );

                    button.type =
                        "button";

                    button.className =
                        "zdt2-time-option";

                    button.textContent =
                        pad(hour);


                    if (
                        selectedHour ===
                        hour
                    ) {
                        button.classList.add(
                            "is-selected"
                        );
                    }


                    button.addEventListener(
                        "click",
                        event => {

                            event.preventDefault();
                            event.stopPropagation();

                            selectedHour =
                                hour;

                            renderTime();

                            updateTimeValue();
                        }
                    );


                    hoursEl.appendChild(
                        button
                    );
                }


                [
                    0,
                    15,
                    30,
                    45
                ].forEach(minute => {

                    const button =
                        document.createElement(
                            "button"
                        );

                    button.type =
                        "button";

                    button.className =
                        "zdt2-time-option";

                    button.textContent =
                        pad(minute);


                    if (
                        selectedMinute ===
                        minute
                    ) {
                        button.classList.add(
                            "is-selected"
                        );
                    }


                    button.addEventListener(
                        "click",
                        event => {

                            event.preventDefault();
                            event.stopPropagation();

                            selectedMinute =
                                minute;

                            renderTime();

                            updateTimeValue();
                        }
                    );


                    minutesEl.appendChild(
                        button
                    );
                });
            }


            nowBtn?.addEventListener(
                "click",
                event => {

                    event.preventDefault();
                    event.stopPropagation();

                    const now =
                        new Date();

                    selectedHour =
                        now.getHours();

                    selectedMinute =
                        Math.round(
                            now.getMinutes() / 15
                        ) * 15;


                    if (
                        selectedMinute ===
                        60
                    ) {

                        selectedMinute =
                            0;

                        selectedHour =
                            (selectedHour + 1) % 24;
                    }


                    updateTimeValue();

                    renderTime();

                    closeAll();
                }
            );


            clearBtn?.addEventListener(
                "click",
                event => {

                    event.preventDefault();
                    event.stopPropagation();

                    selectedHour =
                        null;

                    selectedMinute =
                        null;

                    valueEl.textContent =
                        "اختر الوقت";

                    preview.textContent =
                        "--:--";

                    renderTime();

                    closeAll();
                }
            );


            renderTime();
        }


        console.info(
            "ZYNORA Date/Month/Time Lab initialized."
        );
    }


    if (
        document.readyState ===
        "loading"
    ) {

        document.addEventListener(
            "DOMContentLoaded",
            initZynoraDateTimeLab,
            { once: true }
        );
    }
    else {

        initZynoraDateTimeLab();
    }

})();
