(() => {
    const autoOpenModal = document.querySelector(".nx-setup-modal[data-auto-open='true']");

    if (autoOpenModal) {
        document.documentElement.classList.add("nx-setup-modal-open");
        document.body.classList.add("nx-setup-modal-open");
    }

    /* NEXORA_CUTOFF_DELETE_MODAL_START */
    const deleteModal = document.getElementById("cutoff-delete-modal");
    const deletePolicyName = deleteModal?.querySelector("[data-delete-policy-name]");
    const deleteConfirmButton = deleteModal?.querySelector("[data-delete-confirm-submit]");
    const deleteConfirmLabel = deleteModal?.querySelector("[data-delete-confirm-label]");
    let pendingDeleteForm = null;

    function resetDeleteConfirmation() {
        pendingDeleteForm = null;

        if (deleteConfirmButton) {
            deleteConfirmButton.disabled = false;
            deleteConfirmButton.removeAttribute("aria-busy");
        }

        if (deleteConfirmLabel) {
            deleteConfirmLabel.textContent =
                "\u062d\u0630\u0641 \u0627\u0644\u0633\u064a\u0627\u0633\u0629";
        }
    }

    document.querySelectorAll("[data-cutoff-delete-open]").forEach(button => {
        button.addEventListener("click", () => {
            const formElement = button.closest("[data-confirm-delete]");

            if (!formElement) {
                return;
            }

            pendingDeleteForm = formElement;

            if (deletePolicyName) {
                deletePolicyName.textContent =
                    formElement.dataset.policyName ||
                    "\u0633\u064a\u0627\u0633\u0629 \u0627\u0644\u063a\u0644\u0642";
            }

            if (deleteConfirmButton) {
                deleteConfirmButton.disabled = false;
                deleteConfirmButton.removeAttribute("aria-busy");
            }

            if (deleteConfirmLabel) {
                deleteConfirmLabel.textContent =
                    "\u062d\u0630\u0641 \u0627\u0644\u0633\u064a\u0627\u0633\u0629";
            }
        });
    });

    deleteConfirmButton?.addEventListener("click", () => {
        if (!pendingDeleteForm) {
            return;
        }

        deleteConfirmButton.disabled = true;
        deleteConfirmButton.setAttribute("aria-busy", "true");

        if (deleteConfirmLabel) {
            deleteConfirmLabel.textContent =
                "\u062c\u0627\u0631\u064a \u0627\u0644\u062d\u0630\u0641...";
        }

        window.setTimeout(() => {
            HTMLFormElement.prototype.submit.call(pendingDeleteForm);
        }, 120);
    });

    document.addEventListener("click", event => {
        const closeControl = event.target.closest(
            "#cutoff-delete-modal [data-nx-setup-close]"
        );

        if (closeControl || event.target === deleteModal) {
            window.setTimeout(resetDeleteConfirmation, 0);
        }
    });

    document.addEventListener("keydown", event => {
        if (
            event.key === "Escape" &&
            deleteModal?.classList.contains("is-open")
        ) {
            window.setTimeout(resetDeleteConfirmation, 0);
        }
    });
    /* NEXORA_CUTOFF_DELETE_MODAL_END */

    const logoInput = document.querySelector("[data-logo-file]");
    const logoPreview = document.querySelector("[data-logo-preview]");

    logoInput?.addEventListener("change", () => {
        const file = logoInput.files?.[0];

        if (!file || !logoPreview || !file.type.startsWith("image/")) {
            return;
        }

        const reader = new FileReader();

        reader.addEventListener("load", () => {
            logoPreview.innerHTML = "";
            const image = document.createElement("img");
            image.src = String(reader.result || "");
            image.alt = "Company logo preview";
            logoPreview.appendChild(image);
        });

        reader.readAsDataURL(file);
    });
})();

