/* NEXORA Add Employee - Documents Dropdown Modal */
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

                <button type="button" class="nxr-document-remove" data-document-remove title="حذف">×</button>
            </div>
        `;
    }

    function setupDocumentDropdowns() {
        const list = document.querySelector("[data-documents-dropdown-list]");
        const addButton = document.querySelector("[data-add-document-dropdown]");
        const count = document.querySelector("[data-documents-count]");

        if (!list || !addButton) return;

        function refresh() {
            const rows = Array.from(list.querySelectorAll("[data-document-row]"));
            rows.forEach((row, index) => {
                row.querySelectorAll("select").forEach(select => {
                    const name = select.getAttribute("name") || "";
                    if (name.startsWith("InitialDocumentTypes")) {
                        select.setAttribute("name", `InitialDocumentTypes[${index}]`);
                    }
                    if (name.startsWith("InitialDocumentRequired")) {
                        select.setAttribute("name", `InitialDocumentRequired[${index}]`);
                    }
                });
            });

            if (count) count.textContent = String(rows.length);
        }

        function addRow() {
            const index = list.querySelectorAll("[data-document-row]").length;
            list.insertAdjacentHTML("beforeend", rowTemplate(index));
            refresh();
        }

        addButton.addEventListener("click", function (event) {
            event.preventDefault();
            addRow();
        });

        list.addEventListener("click", function (event) {
            const remove = event.target.closest("[data-document-remove]");
            if (!remove) return;

            event.preventDefault();
            const row = remove.closest("[data-document-row]");
            if (row) row.remove();

            if (list.querySelectorAll("[data-document-row]").length === 0) {
                addRow();
            }

            refresh();
        });

        if (list.querySelectorAll("[data-document-row]").length === 0) {
            addRow();
        }

        refresh();
    }

    document.addEventListener("DOMContentLoaded", setupDocumentDropdowns);
})();
