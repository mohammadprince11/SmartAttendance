(function () {
    "use strict";

    const editShellSelector = ".nxr-profile-settings-edit-shell";
    const editTriggerSelector = ".nxr-profile-settings-edit-trigger";
    const deleteFormSelector = "form[data-nxr-field-delete-form]";

    let backdrop = null;
    let deleteModal = null;
    let activeEdit = null;
    let pendingDeleteForm = null;

    function ensureBackdrop() {
        if (backdrop) return backdrop;

        backdrop = document.createElement("div");
        backdrop.className = "nxr-profile-settings-modal-backdrop";
        backdrop.hidden = true;
        document.body.appendChild(backdrop);

        backdrop.addEventListener("click", function () {
            closeEditModal();
            closeDeleteModal();
        });

        return backdrop;
    }

    function showBackdrop() {
        ensureBackdrop();
        backdrop.hidden = false;
        document.body.classList.add("nxr-profile-settings-modal-active");
    }

    function hideBackdropIfIdle() {
        const hasEdit = Boolean(activeEdit);
        const hasDelete = Boolean(deleteModal && !deleteModal.hidden);

        if (!hasEdit && !hasDelete) {
            if (backdrop) backdrop.hidden = true;
            document.body.classList.remove("nxr-profile-settings-modal-active");
        }
    }

    function ensureEditHeader(form) {
        if (!form || form.dataset.nxrBodyModalReady === "true") return;

        form.dataset.nxrBodyModalReady = "true";

        const head = document.createElement("div");
        head.className = "nxr-profile-settings-edit-modal-head";

        const title = document.createElement("strong");
        title.textContent = "\u062A\u0639\u062F\u064A\u0644 \u0627\u0644\u062D\u0642\u0644";

        const closeButton = document.createElement("button");
        closeButton.type = "button";
        closeButton.className = "nxr-profile-settings-modal-close";
        closeButton.setAttribute("aria-label", "Close");
        closeButton.textContent = "\u00D7";
        closeButton.addEventListener("click", closeEditModal);

        head.appendChild(title);
        head.appendChild(closeButton);
        form.insertBefore(head, form.firstChild);

        const actions = form.querySelector(".nxr-profile-settings-actions");

        if (actions && !actions.querySelector(".nxr-profile-settings-cancel-button")) {
            const cancelButton = document.createElement("button");
            cancelButton.type = "button";
            cancelButton.className = "nxr-profile-settings-cancel-button";
            cancelButton.textContent = "\u0625\u0644\u063A\u0627\u0621";
            cancelButton.addEventListener("click", closeEditModal);
            actions.insertBefore(cancelButton, actions.firstChild);
        }

        form.addEventListener("click", function (event) {
            event.stopPropagation();
        });
    }

    function openEditModal(shell) {
        if (!shell) return;

        const form = shell.querySelector(".nxr-profile-settings-edit-form");
        if (!form) return;

        closeEditModal();

        const placeholder = document.createComment("NEXORA_EDIT_FORM_PLACEHOLDER");
        shell.insertBefore(placeholder, form);

        ensureEditHeader(form);

        activeEdit = {
            shell: shell,
            form: form,
            placeholder: placeholder
        };

        form.classList.add("nxr-profile-settings-body-modal");
        document.body.appendChild(form);
        showBackdrop();

        window.setTimeout(function () {
            const firstInput = form.querySelector("input, select, button");
            if (firstInput) firstInput.focus({ preventScroll: true });
        }, 40);
    }

    function closeEditModal() {
        if (!activeEdit) return;

        const item = activeEdit;
        activeEdit = null;

        item.form.classList.remove("nxr-profile-settings-body-modal");

        if (item.placeholder && item.placeholder.parentNode) {
            item.placeholder.parentNode.insertBefore(item.form, item.placeholder);
            item.placeholder.remove();
        } else {
            item.shell.appendChild(item.form);
        }

        hideBackdropIfIdle();
    }

    function ensureDeleteModal() {
        if (deleteModal) return deleteModal;

        deleteModal = document.createElement("div");
        deleteModal.className = "nxr-profile-settings-delete-modal";
        deleteModal.hidden = true;
        deleteModal.setAttribute("role", "dialog");
        deleteModal.setAttribute("aria-modal", "true");

        deleteModal.innerHTML =
            '<div class="nxr-profile-settings-delete-head">' +
                '<div>' +
                    '<span>NEXORA Delete Confirmation</span>' +
                    '<strong>\u062D\u0630\u0641 \u0627\u0644\u062D\u0642\u0644</strong>' +
                '</div>' +
                '<button type="button" class="nxr-profile-settings-modal-close" data-nxr-delete-cancel aria-label="Close">\u00D7</button>' +
            '</div>' +
            '<p>\u0647\u0644 \u062A\u0631\u064A\u062F \u062D\u0630\u0641 \u0647\u0630\u0627 \u0627\u0644\u062D\u0642\u0644\u061F \u0633\u064A\u062A\u0645 \u062D\u0630\u0641 \u0627\u0644\u0642\u064A\u0645 \u0627\u0644\u0645\u062D\u0641\u0648\u0638\u0629 \u0644\u0647\u0630\u0627 \u0627\u0644\u062D\u0642\u0644 \u0645\u0646 \u0645\u0644\u0641\u0627\u062A \u0627\u0644\u0645\u0648\u0638\u0641\u064A\u0646.</p>' +
            '<div class="nxr-profile-settings-delete-actions">' +
                '<button type="button" class="cancel" data-nxr-delete-cancel>\u0625\u0644\u063A\u0627\u0621</button>' +
                '<button type="button" class="danger" data-nxr-delete-confirm>\u062A\u0623\u0643\u064A\u062F \u0627\u0644\u062D\u0630\u0641</button>' +
            '</div>';

        document.body.appendChild(deleteModal);

        deleteModal.querySelectorAll("[data-nxr-delete-cancel]").forEach(function (button) {
            button.addEventListener("click", closeDeleteModal);
        });

        deleteModal.querySelector("[data-nxr-delete-confirm]").addEventListener("click", function () {
            if (!pendingDeleteForm) {
                closeDeleteModal();
                return;
            }

            pendingDeleteForm.dataset.nxrConfirmed = "true";
            pendingDeleteForm.submit();
        });

        deleteModal.addEventListener("click", function (event) {
            event.stopPropagation();
        });

        return deleteModal;
    }

    function openDeleteModal(form) {
        closeEditModal();
        pendingDeleteForm = form;
        ensureDeleteModal();
        deleteModal.hidden = false;
        showBackdrop();
    }

    function closeDeleteModal() {
        if (deleteModal) deleteModal.hidden = true;
        pendingDeleteForm = null;
        hideBackdropIfIdle();
    }

    function wireEditModals() {
        document.querySelectorAll(editShellSelector).forEach(function (shell) {
            if (shell.dataset.nxrBodyModalWire === "true") return;

            shell.dataset.nxrBodyModalWire = "true";

            const trigger = shell.querySelector(editTriggerSelector);
            if (trigger) {
                trigger.addEventListener("click", function (event) {
                    event.preventDefault();
                    event.stopPropagation();
                    openEditModal(shell);
                });
            }
        });
    }

    function wireDeleteModals() {
        document.querySelectorAll(deleteFormSelector).forEach(function (form) {
            form.removeAttribute("onsubmit");

            if (form.dataset.nxrDeleteReady === "true") return;

            form.dataset.nxrDeleteReady = "true";

            form.addEventListener("submit", function (event) {
                if (form.dataset.nxrConfirmed === "true") return;

                event.preventDefault();
                event.stopPropagation();
                openDeleteModal(form);
            });
        });
    }

    document.addEventListener("keydown", function (event) {
        if (event.key === "Escape") {
            closeEditModal();
            closeDeleteModal();
        }
    });

    document.addEventListener("DOMContentLoaded", function () {
        ensureBackdrop();
        ensureDeleteModal();
        wireEditModals();
        wireDeleteModals();
    });
})();
/* NEXORA_FIX08I_PROFILE_SETTINGS_ACCORDION_CAPSULES_START */
(function () {
    "use strict";

    const sectionSelector = ".nxr-profile-settings-section";

    function getFieldCount(section) {
        const countNode = section.querySelector(":scope > header strong");
        const value = countNode ? parseInt((countNode.textContent || "0").trim(), 10) : 0;
        return Number.isFinite(value) ? value : 0;
    }

    function openSection(targetSection) {
        const sections = Array.from(document.querySelectorAll(sectionSelector));

        sections.forEach(function (section) {
            const isOpen = section === targetSection;
            section.classList.toggle("is-open", isOpen);

            const header = section.querySelector(":scope > header");
            if (header) {
                header.setAttribute("aria-expanded", isOpen ? "true" : "false");
            }
        });
    }

    function initProfileSettingsAccordion() {
        const sections = Array.from(document.querySelectorAll(sectionSelector));

        if (!sections.length) {
            return;
        }

        sections.forEach(function (section) {
            if (section.dataset.nxrAccordionReady === "true") {
                return;
            }

            section.dataset.nxrAccordionReady = "true";

            const header = section.querySelector(":scope > header");

            if (!header) {
                return;
            }

            header.setAttribute("role", "button");
            header.setAttribute("tabindex", "0");
            header.setAttribute("aria-expanded", "false");

            header.addEventListener("click", function () {
                openSection(section);
            });

            header.addEventListener("keydown", function (event) {
                if (event.key === "Enter" || event.key === " ") {
                    event.preventDefault();
                    openSection(section);
                }
            });
        });

        const alreadyOpen = sections.find(function (section) {
            return section.classList.contains("is-open");
        });

        if (!alreadyOpen) {
            const firstWithFields = sections.find(function (section) {
                return getFieldCount(section) > 0;
            });

            openSection(firstWithFields || sections[0]);
        }
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initProfileSettingsAccordion);
    } else {
        initProfileSettingsAccordion();
    }
})();
 /* NEXORA_FIX08I_PROFILE_SETTINGS_ACCORDION_CAPSULES_END */