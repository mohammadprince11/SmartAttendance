(function () {
    function openModal(modal) {
        if (modal) modal.classList.add("is-open");
    }

    function closeModal(modal) {
        if (modal) modal.classList.remove("is-open");
    }

    // ===== أزواج البصمات: اليوم قد يحمل أكثر من زوج، والمحرك يشتق الحالة منها =====
    let pairIndex = 0;

    function pairRow(pair) {
        const i = pairIndex++;
        const row = document.createElement("div");
        row.className = "punch-pair";
        row.innerHTML =
            '<input type="hidden" name="PunchPairs[' + i + '].Id" value="' + (pair && pair.id ? pair.id : 0) + '" />' +
            '<div class="hrms-field"><label>الدخول</label>' +
            '<input type="time" name="PunchPairs[' + i + '].CheckIn" value="' + (pair && pair.inTime ? pair.inTime : "") + '" /></div>' +
            '<div class="hrms-field"><label>الخروج</label>' +
            '<input type="time" name="PunchPairs[' + i + '].CheckOut" value="' + (pair && pair.outTime ? pair.outTime : "") + '" /></div>' +
            '<button type="button" class="punch-remove" title="إفراغ الزوج (يُحذف عند الحفظ)">×</button>';

        row.querySelector(".punch-remove").addEventListener("click", function () {
            row.querySelectorAll('input[type="time"]').forEach(function (input) { input.value = ""; });
            row.classList.add("is-cleared");
        });

        return row;
    }

    function initEditModal() {
        const modal = document.querySelector("[data-edit-modal]");
        if (!modal) return;

        const selected = modal.querySelector("[data-edit-selected]");
        const employeeNo = modal.querySelector("#editEmployeeNo");
        const date = modal.querySelector("#editDate");
        const notes = modal.querySelector("#editNotes");
        const pairsBox = modal.querySelector("#editPairs");

        const addButton = modal.querySelector("[data-add-pair]");
        if (addButton) {
            addButton.addEventListener("click", function () {
                pairsBox.appendChild(pairRow(null));
            });
        }

        document.querySelectorAll("[data-edit-attendance]").forEach(function (button) {
            button.addEventListener("click", function () {
                if (employeeNo) employeeNo.value = button.dataset.employeeno || "";
                if (date) date.value = button.dataset.date || "";
                if (notes) notes.value = "";

                // أزواج اليوم من خريطة الخادم — وإن غابت يُفتح زوج فارغ للإضافة
                const key = (button.dataset.employeeno || "") + "|" + (button.dataset.date || "");
                const pairs = (window.ATTENDANCE_PAIRS || {})[key] || [];

                pairIndex = 0;
                pairsBox.innerHTML = "";

                if (pairs.length) {
                    pairs.forEach(function (pair) { pairsBox.appendChild(pairRow(pair)); });
                } else {
                    pairsBox.appendChild(pairRow({
                        id: 0,
                        inTime: button.dataset.checkin || "",
                        outTime: button.dataset.checkout || ""
                    }));
                }

                if (selected) {
                    selected.textContent = (button.dataset.employeeno || "") + " - " + (button.dataset.employee || "") + " | " + (button.dataset.date || "")
                        + " · " + pairs.length + " زوج";
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

    // مودال «البصمات الأخرى» — نفس نمط مودال الاستيراد (فتح/إغلاق/نقر الخلفية)
    function initOtherModal() {
        const modal = document.querySelector("[data-other-modal]");
        if (!modal) return;

        document.querySelectorAll("[data-open-other]").forEach(function (button) {
            button.addEventListener("click", function () {
                openModal(modal);
            });
        });

        document.querySelectorAll("[data-close-other]").forEach(function (button) {
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
        initOtherModal();
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", init);
    } else {
        init();
    }
})();
