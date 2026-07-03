(function () {
    const storageKey = "SmartAttendance.SidebarModules.v1";

    function readState() {
        try { return JSON.parse(localStorage.getItem(storageKey) || "{}"); }
        catch { return {}; }
    }

    function saveState(state) {
        localStorage.setItem(storageKey, JSON.stringify(state));
    }

    function initModuleState() {
        const state = readState();

        document.querySelectorAll(".nav-module").forEach(function (module, index) {
            const key = module.querySelector("[data-i18n]")?.getAttribute("data-i18n") || `module-${index}`;
            module.dataset.moduleKey = key;

            if (state[key] === false) {
                module.removeAttribute("open");
            }

            module.addEventListener("toggle", function () {
                const current = readState();
                current[key] = module.open;
                saveState(current);
            });
        });
    }

    function preventDisabledLinks() {
        document.querySelectorAll(".nav-disabled-link").forEach(function (link) {
            link.addEventListener("click", function (event) {
                event.preventDefault();
            });
        });
    }

    function init() {
        initModuleState();
        preventDisabledLinks();
    }

    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", init);
    else init();
})();