/* NEXORA_SEARCHABLE_SELECTS_START */
(() => {
    const selectors = document.querySelectorAll("select[data-searchable-select]");

    function normalize(value) {
        return String(value || "").trim().toLocaleLowerCase();
    }

    function closeAll(except) {
        document.querySelectorAll(".nx-search-select.is-open").forEach(element => {
            if (element !== except) {
                element.classList.remove("is-open");
                element.querySelector(".nx-search-select__button")?.setAttribute("aria-expanded", "false");
            }
        });
    }

    selectors.forEach(select => {
        if (select.dataset.searchableReady === "true") {
            return;
        }

        select.dataset.searchableReady = "true";

        const wrapper = document.createElement("div");
        wrapper.className = "nx-search-select";

        const button = document.createElement("button");
        button.type = "button";
        button.className = "nx-search-select__button";
        button.setAttribute("aria-haspopup", "listbox");
        button.setAttribute("aria-expanded", "false");

        const buttonText = document.createElement("span");
        buttonText.className = "nx-search-select__value";

        const arrow = document.createElement("span");
        arrow.className = "nx-search-select__arrow";
        arrow.setAttribute("aria-hidden", "true");

        button.append(buttonText, arrow);

        const panel = document.createElement("div");
        panel.className = "nx-search-select__panel";

        const search = document.createElement("input");
        search.type = "search";
        search.className = "nx-search-select__search";
        search.placeholder = select.dataset.searchPlaceholder || "Search";
        search.autocomplete = "off";

        const list = document.createElement("div");
        list.className = "nx-search-select__list";
        list.setAttribute("role", "listbox");

        panel.append(search, list);

        select.parentNode.insertBefore(wrapper, select);
        wrapper.append(select, button, panel);
        select.classList.add("nx-search-select__native");

        function selectedText() {
            const selectedOption = select.options[select.selectedIndex];
            return selectedOption ? selectedOption.text : "";
        }

        function syncButton() {
            const text = selectedText();
            buttonText.textContent = text || "--";
            button.classList.toggle("is-placeholder", !select.value);
        }

        function chooseOption(option) {
            select.value = option.value;
            select.dispatchEvent(new Event("change", { bubbles: true }));
            syncButton();
            wrapper.classList.remove("is-open");
            button.setAttribute("aria-expanded", "false");
            button.focus();
        }

        function render(query) {
            const normalizedQuery = normalize(query);
            list.innerHTML = "";

            const matchingOptions = Array.from(select.options).filter(option => {
                if (!normalizedQuery) {
                    return true;
                }

                return normalize(option.text).includes(normalizedQuery) ||
                    normalize(option.value).includes(normalizedQuery);
            });

            if (matchingOptions.length === 0) {
                const empty = document.createElement("div");
                empty.className = "nx-search-select__empty";
                empty.textContent = "No results";
                list.appendChild(empty);
                return;
            }

            matchingOptions.forEach(option => {
                const item = document.createElement("button");
                item.type = "button";
                item.className = "nx-search-select__option";
                item.textContent = option.text;
                item.dataset.value = option.value;
                item.setAttribute("role", "option");
                item.setAttribute("aria-selected", option.value === select.value ? "true" : "false");

                if (option.value === select.value) {
                    item.classList.add("is-selected");
                }

                item.addEventListener("click", () => chooseOption(option));
                list.appendChild(item);
            });
        }

        function openDropdown() {
            const willOpen = !wrapper.classList.contains("is-open");
            closeAll(wrapper);
            wrapper.classList.toggle("is-open", willOpen);
            button.setAttribute("aria-expanded", willOpen ? "true" : "false");

            if (willOpen) {
                search.value = "";
                render("");
                window.setTimeout(() => search.focus(), 0);
            }
        }

        button.addEventListener("click", openDropdown);

        search.addEventListener("input", () => render(search.value));

        search.addEventListener("keydown", event => {
            if (event.key === "Escape") {
                wrapper.classList.remove("is-open");
                button.setAttribute("aria-expanded", "false");
                button.focus();
            }

            if (event.key === "ArrowDown") {
                event.preventDefault();
                list.querySelector(".nx-search-select__option")?.focus();
            }
        });

        list.addEventListener("keydown", event => {
            const current = event.target.closest(".nx-search-select__option");

            if (!current) {
                return;
            }

            if (event.key === "ArrowDown") {
                event.preventDefault();
                current.nextElementSibling?.focus();
            }

            if (event.key === "ArrowUp") {
                event.preventDefault();
                current.previousElementSibling?.focus();
            }

            if (event.key === "Escape") {
                wrapper.classList.remove("is-open");
                button.setAttribute("aria-expanded", "false");
                button.focus();
            }
        });

        select.addEventListener("change", syncButton);

        syncButton();
        render("");
    });

    document.addEventListener("click", event => {
        if (!event.target.closest(".nx-search-select")) {
            closeAll();
        }
    });

    document.addEventListener("keydown", event => {
        if (event.key === "Escape") {
            closeAll();
        }
    });
})();
/* NEXORA_SEARCHABLE_SELECTS_END */