(function () {
    "use strict";

    function getModal() {
        return document.querySelector("[data-nxr-reassign-modal]");
    }

    function portalModalToBody(modal) {
        if (!modal) return;

        if (modal.parentElement !== document.body) {
            document.body.appendChild(modal);
        }
    }

    function refreshSelect(select) {
        if (!select) return;

        if (window.NexoraSelect && typeof window.NexoraSelect.refresh === "function") {
            window.NexoraSelect.refresh(select);
        } else if (window.NexoraSelect && typeof window.NexoraSelect.refreshAll === "function") {
            window.NexoraSelect.refreshAll();
        }
    }

    function getFirstVisibleOption(select) {
        if (!select) return null;

        return Array.from(select.options).find(function (option) {
            return !option.hidden && !option.disabled;
        }) || null;
    }

    function ensureSelectedVisible(select) {
        if (!select) return false;

        var selected = select.options[select.selectedIndex];

        if (!selected || selected.hidden || selected.disabled) {
            var first = getFirstVisibleOption(select);

            if (first) {
                select.value = first.value;
                return true;
            }
        }

        return false;
    }

    function filterBranches(companySelect, branchSelect) {
        if (!companySelect || !branchSelect) return false;

        var changed = false;
        var companyId = companySelect.value;

        Array.from(branchSelect.options).forEach(function (option) {
            var shouldHide = option.dataset.companyId !== companyId;

            if (option.hidden !== shouldHide) {
                option.hidden = shouldHide;
                changed = true;
            }
        });

        if (ensureSelectedVisible(branchSelect)) {
            changed = true;
        }

        return changed;
    }

    function filterDepartments(companySelect, departmentSelect) {
        if (!companySelect || !departmentSelect) return false;

        var changed = false;
        var companyId = companySelect.value;

        Array.from(departmentSelect.options).forEach(function (option) {
            var shouldHide = option.dataset.companyId !== companyId;

            if (option.hidden !== shouldHide) {
                option.hidden = shouldHide;
                changed = true;
            }
        });

        if (ensureSelectedVisible(departmentSelect)) {
            changed = true;
        }

        return changed;
    }

    function syncOrganizationSelects() {
        var modal = getModal();

        if (!modal) return;

        var companySelect = modal.querySelector("[data-nxr-reassign-company]");
        var branchSelect = modal.querySelector("[data-nxr-reassign-branch]");
        var departmentSelect = modal.querySelector("[data-nxr-reassign-department]");

        if (!companySelect || !branchSelect || !departmentSelect) {
            return;
        }

        filterBranches(companySelect, branchSelect);
        filterDepartments(companySelect, departmentSelect);

        refreshSelect(companySelect);
        refreshSelect(branchSelect);
        refreshSelect(departmentSelect);
    }

    function focusFirst(modal) {
        if (!modal) return;

        var first = modal.querySelector("[data-nxr-reassign-company], input:not([type='hidden']), select, textarea, button:not([data-nxr-reassign-close])");

        if (first) {
            setTimeout(function () {
                try {
                    first.focus();
                } catch (_) {
                }
            }, 60);
        }
    }

    function openModal(event) {
        if (event) {
            event.preventDefault();
            event.stopPropagation();

            if (typeof event.stopImmediatePropagation === "function") {
                event.stopImmediatePropagation();
            }
        }

        var modal = getModal();

        if (!modal) {
            console.warn("NEXORA reassign modal was not found.");
            return;
        }

        portalModalToBody(modal);

        modal.hidden = false;
        modal.classList.add("is-open");

        document.documentElement.classList.add("nxr-modal-open");
        document.body.classList.add("nxr-modal-open");

        syncOrganizationSelects();

        setTimeout(syncOrganizationSelects, 120);
        focusFirst(modal);
    }

    function closeModal(event) {
        if (event) {
            event.preventDefault();
            event.stopPropagation();
        }

        var modal = getModal();

        if (!modal) {
            return;
        }

        modal.hidden = true;
        modal.classList.remove("is-open");

        document.documentElement.classList.remove("nxr-modal-open");
        document.body.classList.remove("nxr-modal-open");
    }

    function bindButtons() {
        document.querySelectorAll("[data-nxr-reassign-open]").forEach(function (button) {
            if (button.dataset.nxrReassignBound === "1") {
                return;
            }

            button.dataset.nxrReassignBound = "1";
            button.addEventListener("click", openModal, true);

            button.addEventListener("keydown", function (event) {
                if (event.key === "Enter" || event.key === " ") {
                    openModal(event);
                }
            }, true);
        });

        document.querySelectorAll("[data-nxr-reassign-close]").forEach(function (button) {
            if (button.dataset.nxrReassignCloseBound === "1") {
                return;
            }

            button.dataset.nxrReassignCloseBound = "1";
            button.addEventListener("click", closeModal, true);
        });
    }

    document.addEventListener("change", function (event) {
        if (event.target.matches("[data-nxr-reassign-company]")) {
            syncOrganizationSelects();
            return;
        }

        if (event.target.matches("[data-nxr-reassign-branch]")) {
            syncOrganizationSelects();
            return;
        }
    }, true);

    document.addEventListener("click", function (event) {
        var openButton = event.target.closest("[data-nxr-reassign-open]");

        if (openButton) {
            openModal(event);
            return;
        }

        var closeButton = event.target.closest("[data-nxr-reassign-close]");

        if (closeButton) {
            closeModal(event);
            return;
        }

        var modal = getModal();

        if (modal && !modal.hidden && event.target === modal) {
            closeModal(event);
        }
    }, true);

    document.addEventListener("keydown", function (event) {
        if (event.key === "Escape") {
            closeModal(event);
        }
    }, true);

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", function () {
            bindButtons();
            syncOrganizationSelects();
        });
    } else {
        bindButtons();
        syncOrganizationSelects();
    }

    setTimeout(bindButtons, 250);
    setTimeout(bindButtons, 900);
    setTimeout(syncOrganizationSelects, 950);

    window.NEXORA_REASSIGN_MODAL_OPEN = openModal;
    window.NEXORA_REASSIGN_MODAL_CLOSE = closeModal;
    window.NEXORA_REASSIGN_MODAL_SYNC_ORG = syncOrganizationSelects;
})();