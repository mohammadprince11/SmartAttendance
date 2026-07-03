(function () {
    function initTabs() {
        document.querySelectorAll("[data-my-tab]").forEach(function (button) {
            button.addEventListener("click", function () {
                const tab = button.getAttribute("data-my-tab");

                document.querySelectorAll("[data-my-tab]").forEach(function (item) {
                    item.classList.toggle("active", item.getAttribute("data-my-tab") === tab);
                });

                document.querySelectorAll("[data-my-panel]").forEach(function (panel) {
                    panel.classList.toggle("active", panel.getAttribute("data-my-panel") === tab);
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
