
(function () {
    "use strict";

    function ready(fn) {
        if (document.readyState === "loading") {
            document.addEventListener("DOMContentLoaded", fn);
        } else {
            fn();
        }
    }

    function normalize(value) {
        return (value || "").toString().replace(/\s+/g, " ").trim().toLowerCase();
    }

    ready(function () {
        var root = document.querySelector("[data-nxra-page]");
        if (!root) return;

        var textarea = root.querySelector("[data-target-employees]");
        var targetSummary = root.querySelector("[data-target-summary]");
        var previewAudience = root.querySelector("[data-preview-audience]");

        if (!textarea || textarea.dataset.multiselectReady === "1") return;
        textarea.dataset.multiselectReady = "1";

        var employeeSource = [
            { code: "10158", name: "محمد عقيل توفيق", dept: "Human Resources" },
            { code: "1001", name: "احمد خضر محمود", dept: "Operations" },
            { code: "11230", name: "محمد علي زيدان", dept: "HR Operations" },
            { code: "1002", name: "زهراء حسين", dept: "Finance" },
            { code: "1003", name: "فريد عبدالرحمن", dept: "Finance" },
            { code: "1004", name: "رند خضير محسن", dept: "Finance" },
            { code: "1005", name: "محمد الكردي", dept: "HR Consultant" },
            { code: "1006", name: "مصطفى اياد", dept: "Marketing" },
            { code: "1007", name: "عيسى مازن حسام", dept: "Business Development" },
            { code: "1008", name: "ماهر عايد العابد", dept: "Rixos" }
        ];

        var wrapper = document.createElement("div");
        wrapper.className = "nxra-employee-select";
        wrapper.setAttribute("data-nxra-employee-select", "1");

        wrapper.innerHTML =
            '<button type="button" class="nxra-employee-select-toggle" data-emp-toggle>' +
                '<span class="label">اختر الموظفين من القائمة</span>' +
                '<span class="count" data-emp-count>0</span>' +
            '</button>' +
            '<div class="nxra-employee-dropdown" data-emp-dropdown>' +
                '<input type="search" class="nxra-employee-search" placeholder="ابحث باسم الموظف أو الكود..." data-emp-search />' +
                '<div class="nxra-employee-list" data-emp-list></div>' +
                '<div class="nxra-employee-select-actions">' +
                    '<button type="button" data-emp-clear>مسح الاختيار</button>' +
                    '<button type="button" class="primary" data-emp-done>تم</button>' +
                '</div>' +
            '</div>' +
            '<div class="nxra-employee-selected" data-emp-selected></div>';

        textarea.style.display = "none";
        textarea.parentElement.insertBefore(wrapper, textarea);

        var toggle = wrapper.querySelector("[data-emp-toggle]");
        var list = wrapper.querySelector("[data-emp-list]");
        var search = wrapper.querySelector("[data-emp-search]");
        var count = wrapper.querySelector("[data-emp-count]");
        var selectedBox = wrapper.querySelector("[data-emp-selected]");
        var clearButton = wrapper.querySelector("[data-emp-clear]");
        var doneButton = wrapper.querySelector("[data-emp-done]");

        function getSelectedValues() {
            return Array.from(list.querySelectorAll("input[type='checkbox']:checked")).map(function (input) {
                return input.value;
            });
        }

        function syncTextareaAndPreview() {
            var selected = getSelectedValues();
            textarea.value = selected.join(", ");

            if (count) count.textContent = selected.length.toString();

            var label = wrapper.querySelector(".nxra-employee-select-toggle .label");
            if (label) {
                label.textContent = selected.length === 0
                    ? "اختر الموظفين من القائمة"
                    : "تم اختيار " + selected.length + " موظف";
            }

            selectedBox.innerHTML = "";
            selected.forEach(function (value) {
                var chip = document.createElement("span");
                chip.className = "nxra-employee-chip";
                chip.innerHTML = '<span>' + value + '</span><button type="button" aria-label="حذف">×</button>';

                chip.querySelector("button").addEventListener("click", function () {
                    var input = list.querySelector("input[value='" + CSS.escape(value) + "']");
                    if (input) input.checked = false;
                    syncTextareaAndPreview();
                });

                selectedBox.appendChild(chip);
            });

            var text = selected.length
                ? "توجيه الإعلان: موظفون محددون: " + selected.join("، ")
                : "توجيه الإعلان: موظفون محددون: لم يتم اختيار موظفين";

            if (targetSummary) targetSummary.textContent = text;
            if (previewAudience) previewAudience.textContent = selected.length
                ? "موظفون محددون: " + selected.join("، ")
                : "موظفون محددون: لم يتم اختيار موظفين";

            textarea.dispatchEvent(new Event("input", { bubbles: true }));
            textarea.dispatchEvent(new Event("change", { bubbles: true }));
        }

        function renderList(query) {
            var q = normalize(query);
            list.innerHTML = "";

            employeeSource
                .filter(function (emp) {
                    var text = normalize(emp.code + " " + emp.name + " " + emp.dept);
                    return !q || text.indexOf(q) >= 0;
                })
                .forEach(function (emp) {
                    var value = emp.name + " (" + emp.code + ")";
                    var item = document.createElement("label");
                    item.className = "nxra-employee-item";
                    item.innerHTML =
                        '<input type="checkbox" value="' + value.replace(/"/g, "&quot;") + '" />' +
                        '<span>' +
                            '<strong>' + emp.name + '</strong>' +
                            '<span>' + emp.code + ' - ' + emp.dept + '</span>' +
                        '</span>';

                    var checkbox = item.querySelector("input");
                    checkbox.addEventListener("change", syncTextareaAndPreview);

                    list.appendChild(item);
                });
        }

        toggle.addEventListener("click", function () {
            wrapper.classList.toggle("open");
            if (wrapper.classList.contains("open")) {
                setTimeout(function () {
                    if (search) search.focus();
                }, 30);
            }
        });

        doneButton.addEventListener("click", function () {
            wrapper.classList.remove("open");
        });

        clearButton.addEventListener("click", function () {
            list.querySelectorAll("input[type='checkbox']").forEach(function (input) {
                input.checked = false;
            });
            syncTextareaAndPreview();
        });

        search.addEventListener("input", function () {
            var previous = getSelectedValues();
            renderList(search.value);

            previous.forEach(function (value) {
                var input = list.querySelector("input[value='" + CSS.escape(value) + "']");
                if (input) input.checked = true;
            });

            syncTextareaAndPreview();
        });

        document.addEventListener("click", function (event) {
            if (!wrapper.contains(event.target)) {
                wrapper.classList.remove("open");
            }
        });

        renderList("");
        syncTextareaAndPreview();
    });
})();
