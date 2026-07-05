/* NEXORA Add Employee Modal + Readiness Fix */
(function () {
    "use strict";

    function bindModal() {
        const overlay = document.querySelector("[data-nxr-documents-modal]");
        if (!overlay) return;

        const openers = document.querySelectorAll("[data-nxr-documents-modal-open]");
        const closers = overlay.querySelectorAll("[data-nxr-documents-modal-close]");

        function openModal(event) {
            if (event) {
                event.preventDefault();
                event.stopPropagation();
            }
            overlay.classList.add("open");
            overlay.setAttribute("aria-hidden", "false");
            document.body.style.overflow = "hidden";
        }

        function closeModal(event) {
            if (event) {
                event.preventDefault();
                event.stopPropagation();
            }
            overlay.classList.remove("open");
            overlay.setAttribute("aria-hidden", "true");
            document.body.style.overflow = "";
        }

        openers.forEach(btn => {
            btn.type = "button";
            btn.addEventListener("click", openModal, true);
        });

        closers.forEach(btn => {
            btn.type = "button";
            btn.addEventListener("click", closeModal, true);
        });

        overlay.addEventListener("click", event => {
            if (event.target === overlay) closeModal(event);
        }, true);

        document.addEventListener("keydown", event => {
            if (event.key === "Escape" && overlay.classList.contains("open")) {
                closeModal(event);
            }
        });
    }

    function updateReadiness() {
        const form = document.querySelector("#employee-create-form");
        if (!form) return;

        const requiredSelectors = [
            "[name='Employee.EmployeeNo']",
            "[name='Employee.FullName']",
            "[name='Employee.DepartmentId']",
            "[name='Employee.Position']",
            "[name='Employee.HireDate']",
            "[name='Employee.Phone']",
            "[name='Employee.Email']"
        ];

        const scoreText = document.querySelector("[data-nxr-readiness-score]");
        const percentText = document.querySelector("[data-nxr-readiness-percent]");
        const ring = document.querySelector(".nxr-progress-ring");
        const summary = document.querySelector("[data-nxr-readiness-summary]");

        function calculate() {
            let filled = 0;
            let total = requiredSelectors.length;

            requiredSelectors.forEach(selector => {
                const input = form.querySelector(selector);
                if (input && String(input.value || "").trim() !== "") filled++;
            });

            const percent = Math.round((filled / total) * 100);

            if (scoreText) scoreText.textContent = filled + " / " + total;
            if (percentText) percentText.textContent = percent + "%";
            if (ring) {
                const degrees = Math.round((percent / 100) * 360);
                ring.style.background = "conic-gradient(#12D9E3 " + degrees + "deg, rgba(18,217,227,.13) " + degrees + "deg 360deg)";
            }
            if (summary) {
                summary.textContent = percent >= 100
                    ? "البيانات الأساسية مكتملة وجاهزة للحفظ."
                    : "أكمل الحقول الرئيسية حتى يصبح ملف الموظف جاهزاً.";
            }
        }

        form.addEventListener("input", calculate);
        form.addEventListener("change", calculate);
        calculate();
    }

    document.addEventListener("DOMContentLoaded", function () {
        bindModal();
        updateReadiness();
    });
})();
