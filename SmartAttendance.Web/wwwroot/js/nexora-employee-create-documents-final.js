/* NEXORA Employee Create Documents - Final */
(function () {
    "use strict";

    const documentTypes = [
        ["National ID", "البطاقة الوطنية"],
        ["Residence Card", "بطاقة السكن"],
        ["Passport", "جواز السفر"],
        ["Health Card", "البطاقة الصحية"],
        ["Work Permit", "إجازة العمل"],
        ["Contract", "العقد"],
        ["Photo", "الصورة الشخصية"],
        ["Certificate", "الشهادة"],
        ["Other", "أخرى"]
    ];

    function qs(selector, root) {
        return (root || document).querySelector(selector);
    }

    function qsa(selector, root) {
        return Array.from((root || document).querySelectorAll(selector));
    }

    function escapeText(value) {
        return (value || "").replace(/[&<>'"]/g, function (char) {
            return ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", "'": "&#39;", '"': "&quot;" })[char];
        });
    }

    function createOptions(selectedValue) {
        return documentTypes.map(function (item) {
            const value = item[0];
            const label = item[1];
            const selected = value === selectedValue ? " selected" : "";
            return `<option value="${escapeText(value)}"${selected}>${escapeText(label)}</option>`;
        }).join("");
    }

    function initDocumentsModal() {
        const form = qs("#employee-create-form");
        const modal = qs("[data-nxr-documents-modal]");
        const list = qs("[data-nxr-documents-list]");
        const preview = qs("[data-nxr-documents-preview]");
        const count = qs("[data-nxr-documents-count]");

        if (!form || !modal || !list) return;

        function rows() {
            return qsa(".nxr-document-row", list);
        }

        function openModal(event) {
            if (event) {
                event.preventDefault();
                event.stopPropagation();
            }

            if (rows().length === 0) addRow();
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

            modal.classList.remove("open");
            modal.setAttribute("aria-hidden", "true");
            document.body.style.overflow = "";
            updateSummary();
            return false;
        }

        function updateIndexes() {
            rows().forEach(function (row, index) {
                const type = qs("[data-document-type]", row);
                const required = qs("[data-document-required]", row);
                const file = qs("[data-document-file]", row);

                if (type) type.name = `InitialDocumentTypes[${index}]`;
                if (required) required.name = `InitialDocumentRequired[${index}]`;
                if (file) {
                    file.name = `InitialDocumentFiles[${index}]`;
                    file.id = `InitialDocumentFiles_${index}`;
                }
            });
        }

        function selectedRows() {
            return rows().map(function (row) {
                const type = qs("[data-document-type]", row);
                const required = qs("[data-document-required]", row);
                const file = qs("[data-document-file]", row);
                const typeText = type && type.selectedOptions.length ? type.selectedOptions[0].textContent.trim() : "مستمسك";
                const requiredText = required && required.value === "Required" ? "مطلوب" : "اختياري";
                const fileName = file && file.files && file.files.length ? file.files[0].name : "بدون ملف";
                return { typeText, requiredText, fileName, hasFile: file && file.files && file.files.length > 0 };
            });
        }

        function updateSummary() {
            updateIndexes();
            const selected = selectedRows();
            const withFiles = selected.filter(x => x.hasFile);

            if (count) count.textContent = String(withFiles.length);

            if (!preview) return;

            if (withFiles.length === 0) {
                preview.textContent = "لم يتم اختيار أي مستمسك بعد.";
                return;
            }

            preview.innerHTML = withFiles.map(function (item) {
                return `<div><strong>${escapeText(item.typeText)}</strong> - ${escapeText(item.requiredText)} - ${escapeText(item.fileName)}</div>`;
            }).join("");
        }

        function addRow(event) {
            if (event) {
                event.preventDefault();
                event.stopPropagation();
            }

            const index = rows().length;
            const row = document.createElement("div");
            row.className = "nxr-document-row";
            row.innerHTML = `
                <div>
                    <label>نوع المستمسك</label>
                    <select data-document-type name="InitialDocumentTypes[${index}]">
                        ${createOptions(index === 0 ? "National ID" : "Other")}
                    </select>
                </div>
                <div>
                    <label>الحالة</label>
                    <select data-document-required name="InitialDocumentRequired[${index}]">
                        <option value="Required">مطلوب</option>
                        <option value="Optional">اختياري</option>
                    </select>
                </div>
                <div>
                    <label>اختيار الملف</label>
                    <button type="button" class="nxr-document-file-trigger" data-document-file-trigger>&#1575;&#1582;&#1578;&#1610;&#1575;&#1585; &#1605;&#1604;&#1601;</button>
                    <input id="InitialDocumentFiles_${index}" type="file" class="nxr-document-file-input" name="InitialDocumentFiles[${index}]" data-document-file accept=".pdf,.jpg,.jpeg,.png,.doc,.docx,.xls,.xlsx" />
                    <div class="nxr-document-file-name" data-document-file-name></div>
                </div>
                <button type="button" class="nxr-document-remove" data-document-remove aria-label="حذف المستمسك">×</button>
            `;

            list.appendChild(row);
            updateSummary();
            const file = qs("[data-document-file]", row);
            if (file) setTimeout(function () { file.focus(); }, 30);
            return false;
        }

        qsa("[data-nxr-documents-open]").forEach(function (button) {
            button.type = "button";
            button.addEventListener("click", openModal);
        });

        qsa("[data-nxr-documents-close]", modal).forEach(function (button) {
            button.type = "button";
            button.addEventListener("click", closeModal);
        });

        qsa("[data-nxr-document-add]").forEach(function (button) {
            button.type = "button";
            button.addEventListener("click", addRow);
        });

        modal.addEventListener("click", function (event) {
            if (event.target === modal) closeModal(event);
        });

        document.addEventListener("keydown", function (event) {
            if (event.key === "Escape" && modal.classList.contains("open")) closeModal(event);
        });

        list.addEventListener("click", function (event) {
            const trigger = event.target.closest("[data-document-file-trigger]");
            if (!trigger) return;

            event.preventDefault();
            const row = trigger.closest(".nxr-document-row");
            const file = row ? qs("[data-document-file]", row) : null;
            if (file) file.click();
        });
        list.addEventListener("click", function (event) {
            const remove = event.target.closest("[data-document-remove]");
            if (!remove) return;
            event.preventDefault();
            const row = remove.closest(".nxr-document-row");
            if (row) row.remove();
            updateSummary();
        });

        list.addEventListener("change", function (event) {
            if (!event.target.matches("[data-document-file]")) return;

            const row = event.target.closest(".nxr-document-row");
            const trigger = row ? qs("[data-document-file-trigger]", row) : null;
            if (!trigger) return;

            if (event.target.files && event.target.files.length) {
                trigger.textContent = event.target.files[0].name;
                trigger.classList.add("is-selected");
            } else {
                trigger.textContent = "\u0627\u062e\u062a\u064a\u0627\u0631 \u0645\u0644\u0641";
                trigger.classList.remove("is-selected");
            }
        });
        list.addEventListener("change", function (event) {
            const row = event.target.closest(".nxr-document-row");
            if (!row) return;
            const file = qs("[data-document-file]", row);
            const label = qs("[data-document-file-name]", row);
            if (label && file && file.files && file.files.length) {
                label.textContent = file.files[0].name;
            } else if (label) {
                label.textContent = "";
            }
            updateSummary();
        });

        form.addEventListener("submit", function () {
            updateIndexes();
            updateSummary();
        });
    }

    document.addEventListener("DOMContentLoaded", initDocumentsModal);
})();

