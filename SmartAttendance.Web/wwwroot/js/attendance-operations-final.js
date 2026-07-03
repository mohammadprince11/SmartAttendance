(function () {
    function openModal(modal) {
        if (modal) modal.classList.add("is-open");
    }

    function closeModal(modal) {
        if (modal) modal.classList.remove("is-open");
    }

    function initEditModal() {
        const modal = document.querySelector("[data-edit-modal]");
        if (!modal) return;

        const selected = modal.querySelector("[data-edit-selected]");
        const employeeNo = modal.querySelector("#editEmployeeNo");
        const date = modal.querySelector("#editDate");
        const checkIn = modal.querySelector("#editCheckIn");
        const checkOut = modal.querySelector("#editCheckOut");
        const status = modal.querySelector("#editStatus");
        const notes = modal.querySelector("#editNotes");

        document.querySelectorAll("[data-edit-attendance]").forEach(function (button) {
            button.addEventListener("click", function () {
                if (employeeNo) employeeNo.value = button.dataset.employeeno || "";
                if (date) date.value = button.dataset.date || "";
                if (checkIn) checkIn.value = button.dataset.checkin || "";
                if (checkOut) checkOut.value = button.dataset.checkout || "";
                if (status) status.value = button.dataset.status || "1";
                if (notes) notes.value = "";

                if (selected) {
                    selected.textContent = (button.dataset.employeeno || "") + " - " + (button.dataset.employee || "") + " | " + (button.dataset.date || "");
                }

                openModal(modal);
            });
        });

        document.querySelectorAll("[data-close-edit]").forEach(function (button) {
            button.addEventListener("click", function () {
                closeModal(modal);
            });
        });

        modal.addEventListener("click", function (event) {
            if (event.target === modal) closeModal(modal);
        });
    }

    function initImportModal() {
        const modal = document.querySelector("[data-import-modal]");
        if (!modal) return;

        document.querySelectorAll("[data-open-import]").forEach(function (button) {
            button.addEventListener("click", function () {
                openModal(modal);
            });
        });

        document.querySelectorAll("[data-close-import]").forEach(function (button) {
            button.addEventListener("click", function () {
                closeModal(modal);
            });
        });

        modal.addEventListener("click", function (event) {
            if (event.target === modal) closeModal(modal);
        });
    }

    function init() {
        initEditModal();
        initImportModal();
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", init);
    } else {
        init();
    }
})();
