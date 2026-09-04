
(function () {
    "use strict";

    function ready(fn) {
        if (document.readyState === "loading") {
            document.addEventListener("DOMContentLoaded", fn);
        } else {
            fn();
        }
    }

    function textOf(el) {
        return (el && el.textContent ? el.textContent : "").replace(/\s+/g, " ").trim();
    }

    function getRequiredType(card) {
        var title = textOf(card.querySelector("strong")).toLowerCase();

        if (title.includes("هوية") || title.includes("بطاقة وطنية")) return "ID";
        if (title.includes("عقد")) return "Contract";
        if (title.includes("جواز")) return "Passport";
        if (title.includes("إقامة") || title.includes("اقامة") || title.includes("فيزا")) return "Visa";
        if (title.includes("صحية") || title.includes("بطاقة صحية")) return "Health Card";

        return "";
    }

    function getTypeLabel(value) {
        switch (value) {
            case "ID": return "هوية / بطاقة وطنية";
            case "Contract": return "عقد عمل";
            case "Passport": return "جواز سفر";
            case "Visa": return "إقامة / فيزا";
            case "Health Card": return "بطاقة صحية";
            default: return value || "مستند";
        }
    }

    function ensureOption(select, value, label) {
        if (!select || !value) return;

        var exists = Array.from(select.options).some(function (opt) {
            return opt.value === value;
        });

        if (!exists) {
            var option = document.createElement("option");
            option.value = value;
            option.textContent = label || value;
            select.appendChild(option);
        }
    }

    function setSelectValue(select, value, label) {
        if (!select || !value) return;
        ensureOption(select, value, label);
        select.value = value;
        select.dispatchEvent(new Event("change", { bubbles: true }));
    }

    ready(function () {
        var rulesCenter = document.querySelector(".nxr-document-rules-center");
        if (!rulesCenter) return;

        var ruleCards = Array.from(rulesCenter.querySelectorAll(".nxr-doc-rule-card"));
        if (!ruleCards.length) return;

        var uploadPanel = document.querySelector(".nxr-documents-upload-panel, .nxr-upload-panel");
        var employeeSelect = document.querySelector("select[name='Input.EmployeeId'], select#Input_EmployeeId");
        var typeSelect = document.querySelector("select[name='Input.DocumentType'], select#Input_DocumentType");
        var fileInput = document.querySelector("input[type='file'][name='Input.File'], input#Input_File");
        var url = new URL(window.location.href);
        var employeeId = url.searchParams.get("employeeId") || url.searchParams.get("EmployeeId") || "";

        var head = rulesCenter.querySelector(".nxr-document-rules-head");
        if (head && !rulesCenter.querySelector(".nxr-document-rules-actions")) {
            var actions = document.createElement("div");
            actions.className = "nxr-document-rules-actions";

            var missingOnly = document.createElement("button");
            missingOnly.type = "button";
            missingOnly.className = "nxr-doc-rule-filter-btn";
            missingOnly.textContent = "عرض المتطلبات التي تحتاج متابعة";

            var showAll = document.createElement("button");
            showAll.type = "button";
            showAll.className = "nxr-doc-rule-filter-btn";
            showAll.textContent = "عرض الكل";

            missingOnly.addEventListener("click", function () {
                rulesCenter.classList.add("is-focus-mode");
            });

            showAll.addEventListener("click", function () {
                rulesCenter.classList.remove("is-focus-mode");
            });

            actions.appendChild(missingOnly);
            actions.appendChild(showAll);
            head.appendChild(actions);
        }

        function prepareUpload(card) {
            var type = getRequiredType(card);
            if (!type) return;

            ruleCards.forEach(function (item) {
                item.classList.remove("is-selected-rule");
            });
            card.classList.add("is-selected-rule");

            if (employeeId) {
                setSelectValue(employeeSelect, employeeId, employeeId);
            }

            setSelectValue(typeSelect, type, getTypeLabel(type));

            if (uploadPanel) {
                uploadPanel.classList.add("is-targeted-upload");

                var note = uploadPanel.querySelector(".nxr-upload-autofill-note");
                if (!note) {
                    note = document.createElement("div");
                    note.className = "nxr-upload-autofill-note";
                    uploadPanel.insertBefore(note, uploadPanel.firstElementChild);
                }

                note.innerHTML = "تم تجهيز نموذج الرفع لمستند: <strong>" + getTypeLabel(type) + "</strong>";

                uploadPanel.scrollIntoView({ behavior: "smooth", block: "start" });

                setTimeout(function () {
                    if (fileInput) fileInput.focus();
                }, 500);
            }
        }

        ruleCards.forEach(function (card) {
            if (card.dataset.nxrRulesActionReady === "1") return;
            card.dataset.nxrRulesActionReady = "1";

            var type = getRequiredType(card);
            var needsAction =
                card.classList.contains("missing") ||
                card.classList.contains("expired") ||
                card.classList.contains("review") ||
                card.classList.contains("expiring");

            if (!type || !needsAction) return;

            var btn = document.createElement("button");
            btn.type = "button";
            btn.className = "nxr-doc-rule-action-btn";

            if (card.classList.contains("missing") || card.classList.contains("expired")) {
                btn.classList.add("danger");
            } else {
                btn.classList.add("warn");
            }

            btn.textContent = card.classList.contains("expiring") ? "تحديث هذا المستند" : "رفع هذا المستند";

            btn.addEventListener("click", function () {
                prepareUpload(card);
            });

            card.appendChild(btn);
        });
    });
})();
