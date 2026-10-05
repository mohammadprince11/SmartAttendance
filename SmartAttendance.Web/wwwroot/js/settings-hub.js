(function () {
    "use strict";

    var tabs = Array.prototype.slice.call(
        document.querySelectorAll("[data-settings-tab]")
    );
    var panels = Array.prototype.slice.call(
        document.querySelectorAll("[data-settings-panel]")
    );

    if (!tabs.length || !panels.length) return;

    function activate(panelId, focusTab, updateUrl) {
        var selectedTab = tabs.find(function (tab) {
            return tab.dataset.settingsTab === panelId;
        });
        var selectedPanel = panels.find(function (panel) {
            return panel.id === panelId;
        });

        if (!selectedTab || !selectedPanel) return;

        tabs.forEach(function (tab) {
            var active = tab === selectedTab;
            tab.setAttribute("aria-selected", active ? "true" : "false");
            tab.tabIndex = active ? 0 : -1;
        });

        panels.forEach(function (panel) {
            panel.hidden = panel !== selectedPanel;
        });

        if (updateUrl) {
            var url = new URL(window.location.href);
            url.searchParams.set("tab", panelId);
            url.hash = "";
            history.replaceState(null, "", url);
        }

        if (focusTab) selectedTab.focus();
    }

    tabs.forEach(function (tab, index) {
        tab.addEventListener("click", function () {
            activate(tab.dataset.settingsTab, false, true);
        });

        tab.addEventListener("keydown", function (event) {
            if (!["ArrowRight", "ArrowLeft", "Home", "End"].includes(event.key)) return;

            event.preventDefault();
            var nextIndex = index;

            if (event.key === "Home") nextIndex = 0;
            else if (event.key === "End") nextIndex = tabs.length - 1;
            else if (event.key === "ArrowRight") nextIndex = (index - 1 + tabs.length) % tabs.length;
            else nextIndex = (index + 1) % tabs.length;

            activate(tabs[nextIndex].dataset.settingsTab, true, true);
        });
    });

    var requested = new URLSearchParams(window.location.search).get("tab");
    var legacyHash = window.location.hash.replace(/^#/, "");
    activate(requested || legacyHash || tabs[0].dataset.settingsTab, false, false);
})();
