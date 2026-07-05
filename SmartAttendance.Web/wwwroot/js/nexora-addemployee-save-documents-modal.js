/* NEXORA Add Employee - Save Documents From Modal */
(function () {
    "use strict";

    const documentTypes = [
        "هوية / بطاقة وطنية",
        "عقد عمل",
        "جواز سفر",
        "بطاقة صحية",
        "شهادة / مؤهل",
        "صورة شخصية",
        "إقامة",
        "كتاب تعيين",
        "تعهد",
        "أخرى"
    ];

    function rowTemplate(index) {
        const options = documentTypes.map(type => `<option value="${type}">${type}</option>`).join("");
        const fileId = `InitialDocumentFile_${index}_${Date.now()}`;

        return `
            <div class="nxr-document-row" data-document-row>
                <div>
                    <label>نوع المستمسك</label>
                    <select form="employee-create-form" name="InitialDocumentTypes[${index}]" data-document-select>
                        <option value="">اختر نوع المستمسك</option>
                        ${options}
                    </select>
                </div>

                <div>
                    <label>مطلوب؟</label>
                    <select form="employee-create-form" name="InitialDocumentRequired[${index}]">
                        <option value="Yes">مطلوب</option>
                        <option value="No">اختياري</option>
                    </select>
                </div>

                <div class="nxr-document-file-control">
                    <label>الملف</label>
                    <label class="nxr-document-file-label" for="${fileId}" data-document-file-label>اختيار ملف</label>
                    <input form="employee-create-form" id="${fileId}" type="file" class="nxr-document-file-input" name="InitialDocumentFiles[${index}]" data-document-file />
                </div>

                <button type="button" class="nxr-document-remove" data-document-remove title="حذف">×</button>
            </div>
        `;
    }

    function setupDocuments() {
        const list = document.querySelector("[data-documents-dropdown-list]");
        const addButton = document.querySelector("[data-add-document-dropdown]");
        const count = document.querySelector("[data-documents-count]");

        if (!list || !addButton) return;

        list.innerHTML = "";

        function refresh() {
            const rows = Array.from(list.querySelectorAll("[data-document-row]"));

            rows.forEach((row, index) => {
                row.querySelectorAll("select, input[type='file']").forEach(control => {
                    const name = control.getAttribute("name") || "";

                    if (name.startsWith("InitialDocumentTypes")) {
                        control.setAttribute("name", `InitialDocumentTypes[${index}]`);
                    }

                    if (name.startsWith("InitialDocumentRequired")) {
                        control.setAttribute("name", `InitialDocumentRequired[${index}]`);
                    }

                    if (name.startsWith("InitialDocumentFiles")) {
                        control.setAttribute("name", `InitialDocumentFiles[${index}]`);
                    }

                    control.setAttribute("form", "employee-create-form");
                });
            });

            if (count) count.textContent = String(rows.length);
        }

        function wire(scope) {
            scope.querySelectorAll("[data-document-file]").forEach(input => {
                if (input.dataset.nxReady === "1") return;
                input.dataset.nxReady = "1";

                input.addEventListener("change", function () {
                    const row = input.closest("[data-document-row]");
                    const label = row ? row.querySelector("[data-document-file-label]") : null;
                    if (!label) return;

                    if (input.files && input.files.length > 0) {
                        label.textContent = "تم اختيار ملف";
                        label.classList.add("is-selected");
                    } else {
                        label.textContent = "اختيار ملف";
                        label.classList.remove("is-selected");
                    }
                });
            });
        }

        function addRow() {
            const index = list.querySelectorAll("[data-document-row]").length;
            list.insertAdjacentHTML("beforeend", rowTemplate(index));
            wire(list);
            refresh();
        }

        addButton.onclick = function (event) {
            event.preventDefault();
            event.stopPropagation();
            addRow();
        };

        list.addEventListener("click", function (event) {
            const remove = event.target.closest("[data-document-remove]");
            if (!remove) return;

            event.preventDefault();
            event.stopPropagation();

            const row = remove.closest("[data-document-row]");
            if (row) row.remove();

            if (list.querySelectorAll("[data-document-row]").length === 0) {
                addRow();
            }

            refresh();
        });

        addRow();
    }

    function patchModal() {
        const note = document.querySelector(".nxr-force-modal-note");
        if (note) {
            note.innerHTML = "<strong>طريقة العمل:</strong> اختر نوع المستمسك واختر الملف. عند الضغط على إنشاء الموظف سيتم حفظ الموظف وحفظ الملفات وربطها بملفه تلقائياً.";
        }

        document.querySelectorAll(".nxr-force-modal-actions a[href*='EmployeeDocuments']").forEach(a => a.remove());

        const closeBtn = document.querySelector(".nxr-force-modal-actions [data-force-documents-modal-close]");
        if (closeBtn) closeBtn.textContent = "تم";
    }

    document.addEventListener("DOMContentLoaded", function () {
        setupDocuments();
        patchModal();
    });
})();
