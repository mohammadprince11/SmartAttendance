/* ZYNORA_FIX15C_OPTIONAL_SELECT_DISABLED */
(function () {
    "use strict";
    window.ZynoraOptionalSelectV17Disabled = true;

    function cleanup() {
        document.querySelectorAll(".nxos__menu, .nxos").forEach(function (element) {
            element.remove();
        });

        document.querySelectorAll("select.nxos-native, select[data-zynora-built='1']").forEach(function (select) {
            select.classList.remove("nxos-native");
            delete select.dataset.zynoraBuilt;
            delete select.dataset.nxosId;
        });
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", cleanup);
    } else {
        cleanup();
    }

    setTimeout(cleanup, 50);
    setTimeout(cleanup, 250);
    setTimeout(cleanup, 800);
})();