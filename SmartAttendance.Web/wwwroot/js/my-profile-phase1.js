(function () {
    function initEdit() {
        const form = document.querySelector("[data-profile-form]");
        const toggle = document.querySelector("[data-toggle-profile-edit]");
        const cancel = document.querySelector("[data-cancel-profile-edit]");
        if (!form || !toggle) return;
        toggle.addEventListener("click", function () { form.classList.add("is-editing"); });
        cancel && cancel.addEventListener("click", function () { form.classList.remove("is-editing"); });
    }

    function initRequestModal() {
        const modal = document.querySelector("[data-request-modal]");
        if (!modal) return;
        document.querySelectorAll("[data-open-request]").forEach(function (button) {
            button.addEventListener("click", function () { modal.classList.add("is-open"); });
        });
        document.querySelectorAll("[data-close-request]").forEach(function (button) {
            button.addEventListener("click", function () { modal.classList.remove("is-open"); });
        });
        modal.addEventListener("click", function (event) {
            if (event.target === modal) modal.classList.remove("is-open");
        });
    }

    function initRequestHints() {
        const select = document.querySelector("[data-request-type]");
        const hint = document.querySelector("[data-request-hint]");
        if (!select || !hint) return;
        const hints = {
            "Missing Punch": "نسيان بصمة: اختر التاريخ، وإذا توجد بصمة واحدة اكتب توضيح البصمة الموجودة في السبب.",
            "Exit Permission": "مغادرة: اختر التاريخ ووقت الخروج والعودة إن وجد.",
            "Overtime": "عمل إضافي: اختر التاريخ وساعات بداية ونهاية العمل الإضافي.",
            "Annual Leave": "إجازة سنوية: اختر تاريخ البداية والنهاية.",
            "Unpaid Leave": "إجازة بدون راتب: اختر تاريخ البداية والنهاية.",
            "Sick Leave": "إجازة مرضية: اختر تاريخ البداية والنهاية وأضف السبب.",
            "Work Leave": "إجازة عمل: اختر التاريخ واكتب سبب المهمة."
        };
        function update() { hint.textContent = hints[select.value] || "اختر نوع الطلب لإظهار التفاصيل المطلوبة."; }
        select.addEventListener("change", update);
        update();
    }

    function init() {
        initEdit();
        initRequestModal();
        initRequestHints();
    }

    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", init);
    else init();
})();
