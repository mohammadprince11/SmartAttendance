(() => {
    function cleanupSetupTransientLayers() {
        document.querySelectorAll("body > .nxcs-panel").forEach(panel => {
            panel.remove();
        });

        document.querySelectorAll(".nxcs-select.is-open").forEach(wrapper => {
            wrapper.classList.remove("is-open");
            wrapper.querySelector(".nxcs-trigger")?.setAttribute(
                "aria-expanded",
                "false"
            );
        });

        if (!document.querySelector(".nx-setup-modal.is-open")) {
            document.documentElement.classList.remove(
                "nx-setup-modal-open"
            );
            document.body.classList.remove("nx-setup-modal-open");
        }
    }

    cleanupSetupTransientLayers();
    window.addEventListener("pageshow", cleanupSetupTransientLayers);
    window.addEventListener("pagehide", cleanupSetupTransientLayers);

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

    /* NEXORA_SETUP_ENTITY_MODALS_START */
    function setInputValue(modal, selector, value) {
        const input = modal?.querySelector(selector);

        if (input) {
            input.value = value ?? "";
        }
    }

    function setCheckboxValue(modal, selector, value) {
        const input = modal?.querySelector(selector);

        if (input) {
            input.checked = value === "true";
        }
    }

    function toSafeCount(value) {
        const count = Number.parseInt(String(value || "0"), 10);
        return Number.isFinite(count) && count > 0 ? count : 0;
    }

    function configureActivationLock(options) {
        const {
            modal,
            checkboxSelector,
            fallbackSelector,
            controlSelector,
            hintSelector,
            hintTextSelector,
            activeValue,
            linkedCount,
            entityLabel
        } = options;

        const checkbox = modal?.querySelector(checkboxSelector);
        const fallback = modal?.querySelector(fallbackSelector);
        const control = modal?.querySelector(controlSelector);
        const hint = modal?.querySelector(hintSelector);
        const hintText = modal?.querySelector(hintTextSelector);
        const isActive = activeValue === "true";
        const isLocked = isActive && linkedCount > 0;

        if (checkbox) {
            checkbox.checked = isActive;
            checkbox.disabled = isLocked;
            checkbox.setAttribute(
                "aria-disabled",
                isLocked ? "true" : "false"
            );
        }

        if (fallback) {
            fallback.value = isLocked ? "true" : "false";
        }

        control?.classList.toggle("is-locked", isLocked);

        if (hint) {
            hint.hidden = !isLocked;
        }

        if (hintText) {
            hintText.textContent = isLocked
                ? `${entityLabel} مرتبط بـ ${linkedCount} موظف فعال.`
                : "";
        }
    }

    document.querySelectorAll("[data-branch-edit-open]").forEach(button => {
        button.addEventListener("click", () => {
            const modal = document.getElementById(
                "setup-branch-edit-modal"
            );

            setInputValue(
                modal,
                "[data-branch-edit-id]",
                button.dataset.branchId
            );
            setInputValue(
                modal,
                "[data-branch-edit-company-id]",
                button.dataset.branchCompanyId
            );
            setInputValue(
                modal,
                "[data-branch-edit-name]",
                button.dataset.branchName
            );
            setInputValue(
                modal,
                "[data-branch-edit-address]",
                button.dataset.branchAddress
            );
            setCheckboxValue(
                modal,
                "[data-branch-edit-active]",
                button.dataset.branchActive
            );

            configureActivationLock({
                modal,
                checkboxSelector: "[data-branch-edit-active]",
                fallbackSelector: "[data-branch-edit-active-fallback]",
                controlSelector: "[data-branch-active-control]",
                hintSelector: "[data-branch-active-lock]",
                hintTextSelector: "[data-branch-active-lock-text]",
                activeValue: button.dataset.branchActive,
                linkedCount: toSafeCount(
                    button.dataset.branchActiveEmployeeCount
                ),
                entityLabel: "موقع العمل"
            });
        });
    });

    document.querySelectorAll("[data-branch-delete-open]").forEach(button => {
        button.addEventListener("click", () => {
            const modal = document.getElementById(
                "setup-branch-delete-modal"
            );

            setInputValue(
                modal,
                "[data-branch-delete-id]",
                button.dataset.branchId
            );
            setInputValue(
                modal,
                "[data-branch-delete-company-id]",
                button.dataset.branchCompanyId
            );

            const name = modal?.querySelector(
                "[data-branch-delete-name]"
            );

            if (name) {
                name.textContent =
                    button.dataset.branchName ||
                    "\u0645\u0648\u0642\u0639 \u0627\u0644\u0639\u0645\u0644";
            }
        });
    });

    document.querySelectorAll("[data-department-edit-open]").forEach(button => {
        button.addEventListener("click", () => {
            const modal = document.getElementById(
                "setup-department-edit-modal"
            );

            setInputValue(
                modal,
                "[data-department-edit-id]",
                button.dataset.departmentId
            );
            setInputValue(
                modal,
                "[data-department-edit-company-id]",
                button.dataset.departmentCompanyId
            );
            setInputValue(
                modal,
                "[data-department-edit-name]",
                button.dataset.departmentName
            );
            setCheckboxValue(
                modal,
                "[data-department-edit-active]",
                button.dataset.departmentActive
            );

            configureActivationLock({
                modal,
                checkboxSelector: "[data-department-edit-active]",
                fallbackSelector: "[data-department-edit-active-fallback]",
                controlSelector: "[data-department-active-control]",
                hintSelector: "[data-department-active-lock]",
                hintTextSelector: "[data-department-active-lock-text]",
                activeValue: button.dataset.departmentActive,
                linkedCount: toSafeCount(
                    button.dataset.departmentActiveEmployeeCount
                ),
                entityLabel: "القسم"
            });
        });
    });

    document.querySelectorAll("[data-department-delete-open]").forEach(button => {
        button.addEventListener("click", () => {
            const modal = document.getElementById(
                "setup-department-delete-modal"
            );

            setInputValue(
                modal,
                "[data-department-delete-id]",
                button.dataset.departmentId
            );
            setInputValue(
                modal,
                "[data-department-delete-company-id]",
                button.dataset.departmentCompanyId
            );

            const name = modal?.querySelector(
                "[data-department-delete-name]"
            );

            if (name) {
                name.textContent =
                    button.dataset.departmentName ||
                    "\u0627\u0644\u0642\u0633\u0645";
            }
        });
    });

    function resetEntitySubmitState() {
        document.querySelectorAll("[data-entity-submit]").forEach(form => {
            const button = form.querySelector(
                "[data-entity-submit-button]"
            );
            const label = form.querySelector(
                "[data-entity-submit-label]"
            );

            if (button) {
                button.disabled = false;
                button.removeAttribute("aria-busy");
            }

            if (label?.dataset.defaultLabel) {
                label.textContent = label.dataset.defaultLabel;
            }
        });
    }

    document.querySelectorAll("[data-entity-submit]").forEach(form => {
        const label = form.querySelector(
            "[data-entity-submit-label]"
        );

        if (label && !label.dataset.defaultLabel) {
            label.dataset.defaultLabel =
                label.textContent?.trim() || "";
        }

        form.addEventListener("submit", () => {
            const button = form.querySelector(
                "[data-entity-submit-button]"
            );
            const submitLabel = form.querySelector(
                "[data-entity-submit-label]"
            );

            if (button) {
                button.disabled = true;
                button.setAttribute("aria-busy", "true");
            }

            if (submitLabel) {
                submitLabel.textContent =
                    "\u062c\u0627\u0631\u064a \u0627\u0644\u062a\u0646\u0641\u064a\u0630...";
            }
        });
    });

    window.addEventListener("pageshow", resetEntitySubmitState);
    /* NEXORA_SETUP_ENTITY_MODALS_END */

    const logoInput = document.querySelector("[data-logo-file]");
    const logoPreview = document.querySelector("[data-logo-preview]");
    const logoContentScroller =
        document.querySelector(".nexora-content");

    let logoScrollSnapshot = null;
    let logoRestoreSequence = 0;

    function normalizeDocumentScroll() {
        document.documentElement.scrollTop = 0;
        document.documentElement.scrollLeft = 0;
        document.body.scrollTop = 0;
        document.body.scrollLeft = 0;

        window.scrollTo({
            left: 0,
            top: 0,
            behavior: "auto"
        });
    }

    function rememberLogoScrollPosition() {
        logoScrollSnapshot = {
            contentScrollTop:
                logoContentScroller?.scrollTop || 0,
            contentScrollLeft:
                logoContentScroller?.scrollLeft || 0
        };

        normalizeDocumentScroll();
    }

    function restoreLogoScrollPosition() {
        const snapshot = logoScrollSnapshot;
        const sequence = ++logoRestoreSequence;

        logoInput?.blur();

        function restore() {
            if (sequence !== logoRestoreSequence) {
                return;
            }

            normalizeDocumentScroll();

            if (snapshot && logoContentScroller) {
                logoContentScroller.scrollTo({
                    left: snapshot.contentScrollLeft,
                    top: snapshot.contentScrollTop,
                    behavior: "auto"
                });
            }
        }

        restore();
        window.requestAnimationFrame(restore);
        window.setTimeout(restore, 40);
        window.setTimeout(() => {
            restore();

            if (sequence === logoRestoreSequence) {
                logoScrollSnapshot = null;
            }
        }, 180);
    }

    logoInput?.addEventListener(
        "pointerdown",
        rememberLogoScrollPosition
    );

    logoInput?.addEventListener(
        "click",
        rememberLogoScrollPosition
    );

    logoInput?.addEventListener(
        "cancel",
        restoreLogoScrollPosition
    );

    logoInput?.addEventListener("change", () => {
        const file = logoInput.files?.[0];

        restoreLogoScrollPosition();

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
            restoreLogoScrollPosition();
        });

        reader.readAsDataURL(file);
    });

    window.addEventListener("focus", () => {
        if (!logoScrollSnapshot) {
            return;
        }

        window.setTimeout(
            restoreLogoScrollPosition,
            0
        );
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

/* NEXORA_SETUP_TABLE_PAGINATION_START */
(() => {
    const pageSize = 5;
    const tables = document.querySelectorAll(
        ".nx-company-setup-page .nexora-setup-table"
    );

    function createButton(text, className, ariaLabel) {
        const button = document.createElement("button");
        button.type = "button";
        button.className = className;
        button.textContent = text;
        button.setAttribute("aria-label", ariaLabel);
        return button;
    }

    function pageSequence(currentPage, pageCount) {
        const candidates = new Set([
            1,
            pageCount,
            currentPage - 1,
            currentPage,
            currentPage + 1
        ]);

        return Array.from(candidates)
            .filter(page => page >= 1 && page <= pageCount)
            .sort((left, right) => left - right);
    }

    tables.forEach((table, tableIndex) => {
        const body = table.tBodies[0];

        if (!body) {
            return;
        }

        const rows = Array.from(body.rows).filter(
            row => !row.querySelector(".nx-setup-empty")
        );

        if (rows.length <= pageSize) {
            return;
        }

        const tableWrap = table.closest(".nexora-setup-table-wrap");

        if (!tableWrap) {
            return;
        }

        const existingPagination =
            tableWrap.nextElementSibling?.matches("[data-nx-setup-pagination]")
                ? tableWrap.nextElementSibling
                : null;

        if (existingPagination) {
            return;
        }

        const pageCount = Math.ceil(rows.length / pageSize);
        let currentPage = 1;

        const pagination = document.createElement("div");
        pagination.className = "nx-setup-pagination";
        pagination.dataset.nxSetupPagination = "true";
        pagination.setAttribute(
            "aria-label",
            "\u062a\u0646\u0642\u0644 \u062c\u062f\u0648\u0644 " + (tableIndex + 1)
        );

        const summary = document.createElement("span");
        summary.className = "nx-setup-pagination__summary";
        summary.setAttribute("aria-live", "polite");

        const controls = document.createElement("div");
        controls.className = "nx-setup-pagination__controls";

        const previousButton = createButton(
            "\u0627\u0644\u0633\u0627\u0628\u0642",
            "nx-setup-pagination__button nx-setup-pagination__nav",
            "\u0627\u0644\u0635\u0641\u062d\u0629 \u0627\u0644\u0633\u0627\u0628\u0642\u0629"
        );

        const pageButtons = document.createElement("div");
        pageButtons.className = "nx-setup-pagination__pages";

        const nextButton = createButton(
            "\u0627\u0644\u062a\u0627\u0644\u064a",
            "nx-setup-pagination__button nx-setup-pagination__nav",
            "\u0627\u0644\u0635\u0641\u062d\u0629 \u0627\u0644\u062a\u0627\u0644\u064a\u0629"
        );

        function renderPageButtons() {
            pageButtons.innerHTML = "";
            const pages = pageSequence(currentPage, pageCount);

            pages.forEach((page, index) => {
                const previousPage = pages[index - 1];

                if (previousPage && page - previousPage > 1) {
                    const ellipsis = document.createElement("span");
                    ellipsis.className = "nx-setup-pagination__ellipsis";
                    ellipsis.textContent = "\u2026";
                    ellipsis.setAttribute("aria-hidden", "true");
                    pageButtons.appendChild(ellipsis);
                }

                const pageButton = createButton(
                    String(page),
                    "nx-setup-pagination__button nx-setup-pagination__page",
                    "\u0627\u0644\u0627\u0646\u062a\u0642\u0627\u0644 \u0625\u0644\u0649 \u0627\u0644\u0635\u0641\u062d\u0629 " + page
                );

                pageButton.dataset.page = String(page);
                pageButton.classList.toggle("is-active", page === currentPage);

                if (page === currentPage) {
                    pageButton.setAttribute("aria-current", "page");
                }

                pageButton.addEventListener("click", () => {
                    currentPage = page;
                    render();
                });

                pageButtons.appendChild(pageButton);
            });
        }

        function render() {
            const startIndex = (currentPage - 1) * pageSize;
            const endIndex = Math.min(startIndex + pageSize, rows.length);

            rows.forEach((row, rowIndex) => {
                const isVisible =
                    rowIndex >= startIndex && rowIndex < endIndex;

                row.hidden = !isVisible;
                row.setAttribute("aria-hidden", isVisible ? "false" : "true");
            });

            summary.textContent =
                "\u0639\u0631\u0636 " +
                (startIndex + 1) +
                "-" +
                endIndex +
                " \u0645\u0646 " +
                rows.length;

            previousButton.disabled = currentPage === 1;
            nextButton.disabled = currentPage === pageCount;

            renderPageButtons();
        }

        previousButton.addEventListener("click", () => {
            if (currentPage > 1) {
                currentPage -= 1;
                render();
            }
        });

        nextButton.addEventListener("click", () => {
            if (currentPage < pageCount) {
                currentPage += 1;
                render();
            }
        });

        controls.append(previousButton, pageButtons, nextButton);
        pagination.append(summary, controls);
        tableWrap.insertAdjacentElement("afterend", pagination);

        render();
    });
})();
/* NEXORA_SETUP_TABLE_PAGINATION_END */

/* NEXORA_SETUP_SECTION_NAVIGATION_START */
(() => {
    "use strict";

    const page = document.querySelector(".nx-company-setup-page");
    const navigation = page?.querySelector(".nx-company-setup-nav");

    if (!page || !navigation) {
        return;
    }

    const links = Array.from(
        navigation.querySelectorAll('a[href^="#"]')
    );

    const contentScroller =
        page.closest(".nexora-content") ||
        document.scrollingElement ||
        document.documentElement;

    const reducedMotion = window.matchMedia(
        "(prefers-reduced-motion: reduce)"
    );

    let arrivalTimer = 0;
    let scrollTimer = 0;

    function resolveTarget(link) {
        const hash = link.getAttribute("href");

        if (!hash || hash === "#") {
            return null;
        }

        try {
            return document.querySelector(hash);
        } catch (_) {
            return null;
        }
    }

    function isDocumentScroller(scroller) {
        return (
            scroller === document.scrollingElement ||
            scroller === document.documentElement ||
            scroller === document.body
        );
    }

    function stickyTopbarOffset() {
        const topbar = document.querySelector(".nexora-topbar");
        const topbarHeight =
            topbar?.getBoundingClientRect().height || 0;

        return Math.ceil(topbarHeight) + 18;
    }

    function targetScrollTop(target) {
        const targetRect = target.getBoundingClientRect();
        const offset = stickyTopbarOffset();

        if (isDocumentScroller(contentScroller)) {
            return Math.max(
                0,
                window.scrollY + targetRect.top - offset
            );
        }

        const scrollerRect = contentScroller.getBoundingClientRect();

        return Math.max(
            0,
            contentScroller.scrollTop +
                targetRect.top -
                scrollerRect.top -
                offset
        );
    }

    function setActiveLink(activeLink) {
        links.forEach(link => {
            const isActive = link === activeLink;

            link.classList.toggle("is-active", isActive);

            if (isActive) {
                link.setAttribute("aria-current", "location");
            } else {
                link.removeAttribute("aria-current");
            }
        });
    }

    function showArrival(target) {
        window.clearTimeout(arrivalTimer);

        page.querySelectorAll(
            ".nx-company-section.nx-setup-section-arrival"
        ).forEach(section => {
            section.classList.remove("nx-setup-section-arrival");
        });

        target.classList.remove("nx-setup-section-arrival");
        void target.offsetWidth;
        target.classList.add("nx-setup-section-arrival");

        arrivalTimer = window.setTimeout(() => {
            target.classList.remove("nx-setup-section-arrival");
        }, 760);
    }

    function updateHash(hash) {
        if (!window.history || !window.history.replaceState) {
            return;
        }

        const nextUrl = new URL(window.location.href);
        nextUrl.searchParams.delete("focusSection");
        nextUrl.hash = hash;

        window.history.replaceState(
            window.history.state,
            "",
            nextUrl.pathname + nextUrl.search + nextUrl.hash
        );
    }

    function scrollToSection(link, target) {
        const top = targetScrollTop(target);
        const behavior = reducedMotion.matches
            ? "auto"
            : "smooth";

        setActiveLink(link);

        if (isDocumentScroller(contentScroller)) {
            window.scrollTo({
                top,
                behavior
            });
        } else {
            contentScroller.scrollTo({
                top,
                behavior
            });
        }

        updateHash(link.getAttribute("href"));

        window.clearTimeout(scrollTimer);
        scrollTimer = window.setTimeout(() => {
            showArrival(target);
        }, reducedMotion.matches ? 40 : 430);
    }

    links.forEach(link => {
        link.addEventListener("click", event => {
            const target = resolveTarget(link);

            if (!target) {
                return;
            }

            event.preventDefault();
            scrollToSection(link, target);
        });
    });

    function resetOuterDocumentScroll() {
        document.documentElement.scrollTop = 0;
        document.body.scrollTop = 0;
        window.scrollTo({
            left: 0,
            top: 0,
            behavior: "auto"
        });
    }

    function restoreInitialSection() {
        const requestedSection = new URLSearchParams(
            window.location.search
        ).get("focusSection");
        const initialHash = requestedSection
            ? "#" + requestedSection
            : window.location.hash;

        if (!initialHash) {
            resetOuterDocumentScroll();
            return;
        }

        const initialLink = links.find(
            link => link.getAttribute("href") === initialHash
        );
        const target = initialLink
            ? resolveTarget(initialLink)
            : null;

        if (!initialLink || !target) {
            resetOuterDocumentScroll();
            return;
        }

        resetOuterDocumentScroll();

        window.requestAnimationFrame(() => {
            resetOuterDocumentScroll();

            const top = targetScrollTop(target);

            if (isDocumentScroller(contentScroller)) {
                window.scrollTo({
                    top,
                    behavior: "auto"
                });
            } else {
                contentScroller.scrollTo({
                    top,
                    behavior: "auto"
                });

                resetOuterDocumentScroll();
            }

            setActiveLink(initialLink);
            updateHash(initialHash);
            showArrival(target);
        });
    }

    if ("scrollRestoration" in window.history) {
        window.history.scrollRestoration = "manual";
    }

    restoreInitialSection();
    window.addEventListener("pageshow", restoreInitialSection);
})();
/* NEXORA_SETUP_SECTION_NAVIGATION_END */
