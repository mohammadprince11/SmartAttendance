(function () {
    "use strict";

    const editShellSelector = ".nxr-profile-settings-edit-shell";
    const deleteFormSelector = "form[data-nxr-field-delete-form]";

    let backdrop = null;
    let deleteModal = null;
    let activeDetails = null;
    let pendingDeleteForm = null;

    function ensureBackdrop() {
        if (backdrop) {
            return backdrop;
        }

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
        const hasOpenEdit = Boolean(activeDetails && activeDetails.open);
        const hasDelete = Boolean(deleteModal && !deleteModal.hidden);

        if (!hasOpenEdit && !hasDelete) {
            if (backdrop) {
                backdrop.hidden = true;
            }

            document.body.classList.remove("nxr-profile-settings-modal-active");
        }
    }

    function ensureEditButtons(details) {
        const form = details.querySelector(".nxr-profile-settings-edit-form");

        if (!form || form.dataset.nxrModalButtonsReady === "true") {
            return;
        }

        form.dataset.nxrModalButtonsReady = "true";

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

        if (actions) {
            const cancelButton = document.createElement("button");
            cancelButton.type = "button";
            cancelButton.className = "nxr-profile-settings-cancel-button";
            cancelButton.textContent = "\u0625\u0644\u063A\u0627\u0621";
            cancelButton.addEventListener("click", closeEditModal);
            actions.insertBefore(cancelButton, actions.firstChild);
        }
    }

    function openEditModal(details) {
        document.querySelectorAll(editShellSelector).forEach(function (item) {
            if (item !== details) {
                item.open = false;
                item.classList.remove("is-modal-open");
            }
        });

        activeDetails = details;
        ensureEditButtons(details);
        details.classList.add("is-modal-open");
        showBackdrop();
    }

    function closeEditModal() {
        if (activeDetails) {
            activeDetails.open = false;
            activeDetails.classList.remove("is-modal-open");
            activeDetails = null;
        }

        document.querySelectorAll(editShellSelector).forEach(function (item) {
            item.classList.remove("is-modal-open");
        });

        hideBackdropIfIdle();
    }

    function ensureDeleteModal() {
        if (deleteModal) {
            return deleteModal;
        }

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
        if (deleteModal) {
            deleteModal.hidden = true;
        }

        pendingDeleteForm = null;
        hideBackdropIfIdle();
    }

    function wireEditModals() {
        document.querySelectorAll(editShellSelector).forEach(function (details) {
            if (details.dataset.nxrModalReady === "true") {
                return;
            }

            details.dataset.nxrModalReady = "true";

            details.addEventListener("toggle", function () {
                if (details.open) {
                    openEditModal(details);
                }
                else if (activeDetails === details) {
                    details.classList.remove("is-modal-open");
                    activeDetails = null;
                    hideBackdropIfIdle();
                }
            });
        });
    }

    function wireDeleteModals() {
        document.querySelectorAll(deleteFormSelector).forEach(function (form) {
            form.removeAttribute("onsubmit");

            if (form.dataset.nxrDeleteReady === "true") {
                return;
            }

            form.dataset.nxrDeleteReady = "true";

            form.addEventListener("submit", function (event) {
                if (form.dataset.nxrConfirmed === "true") {
                    return;
                }

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