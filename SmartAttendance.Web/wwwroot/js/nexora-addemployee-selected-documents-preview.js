/* NEXORA Add Employee - Selected Documents Preview */
(function () {
    "use strict";

    function getDocumentRows() {
        return Array.from(document.querySelectorAll("[data-document-row]"));
    }

    function getSelectedDocuments() {
        return getDocumentRows().map((row, index) => {
            const typeSelect = row.querySelector("[data-document-select]");
            const requiredSelect = row.querySelector("select[name^='InitialDocumentRequired']");
            const fileInput = row.querySelector("[data-document-file]");

            const file = fileInput && fileInput.files && fileInput.files.length > 0
                ? fileInput.files[0]
                : null;

            return {
                index: index + 1,
                type: typeSelect && typeSelect.value ? typeSelect.value : "غير محدد",
                required: requiredSelect && requiredSelect.value === "No" ? "اختياري" : "مطلوب",
                fileName: file ? file.name : "",
                hasFile: !!file
            };
        }).filter(x => x.hasFile || x.type !== "غير محدد");
    }

    function ensurePreviewInCreatePage() {
        const sectionButton = document.querySelector("[data-force-documents-modal-open], [data-nxr-documents-modal-open]");
        if (!sectionButton) return null;

        const section = sectionButton.closest(".nxr-card, .nxr-form-section, section");
        if (!section) return null;

        let preview = section.querySelector("[data-selected-documents-preview]");
        if (preview) return preview;

        preview = document.createElement("div");
        preview.className = "nxr-selected-documents-preview";
        preview.setAttribute("data-selected-documents-preview", "true");
        preview.innerHTML = `
            <div class="nxr-selected-documents-empty" data-selected-documents-empty>
                لم تتم إضافة مستمسكات بعد. اضغط اختيار المستمسكات وحدد النوع والملف.
            </div>
            <div class="nxr-selected-documents-list" data-selected-documents-list></div>
        `;

        const actions = section.querySelector(".nxr-actions");
        if (actions) {
            actions.insertAdjacentElement("afterend", preview);
        } else {
            section.appendChild(preview);
        }

        return preview;
    }

    function ensureModalSelectedArea() {
        const body = document.querySelector(".nxr-force-modal-body");
        if (!body) return null;

        let area = body.querySelector("[data-modal-selected-files]");
        if (area) return area;

        area = document.createElement("div");
        area.className = "nxr-modal-selected-files";
        area.setAttribute("data-modal-selected-files", "true");
        area.innerHTML = `
            <div class="nxr-modal-selected-title">الملفات المختارة</div>
            <div class="nxr-modal-selected-empty" data-modal-selected-empty>
                لا توجد ملفات مختارة حالياً.
            </div>
            <div class="nxr-selected-documents-list" data-modal-selected-list></div>
            <div class="nxr-modal-save-hint">
                عند الضغط على إنشاء الموظف سيتم حفظ الملفات المختارة وربطها بملف الموظف تلقائياً.
            </div>
        `;

        const addButton = body.querySelector("[data-add-document-dropdown]");
        if (addButton) {
            addButton.insertAdjacentElement("afterend", area);
        } else {
            body.appendChild(area);
        }

        return area;
    }

    function renderPreview() {
        const docs = getSelectedDocuments();

        const preview = ensurePreviewInCreatePage();
        if (preview) {
            const empty = preview.querySelector("[data-selected-documents-empty]");
            const list = preview.querySelector("[data-selected-documents-list]");

            if (docs.length === 0) {
                if (empty) empty.style.display = "";
                if (list) list.innerHTML = "";
            } else {
                if (empty) empty.style.display = "none";
                if (list) {
                    list.innerHTML = docs.map(doc => `
                        <div class="nxr-selected-document-item">
                            <div class="nxr-selected-document-main">
                                <strong>${doc.type}</strong>
                                <span>${doc.fileName || "لم يتم اختيار ملف لهذا المستمسك"}</span>
                            </div>
                            <div class="nxr-selected-document-pill">${doc.required}</div>
                        </div>
                    `).join("");
                }
            }
        }

        const modalArea = ensureModalSelectedArea();
        if (modalArea) {
            const empty = modalArea.querySelector("[data-modal-selected-empty]");
            const list = modalArea.querySelector("[data-modal-selected-list]");

            if (docs.length === 0) {
                if (empty) empty.style.display = "";
                if (list) list.innerHTML = "";
            } else {
                if (empty) empty.style.display = "none";
                if (list) {
                    list.innerHTML = docs.map(doc => `
                        <div class="nxr-modal-selected-item">
                            <div>
                                <strong>${doc.type}</strong>
                                <span>${doc.fileName || "بدون ملف"}</span>
                            </div>
                            <div class="nxr-selected-document-pill">${doc.required}</div>
                        </div>
                    `).join("");
                }
            }
        }
    }

    function wireEvents() {
        document.addEventListener("change", function (event) {
            if (
                event.target.matches("[data-document-file]") ||
                event.target.matches("[data-document-select]") ||
                event.target.matches("select[name^='InitialDocumentRequired']")
            ) {
                setTimeout(renderPreview, 20);
            }
        }, true);

        document.addEventListener("click", function (event) {
            if (
                event.target.closest("[data-add-document-dropdown]") ||
                event.target.closest("[data-document-remove]") ||
                event.target.closest("[data-force-documents-modal-open]")
            ) {
                setTimeout(renderPreview, 80);
            }
        }, true);

        const form = document.getElementById("employee-create-form");
        if (form && !form.dataset.documentsValidationReady) {
            form.dataset.documentsValidationReady = "1";
            form.addEventListener("submit", function (event) {
                const rows = getDocumentRows();
                const invalidRows = rows.filter(row => {
                    const type = row.querySelector("[data-document-select]");
                    const file = row.querySelector("[data-document-file]");
                    const hasType = type && type.value;
                    const hasFile = file && file.files && file.files.length > 0;

                    // إذا اختار نوع المستمسك لازم يختار ملف.
                    return hasType && !hasFile;
                });

                if (invalidRows.length > 0) {
                    event.preventDefault();
                    alert("يوجد مستمسك محدد بدون ملف. اختر الملف أو احذف الصف.");
                    const open = window.NexoraOpenDocumentsModal;
                    if (typeof open === "function") open(event);
                    renderPreview();
                }
            }, true);
        }
    }

    function patchLabels() {
        document.querySelectorAll("[data-force-documents-modal-open], [data-nxr-documents-modal-open]").forEach(btn => {
            btn.textContent = "اختيار المستمسكات";
        });

        const note = document.querySelector(".nxr-force-modal-note");
        if (note) {
            note.innerHTML = "<strong>طريقة العمل:</strong> اختر نوع المستمسك واختر الملف الخاص به. بعد الاختيار ستظهر الملفات في صفحة إضافة الموظف، وعند إنشاء الموظف سيتم حفظها تلقائياً.";
        }
    }

    document.addEventListener("DOMContentLoaded", function () {
        patchLabels();
        ensurePreviewInCreatePage();
        ensureModalSelectedArea();
        wireEvents();
        setTimeout(renderPreview, 120);
    });
})();
