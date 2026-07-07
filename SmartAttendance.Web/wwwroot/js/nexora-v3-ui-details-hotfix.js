/* NEXORA V3 UI Details Hotfix */
(function () {
    "use strict";

    function setupFilePicker() {
        document.querySelectorAll(".nxr-file-input").forEach(input => {
            const wrapper = input.closest(".nxr-upload-zone-final") || input.parentElement;
            const nameBox = wrapper ? wrapper.querySelector(".nxr-file-name") : null;

            function updateName() {
                if (!nameBox) return;

                if (input.files && input.files.length > 0) {
                    nameBox.textContent = input.files[0].name;
                    nameBox.classList.remove("empty");
                } else {
                    nameBox.textContent = "لم يتم اختيار ملف";
                    nameBox.classList.add("empty");
                }
            }

            input.addEventListener("change", updateName);
            updateName();
        });
    }

    function setupNavGroups() {
        const groups = Array.from(document.querySelectorAll(".nexora-nav-group"));

        groups.forEach(group => {
            const summary = group.querySelector(":scope > summary");
            if (!summary || summary.dataset.nxrToggleReady === "1") return;

            summary.dataset.nxrToggleReady = "1";

            summary.addEventListener("click", event => {
                event.preventDefault();

                const willOpen = !group.open;

                // Accordion behavior: close siblings, but allow current group to close.
                const nav = group.closest(".nexora-nav");
                if (nav) {
                    nav.querySelectorAll(".nexora-nav-group").forEach(other => {
                        if (other !== group) other.open = false;
                    });
                }

                group.open = willOpen;
            });
        });
    }

    function fixRulesButton() {
        document.querySelectorAll("form.nx-grid .nx-actions .nx-btn, form.nx-grid button").forEach(btn => {
            btn.style.writingMode = "horizontal-tb";
            btn.style.minHeight = "44px";
            btn.style.height = "44px";
            btn.style.width = "auto";
        });
    }

    document.addEventListener("DOMContentLoaded", () => {
        setupFilePicker();
        setupNavGroups();
        fixRulesButton();
    });
})();

