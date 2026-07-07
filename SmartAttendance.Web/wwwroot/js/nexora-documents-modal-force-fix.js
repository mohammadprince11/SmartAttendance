/* NEXORA Documents Modal Force Fix */
(function () {
    "use strict";

    function openModal(event) {
        if (event) {
            event.preventDefault();
            event.stopPropagation();
        }

        var modal = document.getElementById("nxrDocumentsModalForce");
        if (!modal) return false;

        modal.classList.add("open");
        modal.setAttribute("aria-hidden", "false");
        document.body.style.overflow = "hidden";
        return false;
    }

    function closeModal(event) {
        if (event) {
            event.preventDefault();
            event.stopPropagation();
        }

        var modal = document.getElementById("nxrDocumentsModalForce");
        if (!modal) return false;

        modal.classList.remove("open");
        modal.setAttribute("aria-hidden", "true");
        document.body.style.overflow = "";
        return false;
    }

    window.NexoraOpenDocumentsModal = openModal;
    window.NexoraCloseDocumentsModal = closeModal;

    document.addEventListener("click", function (event) {
        var openButton = event.target.closest("[data-force-documents-modal-open]");
        if (openButton) {
            openModal(event);
            return;
        }

        var closeButton = event.target.closest("[data-force-documents-modal-close]");
        if (closeButton) {
            closeModal(event);
            return;
        }

        var modal = document.getElementById("nxrDocumentsModalForce");
        if (modal && event.target === modal) {
            closeModal(event);
        }
    }, true);

    document.addEventListener("keydown", function (event) {
        if (event.key === "Escape") {
            var modal = document.getElementById("nxrDocumentsModalForce");
            if (modal && modal.classList.contains("open")) {
                closeModal(event);
            }
        }
    });
})();

