(function () {
    function initTabs() {
        document.querySelectorAll("[data-employee-file-tab]").forEach(function (button) {
            button.addEventListener("click", function () {
                const tab = button.getAttribute("data-employee-file-tab");

                document.querySelectorAll("[data-employee-file-tab]").forEach(function (item) {
                    item.classList.toggle("active", item.getAttribute("data-employee-file-tab") === tab);
                });

                document.querySelectorAll("[data-employee-file-panel]").forEach(function (panel) {
                    panel.classList.toggle("active", panel.getAttribute("data-employee-file-panel") === tab);
                });
            });
        });
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initTabs);
    } else {
        initTabs();
    }
})();
