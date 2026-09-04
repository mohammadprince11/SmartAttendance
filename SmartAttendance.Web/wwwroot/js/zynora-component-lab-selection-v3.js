(() => {

    "use strict";

    function initZynoraSelectionDropdown() {

        const root =
            document.getElementById(
                "selection"
            );

        if (!root) {
            return;
        }


        root
            .querySelectorAll(
                "[data-zcl3-select]"
            )
            .forEach(select => {

                const trigger =
                    select.querySelector(
                        "[data-zcl3-trigger]"
                    );

                const menu =
                    select.querySelector(
                        "[data-zcl3-menu]"
                    );

                const value =
                    select.querySelector(
                        "[data-zcl3-value]"
                    );

                const options =
                    Array.from(
                        select.querySelectorAll(
                            ".zcl3-select-option"
                        )
                    );


                if (
                    !trigger ||
                    !menu ||
                    !value
                ) {
                    return;
                }


                function close() {

                    select.classList.remove(
                        "is-open"
                    );

                    menu.hidden =
                        true;

                    trigger.setAttribute(
                        "aria-expanded",
                        "false"
                    );
                }


                function open() {

                    document
                        .querySelectorAll(
                            "#selection .zcl3-select.is-open"
                        )
                        .forEach(other => {

                            if (other === select) {
                                return;
                            }

                            other.classList.remove(
                                "is-open"
                            );

                            const otherMenu =
                                other.querySelector(
                                    "[data-zcl3-menu]"
                                );

                            const otherTrigger =
                                other.querySelector(
                                    "[data-zcl3-trigger]"
                                );

                            if (otherMenu) {
                                otherMenu.hidden = true;
                            }

                            if (otherTrigger) {

                                otherTrigger.setAttribute(
                                    "aria-expanded",
                                    "false"
                                );
                            }
                        });


                    select.classList.add(
                        "is-open"
                    );

                    menu.hidden =
                        false;

                    trigger.setAttribute(
                        "aria-expanded",
                        "true"
                    );


                    const selected =
                        options.find(
                            option =>
                                option.classList.contains(
                                    "is-selected"
                                )
                        );


                    if (selected) {
                        selected.focus();
                    }
                }


                trigger.addEventListener(
                    "click",
                    event => {

                        event.preventDefault();
                        event.stopPropagation();


                        if (
                            select.classList.contains(
                                "is-open"
                            )
                        ) {

                            close();
                        }
                        else {

                            open();
                        }
                    }
                );


                options.forEach(option => {

                    option.addEventListener(
                        "click",
                        event => {

                            event.preventDefault();
                            event.stopPropagation();


                            options.forEach(item => {

                                item.classList.remove(
                                    "is-selected"
                                );

                                item.setAttribute(
                                    "aria-selected",
                                    "false"
                                );
                            });


                            option.classList.add(
                                "is-selected"
                            );

                            option.setAttribute(
                                "aria-selected",
                                "true"
                            );


                            value.textContent =
                                option.textContent.trim();


                            close();

                            trigger.focus();
                        }
                    );
                });


                select.addEventListener(
                    "keydown",
                    event => {

                        if (
                            event.key ===
                            "Escape"
                        ) {

                            close();

                            trigger.focus();

                            return;
                        }


                        if (
                            event.key ===
                                "ArrowDown" ||
                            event.key ===
                                "ArrowUp"
                        ) {

                            event.preventDefault();


                            if (
                                !select.classList.contains(
                                    "is-open"
                                )
                            ) {

                                open();

                                return;
                            }


                            const focused =
                                document.activeElement;


                            let index =
                                options.indexOf(
                                    focused
                                );


                            if (index < 0) {
                                index = 0;
                            }
                            else if (
                                event.key ===
                                "ArrowDown"
                            ) {
                                index =
                                    (index + 1) %
                                    options.length;
                            }
                            else {
                                index =
                                    (
                                        index - 1 +
                                        options.length
                                    ) %
                                    options.length;
                            }


                            options[index].focus();
                        }


                        if (
                            event.key ===
                                "Enter" &&
                            document.activeElement
                                ?.classList
                                .contains(
                                    "zcl3-select-option"
                                )
                        ) {

                            document
                                .activeElement
                                .click();
                        }
                    }
                );


                document.addEventListener(
                    "click",
                    event => {

                        if (
                            !select.contains(
                                event.target
                            )
                        ) {
                            close();
                        }
                    }
                );


                close();
            });
    }


    if (
        document.readyState ===
        "loading"
    ) {

        document.addEventListener(
            "DOMContentLoaded",
            initZynoraSelectionDropdown,
            { once: true }
        );
    }
    else {

        initZynoraSelectionDropdown();
    }

})();
