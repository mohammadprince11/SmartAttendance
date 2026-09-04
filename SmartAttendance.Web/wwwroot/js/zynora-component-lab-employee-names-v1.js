(() => {

    "use strict";


    const languageDefinitions = {

        fr: {
            name: "Français",
            code: "fr-FR",
            dir: "ltr",
            state: "اختيارية",
            labels: [
                "Prénom",
                "Deuxième nom",
                "Troisième nom",
                "Nom de famille"
            ]
        },

        ku: {
            name: "کوردی",
            code: "ku-IQ",
            dir: "rtl",
            state: "اختيارية",
            labels: [
                "ناوی یەکەم",
                "ناوی دووەم",
                "ناوی سێیەم",
                "ناوی کۆتایی"
            ]
        },

        tr: {
            name: "Türkçe",
            code: "tr-TR",
            dir: "ltr",
            state: "اختيارية",
            labels: [
                "Ad",
                "İkinci Ad",
                "Üçüncü Ad",
                "Soyad"
            ]
        }
    };


    function initEmployeeNameLanguages() {

        const root =
            document.getElementById(
                "employee-name-languages"
            );

        if (!root) {
            return;
        }


        const addButton =
            root.querySelector(
                "[data-zcl10-add]"
            );

        const menu =
            root.querySelector(
                "[data-zcl10-menu]"
            );

        const grid =
            root.querySelector(
                "[data-zcl10-grid]"
            );

        const summary =
            root.querySelector(
                "[data-zcl10-summary]"
            );


        function closeMenu() {

            menu.hidden = true;

            addButton.setAttribute(
                "aria-expanded",
                "false"
            );
        }


        function openMenu() {

            menu.hidden = false;

            addButton.setAttribute(
                "aria-expanded",
                "true"
            );
        }


        function buildCard(languageKey) {

            const item =
                languageDefinitions[
                    languageKey
                ];

            if (!item) {
                return null;
            }


            const card =
                document.createElement(
                    "article"
                );


            card.className =
                "zcl10-card is-added";

            card.dataset.languageCard =
                languageKey;

            card.dir =
                item.dir;


            const labels =
                item.labels;


            card.innerHTML = `
                <header class="zcl10-card-head">

                    <div>
                        <strong>${item.name}</strong>
                        <span>${item.code}</span>
                    </div>

                    <div class="zcl10-tags">
                        <span class="zcl10-tag zcl10-tag--optional">
                            لغة إضافية · ${item.state}
                        </span>
                    </div>

                </header>

                <div class="zcl10-fields">

                    <div class="zcl10-field">
                        <label>${labels[0]}</label>
                        <input type="text"
                               dir="${item.dir}"
                               autocomplete="off" />
                    </div>

                    <div class="zcl10-field">
                        <label>${labels[1]}</label>
                        <input type="text"
                               dir="${item.dir}"
                               autocomplete="off" />
                    </div>

                    <div class="zcl10-field">
                        <label>${labels[2]}</label>
                        <input type="text"
                               dir="${item.dir}"
                               autocomplete="off" />
                    </div>

                    <div class="zcl10-field">
                        <label>${labels[3]}</label>
                        <input type="text"
                               dir="${item.dir}"
                               autocomplete="off" />
                    </div>

                </div>
            `;


            return card;
        }


        function addSummary(languageKey) {

            const item =
                languageDefinitions[
                    languageKey
                ];

            if (!item) {
                return;
            }


            const chip =
                document.createElement(
                    "span"
                );

            chip.textContent =
                `${item.name} · ${item.state}`;

            chip.dataset.summaryLanguage =
                languageKey;


            summary.appendChild(
                chip
            );
        }


        function addLanguage(languageKey) {

            if (
                grid.querySelector(
                    `[data-language-card="${languageKey}"]`
                )
            ) {
                return;
            }


            const card =
                buildCard(
                    languageKey
                );


            if (!card) {
                return;
            }


            grid.appendChild(
                card
            );


            addSummary(
                languageKey
            );


            const menuButton =
                menu.querySelector(
                    `[data-add-language="${languageKey}"]`
                );


            if (menuButton) {
                menuButton.hidden =
                    true;
            }


            closeMenu();
        }


        addButton.addEventListener(
            "click",
            event => {

                event.preventDefault();
                event.stopPropagation();


                if (menu.hidden) {
                    openMenu();
                }
                else {
                    closeMenu();
                }
            }
        );


        menu
            .querySelectorAll(
                "[data-add-language]"
            )
            .forEach(button => {

                button.addEventListener(
                    "click",
                    event => {

                        event.preventDefault();
                        event.stopPropagation();


                        addLanguage(
                            button.dataset
                                .addLanguage
                        );
                    }
                );
            });


        document.addEventListener(
            "click",
            event => {

                if (
                    !root
                        .querySelector(
                            ".zcl10-add-wrap"
                        )
                        .contains(
                            event.target
                        )
                ) {
                    closeMenu();
                }
            });


        closeMenu();
    }


    if (
        document.readyState ===
        "loading"
    ) {

        document.addEventListener(
            "DOMContentLoaded",
            initEmployeeNameLanguages,
            { once: true }
        );
    }
    else {

        initEmployeeNameLanguages();
    }

})();
