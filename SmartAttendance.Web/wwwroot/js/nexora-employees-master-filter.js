(function () {
    "use strict";

    var form = document.querySelector("[data-nxr-server-filter]");

    if (!form) {
        return;
    }

    var searchInput = form.querySelector("[data-nxr-server-search]");
    var pageNumberField = form.querySelector("[data-nxr-page-number-field]");
    var selectControls = Array.from(
        form.querySelectorAll("[data-nxr-server-select]")
    );
    var searchTimer = 0;
    var isComposing = false;

    function resetPage() {
        if (pageNumberField) {
            pageNumberField.value = "1";
        }
    }

    function submitForm() {
        resetPage();

        if (typeof form.requestSubmit === "function") {
            form.requestSubmit();
            return;
        }

        form.submit();
    }

    function scheduleSearch() {
        window.clearTimeout(searchTimer);
        searchTimer = window.setTimeout(submitForm, 450);
    }

    if (searchInput) {
        searchInput.addEventListener("compositionstart", function () {
            isComposing = true;
        });

        searchInput.addEventListener("compositionend", function () {
            isComposing = false;
            scheduleSearch();
        });

        searchInput.addEventListener("input", function () {
            if (!isComposing) {
                scheduleSearch();
            }
        });

        searchInput.addEventListener("keydown", function (event) {
            if (event.key !== "Enter") {
                return;
            }

            event.preventDefault();
            window.clearTimeout(searchTimer);
            submitForm();
        });
    }

    selectControls.forEach(function (control) {
        control.addEventListener("change", function () {
            window.clearTimeout(searchTimer);
            submitForm();
        });
    });
})();
