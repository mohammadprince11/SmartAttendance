(function () {
    const modal = document.querySelector("[data-position-modal]");
    const backdrop = document.querySelector("[data-position-backdrop]");

    function openModal() {
        if (backdrop) backdrop.hidden = false;
        if (modal) modal.hidden = false;
        document.body.style.overflow = "hidden";
        window.setTimeout(() => document.querySelector("#positionName")?.focus(), 80);
    }

    function closeModal() {
        if (backdrop) backdrop.hidden = true;
        if (modal) modal.hidden = true;
        document.body.style.overflow = "";
    }

    function clearForm() {
        document.querySelector("#positionId").value = "0";
        document.querySelector("#positionName").value = "";
        document.querySelector("#positionCode").value = "";
        document.querySelector("#positionDescription").value = "";
        document.querySelector("#positionIsActive").checked = true;
        document.querySelector("[data-position-modal-title]").textContent = "إضافة منصب";
    }

    function init() {
        closeModal();

        document.querySelector("[data-open-position-modal]")?.addEventListener("click", function () {
            clearForm();
            openModal();
        });

        document.querySelectorAll("[data-close-position-modal]").forEach(button => {
            button.addEventListener("click", function (event) {
                event.preventDefault();
                closeModal();
            });
        });

        backdrop?.addEventListener("click", closeModal);

        document.addEventListener("keydown", function (event) {
            if (event.key === "Escape") closeModal();
        });

        document.querySelectorAll("[data-edit-position]").forEach(button => {
            button.addEventListener("click", function () {
                document.querySelector("#positionId").value = button.dataset.id || "0";
                document.querySelector("#positionName").value = button.dataset.name || "";
                document.querySelector("#positionCode").value = button.dataset.code || "";
                document.querySelector("#positionDescription").value = button.dataset.description || "";
                document.querySelector("#positionIsActive").checked = button.dataset.active === "true";
                document.querySelector("[data-position-modal-title]").textContent = "تعديل منصب";
                openModal();
            });
        });
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", init);
    } else {
        init();
    }
})();
