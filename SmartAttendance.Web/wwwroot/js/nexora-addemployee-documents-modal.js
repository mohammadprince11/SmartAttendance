/* NEXORA - Add Employee Documents Modal */
(function () {
    "use strict";

    function setupDocumentsModal() {
        const openButtons = document.querySelectorAll("[data-nxr-documents-modal-open]");
        const overlay = document.querySelector("[data-nxr-documents-modal]");
        if (!overlay || openButtons.length === 0) return;

        const closeButtons = overlay.querySelectorAll("[data-nxr-documents-modal-close]");

        function openModal() {
            overlay.classList.add("open");
            document.body.style.overflow = "hidden";
            const close = overlay.querySelector("[data-nxr-documents-modal-close]");
            if (close) close.focus();
        }

        function closeModal() {
            overlay.classList.remove("open");
            document.body.style.overflow = "";
        }

        openButtons.forEach(button => {
            button.addEventListener("click", event => {
                event.preventDefault();
                openModal();
            });
        });

        closeButtons.forEach(button => {
            button.addEventListener("click", closeModal);
        });

        overlay.addEventListener("click", event => {
            if (event.target === overlay) closeModal();
        });

        document.addEventListener("keydown", event => {
            if (event.key === "Escape" && overlay.classList.contains("open")) {
                closeModal();
            }
        });
    }

    document.addEventListener("DOMContentLoaded", setupDocumentsModal);
})();
