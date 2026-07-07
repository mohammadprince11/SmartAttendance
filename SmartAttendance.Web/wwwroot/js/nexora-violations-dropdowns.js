(function () {
    "use strict";

    const TEXT = {
        chooseCategoryFirst: "\u0627\u062e\u062a\u0631 \u0627\u0644\u0641\u0626\u0629 \u062d\u062a\u0649 \u062a\u0638\u0647\u0631 \u0627\u0644\u0645\u062e\u0627\u0644\u0641\u0627\u062a",
        chooseViolationType: "\u0627\u062e\u062a\u0631 \u0646\u0648\u0639 \u0627\u0644\u0645\u062e\u0627\u0644\u0641\u0629",
        noTypes: "\u0644\u0627 \u062a\u0648\u062c\u062f \u0645\u062e\u0627\u0644\u0641\u0627\u062a \u0645\u0633\u062c\u0644\u0629 \u0644\u0647\u0630\u0647 \u0627\u0644\u0641\u0626\u0629"
    };

    function ready(fn) {
        if (document.readyState === "loading") {
            document.addEventListener("DOMContentLoaded", fn);
        } else {
            fn();
        }
    }

    function triggerChange(element) {
        element.dispatchEvent(new Event("input", { bubbles: true }));
        element.dispatchEvent(new Event("change", { bubbles: true }));

        if (window.jQuery) {
            window.jQuery(element).trigger("change");
        }
    }

    ready(function () {
        const categorySelect = document.querySelector("[data-nxv-category-select]");
        const violationSelect = document.querySelector("[data-nxv-violation-type]");

        if (!categorySelect || !violationSelect) {
            console.warn("NEXORA Violations: dropdown elements not found.");
            return;
        }

        const originalOptions = Array.from(violationSelect.options)
            .map(function (opt) {
                return {
                    value: opt.value || "",
                    text: (opt.textContent || "").trim(),
                    categoryId: opt.dataset.categoryId || opt.getAttribute("data-category-id") || ""
                };
            })
            .filter(function (item) {
                return item.value && item.categoryId;
            });

        function rebuildViolationTypes() {
            const selectedCategoryId = categorySelect.value || "";

            violationSelect.innerHTML = "";

            const placeholder = document.createElement("option");
            placeholder.value = "";
            placeholder.selected = true;
            placeholder.textContent = selectedCategoryId
                ? TEXT.chooseViolationType
                : TEXT.chooseCategoryFirst;

            violationSelect.appendChild(placeholder);

            if (!selectedCategoryId) {
                violationSelect.disabled = true;
                violationSelect.value = "";
                triggerChange(violationSelect);
                return;
            }

            const matched = originalOptions.filter(function (item) {
                return String(item.categoryId) === String(selectedCategoryId);
            });

            matched.forEach(function (item) {
                const opt = document.createElement("option");
                opt.value = item.value;
                opt.textContent = item.text;
                opt.dataset.categoryId = item.categoryId;
                opt.setAttribute("data-category-id", item.categoryId);
                violationSelect.appendChild(opt);
            });

            violationSelect.disabled = matched.length === 0;
            violationSelect.value = "";

            if (matched.length === 0) {
                placeholder.textContent = TEXT.noTypes;
            }

            triggerChange(violationSelect);

            console.log("NEXORA violation types updated", {
                categoryId: selectedCategoryId,
                count: matched.length
            });
        }

        categorySelect.addEventListener("change", rebuildViolationTypes);
        categorySelect.addEventListener("input", rebuildViolationTypes);

        rebuildViolationTypes();
    });
})();