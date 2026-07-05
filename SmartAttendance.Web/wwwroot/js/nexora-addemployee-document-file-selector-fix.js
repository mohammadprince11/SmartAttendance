/* NEXORA Add Employee - Documents Dropdown + File Selector */
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
                    <select name="InitialDocumentTypes[${index}]" data-document-select>
                        <option value="">اختر نوع المستمسك</option>
                        ${options}
                    </select>
                </div>

                <div>
                    <label>مطلوب؟</label>
                    <select name="InitialDocumentRequired[${index}]">
                        <option value="Yes">مطلوب</option>
                        <option value="No">اختياري</option>
                    </select>
                </div>

                <div class="nxr-document-file-control">
                    <label>الملف</label>
                    <label class="nxr-document-file-label" for="${fileId}">اختيار ملف</label>
                    <input id="${fileId}" type="file" class="nxr-document-file-input" name="InitialDocumentFiles[${index}]" data-document-file />
                    <div class="nxr-document-file-name" data-document-file-name>لم يتم اختيار ملف</div>
                </div>

                <button type="button" class="nxr-document-remove" data-document-remove title="حذف">×</button>
            </div>
        `;
    }

    function setupDocumentDropdowns() {
        const list = document.querySelector("[data-documents-dropdown-list]");
        const addButton = document.querySelector("[data-add-document-dropdown]");
        const count = document.querySelector("[data-documents-count]");

        if (!list || !addButton) return;

        // Rebuild existing rows once to guarantee every row has a file selector.
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
                });
            });

            if (count) count.textContent = String(rows.length);
        }

        function wireFileInputs(scope) {
            scope.querySelectorAll("[data-document-file]").forEach(input => {
                if (input.dataset.fileReady === "1") return;
                input.dataset.fileReady = "1";

                input.addEventListener("change", function () {
                    const row = input.closest("[data-document-row]");
                    const nameBox = row ? row.querySelector("[data-document-file-name]") : null;
                    if (!nameBox) return;

                    if (input.files && input.files.length > 0) {
                        nameBox.textContent = input.files[0].name;
                        nameBox.classList.add("has-file");
                    } else {
                        nameBox.textContent = "لم يتم اختيار ملف";
                        nameBox.classList.remove("has-file");
                    }
                });
            });
        }

        function addRow() {
            const index = list.querySelectorAll("[data-document-row]").length;
            list.insertAdjacentHTML("beforeend", rowTemplate(index));
            wireFileInputs(list);
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

        // Start with one row only.
        addRow();
    }

    function patchModalText() {
        const note = document.querySelector(".nxr-force-modal-note");
        if (note) {
            note.innerHTML = "<strong>طريقة العمل:</strong> اختر نوع المستمسك ثم اختر الملف الخاص به. يمكن إضافة أكثر من مستمسك من زر + إضافة مستمسك.";
        }

        const action = document.querySelector(".nxr-force-modal-actions a[href='/EmployeeDocuments']");
        if (action) {
            action.textContent = "فتح صفحة المستندات لاحقاً";
        }
    }

    document.addEventListener("DOMContentLoaded", function () {
        setupDocumentDropdowns();
        patchModalText();
    });
})();
