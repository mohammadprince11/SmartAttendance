/* NEXORA V3 Hotfix */
(function () {
    "use strict";

    function setDirections() {
        document.documentElement.setAttribute("dir", "rtl");
        document.body.setAttribute("dir", "rtl");

        document.querySelectorAll(".nexora-shell").forEach(x => x.style.direction = "ltr");
        document.querySelectorAll(".nexora-main,.nexora-sidebar,.nexora-content,.nxr-page").forEach(x => x.style.direction = "rtl");

        document.querySelectorAll(".nexora-wordmark,.nexora-tagline,.nxr-code,input[type=email],input[type=url]").forEach(x => {
            x.setAttribute("dir", "ltr");
        });
    }

    function hideProfileTabs() {
        document.querySelectorAll(".nxr-tabs,.employee360v2-tabs").forEach(x => x.remove());
    }

    document.addEventListener("DOMContentLoaded", function () {
        setDirections();
        hideProfileTabs();
    });
})();

