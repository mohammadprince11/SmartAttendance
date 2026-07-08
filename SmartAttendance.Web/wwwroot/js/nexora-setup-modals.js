(() => {
    function openModal(id) {
        const modal = document.getElementById(id);
        if (!modal) return;

        modal.classList.add("is-open");
        modal.setAttribute("aria-hidden", "false");
        document.documentElement.classList.add("nx-setup-modal-open");
        document.body.classList.add("nx-setup-modal-open");

        /* NEXORA_SETUP_MODAL_DROPDOWN_REFRESH_START */
        window.requestAnimationFrame(() => {
            if (window.NexoraSelect && typeof window.NexoraSelect.refresh === "function") {
                modal.querySelectorAll("select:not([multiple]):not([data-nexora-native='true'])").forEach(select => {
                    window.NexoraSelect.refresh(select);
                });
            }

            if (window.NexoraSelect && typeof window.NexoraSelect.refreshAll === "function") {
                window.setTimeout(() => window.NexoraSelect.refreshAll(), 80);
            }
        });
        /* NEXORA_SETUP_MODAL_DROPDOWN_REFRESH_END */
        const focusTarget = modal.querySelector("input, select, textarea, button");
        if (focusTarget) {
            setTimeout(() => focusTarget.focus(), 50);
        }
    }

    function closeModal(modal) {
        if (!modal) return;

        modal.classList.remove("is-open");
        modal.setAttribute("aria-hidden", "true");

        if (!document.querySelector(".nx-setup-modal.is-open")) {
            document.documentElement.classList.remove("nx-setup-modal-open");
            document.body.classList.remove("nx-setup-modal-open");
        }
    }

    document.addEventListener("click", (event) => {
        const openButton = event.target.closest("[data-nx-setup-open]");
        if (openButton) {
            event.preventDefault();
            openModal(openButton.getAttribute("data-nx-setup-open"));
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

    document.addEventListener("keydown", (event) => {
        if (event.key !== "Escape") return;

        document.querySelectorAll(".nx-setup-modal.is-open").forEach(closeModal);
    });
})();
