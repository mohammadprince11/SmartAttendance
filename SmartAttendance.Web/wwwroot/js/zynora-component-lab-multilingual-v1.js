(() => {

    "use strict";


    function initZynoraMultilingualLab() {

        const root =
            document.getElementById(
                "multilingual"
            );

        if (!root) {
            return;
        }


        const languages =
            Array.from(
                root.querySelectorAll(
                    "[data-zcl9-lang]"
                )
            );


        const nameInput =
            root.querySelector(
                "[data-zcl9-name]"
            );

        const descriptionInput =
            root.querySelector(
                "[data-zcl9-description]"
            );

        const currentLanguage =
            root.querySelector(
                "[data-zcl9-current-language]"
            );

        const directionBadge =
            root.querySelector(
                "[data-zcl9-direction]"
            );

        const requirement =
            root.querySelector(
                "[data-zcl9-requirement]"
            );

        const helper =
            root.querySelector(
                "[data-zcl9-helper]"
            );

        const progress =
            root.querySelector(
                "[data-zcl9-progress]"
            );

        const statusTitle =
            root.querySelector(
                "[data-zcl9-status-title]"
            );

        const statusText =
            root.querySelector(
                "[data-zcl9-status-text]"
            );

        const statusBadge =
            root.querySelector(
                "[data-zcl9-status-badge]"
            );

        const nameCount =
            root.querySelector(
                "[data-zcl9-name-count]"
            );

        const descriptionCount =
            root.querySelector(
                "[data-zcl9-description-count]"
            );


        /*
         * Demo data only.
         * Later these values come dynamically from
         * CompanyLanguages + LocalizedEntityValues.
         */

        const values = {

            ar: {
                name: "الموارد البشرية",
                description:
                    "إدارة شؤون الموظفين والعمليات المتعلقة بالموارد البشرية."
            },

            en: {
                name: "Human Resources",
                description:
                    "Employee management and human resources operations."
            },

            fr: {
                name: "Ressources humaines",
                description: ""
            },

            ku: {
                name: "",
                description: ""
            },

            tr: {
                name: "",
                description: ""
            }

        };


        let activeCode =
            "ar";


        function getLanguageButton(code) {

            return languages.find(
                button =>
                    button.dataset.zcl9Lang === code
            );
        }


        function saveCurrent() {

            if (!values[activeCode]) {
                values[activeCode] = {
                    name: "",
                    description: ""
                };
            }

            values[activeCode].name =
                nameInput.value;

            values[activeCode].description =
                descriptionInput.value;
        }


        function updateCounters() {

            nameCount.textContent =
                `${nameInput.value.length} / 120`;

            descriptionCount.textContent =
                `${descriptionInput.value.length} / 500`;
        }


        function isLanguageComplete(button) {

            const code =
                button.dataset.zcl9Lang;

            const required =
                button.dataset.required ===
                "true";

            if (!required) {

                return Boolean(
                    values[code]?.name?.trim()
                );
            }

            return Boolean(
                values[code]?.name?.trim()
            );
        }


        function updateProgress() {

            const completed =
                languages.filter(
                    isLanguageComplete
                ).length;

            progress.textContent =
                `${completed} / ${languages.length}`;
        }


        function updateStatus(button) {

            const required =
                button.dataset.required ===
                "true";

            const nameExists =
                Boolean(
                    nameInput.value.trim()
                );


            statusBadge.classList.remove(
                "is-complete",
                "is-missing"
            );


            if (
                required &&
                !nameExists
            ) {

                statusTitle.textContent =
                    "ترجمة مطلوبة";

                statusText.textContent =
                    "يجب إدخال قيمة لهذه اللغة قبل حفظ السجل.";

                statusBadge.textContent =
                    "ناقص";

                statusBadge.classList.add(
                    "is-missing"
                );

                return;
            }


            statusTitle.textContent =
                "الترجمة مكتملة";

            statusText.textContent =
                required
                    ? "الحقول المطلوبة لهذه اللغة مكتملة."
                    : "هذه اللغة اختيارية ويمكن تركها فارغة.";

            statusBadge.textContent =
                "مكتمل";

            statusBadge.classList.add(
                "is-complete"
            );
        }


        function activate(code) {

            saveCurrent();

            activeCode =
                code;


            const button =
                getLanguageButton(code);

            if (!button) {
                return;
            }


            languages.forEach(item => {

                item.classList.toggle(
                    "is-active",
                    item === button
                );
            });


            const direction =
                button.dataset.dir ||
                "ltr";

            const primary =
                button.dataset.primary ===
                "true";

            const required =
                button.dataset.required ===
                "true";


            const languageName =
                button
                    .querySelector(
                        ".zcl9-language-name"
                    )
                    ?.textContent
                    ?.trim() ||
                code;


            currentLanguage.textContent =
                languageName;


            directionBadge.textContent =
                direction.toUpperCase();


            nameInput.dir =
                direction;

            descriptionInput.dir =
                direction;


            nameInput.style.textAlign =
                direction === "rtl"
                    ? "right"
                    : "left";

            descriptionInput.style.textAlign =
                direction === "rtl"
                    ? "right"
                    : "left";


            nameInput.value =
                values[code]?.name ||
                "";

            descriptionInput.value =
                values[code]?.description ||
                "";


            requirement.textContent =
                required
                    ? "مطلوب"
                    : "اختياري";


            requirement.classList.toggle(
                "zcl9-requirement--optional",
                !required
            );


            if (primary) {

                helper.textContent =
                    "هذه هي اللغة الأساسية لبيانات الشركة.";
            }
            else if (required) {

                helper.textContent =
                    "هذه اللغة مفعلة ومطلوبة حسب إعدادات الشركة.";
            }
            else {

                helper.textContent =
                    "هذه اللغة مفعلة ولكن إدخالها اختياري.";
            }


            updateCounters();
            updateStatus(button);
            updateProgress();
        }


        languages.forEach(button => {

            button.addEventListener(
                "click",
                () => {

                    activate(
                        button.dataset.zcl9Lang
                    );
                }
            );
        });


        nameInput.addEventListener(
            "input",
            () => {

                values[activeCode].name =
                    nameInput.value;

                updateCounters();

                const button =
                    getLanguageButton(
                        activeCode
                    );

                updateStatus(button);
                updateProgress();
            }
        );


        descriptionInput.addEventListener(
            "input",
            () => {

                values[activeCode].description =
                    descriptionInput.value;

                updateCounters();
            }
        );


        /*
         * Initial state
         */
        activate("ar");
    }


    if (
        document.readyState ===
        "loading"
    ) {

        document.addEventListener(
            "DOMContentLoaded",
            initZynoraMultilingualLab,
            { once: true }
        );
    }
    else {

        initZynoraMultilingualLab();
    }

})();
