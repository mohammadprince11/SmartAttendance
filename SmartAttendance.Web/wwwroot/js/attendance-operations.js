(function () {
    function initCorrectionModal() {
        const modal = document.querySelector("[data-correction-modal]");
        if (!modal) return;

        const selected = modal.querySelector("[data-selected-record]");
        const id = modal.querySelector("#correctionId");
        const employeeNo = modal.querySelector("#correctionEmployeeNo");
        const recordDate = modal.querySelector("#correctionDate");
        const checkIn = modal.querySelector("#correctionCheckIn");
        const checkOut = modal.querySelector("#correctionCheckOut");
        const status = modal.querySelector("#correctionStatus");
        const notes = modal.querySelector("#correctionNotes");

        document.querySelectorAll("[data-correct]").forEach(function (button) {
            button.addEventListener("click", function () {
                id.value = button.dataset.id || "";
                if (employeeNo) employeeNo.value = button.dataset.employeeno || "";
                if (recordDate) recordDate.value = button.dataset.recorddate || "";
                checkIn.value = button.dataset.checkin || "";
                checkOut.value = button.dataset.checkout || "";
                status.value = button.dataset.status || "1";
                notes.value = button.dataset.notes || "";

                if (selected) {
                    selected.textContent = (button.dataset.employee || "") + " | " + (button.dataset.date || "");
                }

                modal.classList.add("is-open");
            });
        });

        document.querySelectorAll("[data-close-correction]").forEach(function (button) {
            button.addEventListener("click", function () {
                modal.classList.remove("is-open");
            });
        });

        modal.addEventListener("click", function (event) {
            if (event.target === modal) {
                modal.classList.remove("is-open");
            }
        });
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initCorrectionModal);
    } else {
        initCorrectionModal();
    }
})();

