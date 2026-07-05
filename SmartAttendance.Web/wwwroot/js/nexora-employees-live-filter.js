
(function () {
    "use strict";

    function ready(callback) {
        if (document.readyState === "loading") {
            document.addEventListener("DOMContentLoaded", callback);
        } else {
            callback();
        }
    }

    function cleanEmptyFields(form) {
        Array.from(form.elements).forEach(function (el) {
            if (!el.name) return;
            if ((el.tagName === "INPUT" || el.tagName === "SELECT") && (el.value || "").trim() === "") {
                el.disabled = true;
                el.dataset.nxrDisabledForSubmit = "1";
            }
        });
    }

    function restoreDisabledFields(form) {
        Array.from(form.elements).forEach(function (el) {
            if (el.dataset && el.dataset.nxrDisabledForSubmit === "1") {
                el.disabled = false;
                delete el.dataset.nxrDisabledForSubmit;
            }
        });
    }

    ready(function () {
        var form = document.querySelector("[data-nxr-live-filter]");
        if (!form) return;

        var hint = form.querySelector("[data-nxr-live-filter-hint]");
        var timer = null;
        var isSubmitting = false;

        function submitNow(message) {
            if (isSubmitting) return;

            isSubmitting = true;
            form.classList.add("is-submitting");

            if (hint) {
                hint.textContent = message || "جاري تطبيق الفلتر...";
            }

            cleanEmptyFields(form);

            try {
                if (form.requestSubmit) {
                    form.requestSubmit();
                } else {
                    form.submit();
                }
            } catch (e) {
                restoreDisabledFields(form);
                isSubmitting = false;
                form.classList.remove("is-submitting");
                if (hint) hint.textContent = "اضغط Enter للتطبيق";
            }
        }

        form.querySelectorAll("[data-nxr-live-select]").forEach(function (select) {
            select.addEventListener("change", function () {
                submitNow("تم الاختيار، جاري تحديث النتائج...");
            });
        });

        form.querySelectorAll("[data-nxr-live-search]").forEach(function (input) {
            input.addEventListener("input", function () {
                clearTimeout(timer);

                if (hint) {
                    hint.textContent = "سيتم البحث تلقائياً...";
                }

                timer = setTimeout(function () {
                    submitNow("جاري البحث...");
                }, 650);
            });

            input.addEventListener("keydown", function (event) {
                if (event.key === "Enter") {
                    event.preventDefault();
                    clearTimeout(timer);
                    submitNow("جاري البحث...");
                }
            });
        });

        form.addEventListener("submit", function () {
            if (!isSubmitting) {
                isSubmitting = true;
                form.classList.add("is-submitting");
                if (hint) hint.textContent = "جاري تحديث النتائج...";
                cleanEmptyFields(form);
            }
        });

        window.addEventListener("pageshow", function () {
            restoreDisabledFields(form);
            isSubmitting = false;
            form.classList.remove("is-submitting");
        });
    });
})();
