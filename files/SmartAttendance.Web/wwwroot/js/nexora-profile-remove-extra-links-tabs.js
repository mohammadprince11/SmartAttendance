
(function () {
    "use strict";

    function ready(fn) {
        if (document.readyState === "loading") {
            document.addEventListener("DOMContentLoaded", fn);
        } else {
            fn();
        }
    }

    ready(function () {
        document.querySelectorAll(".nxr-profile-pro-nav").forEach(function (el) {
            el.remove();
        });

        var removeTexts = [
            "فتح سجلات الحضور",
            "فتح الطلبات",
            "+ رفع مستند"
        ];

        document.querySelectorAll("a, button").forEach(function (el) {
            var text = (el.textContent || "").replace(/\s+/g, " ").trim();
            if (removeTexts.indexOf(text) >= 0) {
                el.classList.add("nxr-profile-remove-link");
                el.remove();
            }
        });
    });
})();
