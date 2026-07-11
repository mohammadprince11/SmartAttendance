(() => {
    "use strict";

    function setModalInert(modal, value) {
        if (!modal) return;

        try {
            modal.inert = value;
        } catch (_) {
        }

        if (value) {
            modal.setAttribute("inert", "");
        } else {
            modal.removeAttribute("inert");
        }
    }

    function syncPageLock() {
        const hasOpenModal = Boolean(document.querySelector(".nx-setup-modal.is-open"));

        document.documentElement.classList.toggle("nx-setup-modal-open", hasOpenModal);
        document.body.classList.toggle("nx-setup-modal-open", hasOpenModal);
    }

    function refreshModalSelects(modal) {
        window.requestAnimationFrame(() => {
            if (window.NexoraSelect && typeof window.NexoraSelect.refresh === "function") {
                modal.querySelectorAll("select:not([multiple]):not([data-nexora-native='true'])").forEach(select => {
                    window.NexoraSelect.refresh(select);
                });
            }

            if (window.NexoraSelect && typeof window.NexoraSelect.refreshAll === "function") {
                window.setTimeout(() => window.NexoraSelect.refreshAll(), 80);
            }

            if (typeof window.NexoraRefreshSelectSystem === "function") {
                window.setTimeout(() => window.NexoraRefreshSelectSystem(), 80);
            }
        });
    }

    function openModal(id, opener) {
        const modal = document.getElementById(id);
        if (!modal) return;

        modal._nexoraOpener = opener || document.activeElement;
        setModalInert(modal, false);
        modal.setAttribute("aria-hidden", "false");
        modal.classList.add("is-open");
        syncPageLock();
        refreshModalSelects(modal);

        const focusTarget = modal.querySelector(
            "input:not([type='hidden']):not([disabled]), select:not([disabled]), textarea:not([disabled]), button:not([data-nx-setup-close]):not([disabled])"
        );

        if (focusTarget) {
            window.setTimeout(() => focusTarget.focus(), 50);
        }
    }

    function closeModal(modal) {
        if (!modal) return;

        const activeElement = document.activeElement;
        const opener = modal._nexoraOpener;

        if (activeElement && modal.contains(activeElement) && typeof activeElement.blur === "function") {
            activeElement.blur();
        }

        modal.classList.remove("is-open");
        modal.setAttribute("aria-hidden", "true");
        setModalInert(modal, true);
        syncPageLock();

        window.requestAnimationFrame(() => {
            if (opener && opener.isConnected && typeof opener.focus === "function") {
                opener.focus();
            }
        });
    }

    function initializeModals() {
        document.querySelectorAll(".nx-setup-modal").forEach(modal => {
            const isOpen = modal.classList.contains("is-open");

            modal.setAttribute("aria-hidden", isOpen ? "false" : "true");
            setModalInert(modal, !isOpen);
        });

        syncPageLock();
    }

    document.addEventListener("click", event => {
        const openButton = event.target.closest("[data-nx-setup-open]");

        if (openButton) {
            event.preventDefault();
            openModal(openButton.getAttribute("data-nx-setup-open"), openButton);
            return;
        }

        const closeButton = event.target.closest("[data-nx-setup-close]");

        if (closeButton) {
            event.preventDefault();
            closeModal(closeButton.closest(".nx-setup-modal"));
            return;
        }

        const modal = event.target.closest(".nx-setup-modal");

        if (modal && event.target === modal) {
            closeModal(modal);
        }
    });

    document.addEventListener("keydown", event => {
        if (event.key !== "Escape") return;

        const openModals = Array.from(document.querySelectorAll(".nx-setup-modal.is-open"));

        if (openModals.length > 0) {
            event.preventDefault();
            closeModal(openModals[openModals.length - 1]);
        }
    });

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initializeModals);
    } else {
        initializeModals();
    }
})();