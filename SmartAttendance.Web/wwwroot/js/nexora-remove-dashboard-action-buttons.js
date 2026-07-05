/* NEXORA Remove Dashboard Action Buttons
   Removes page action buttons:
   - تقديم الطلبات
   - الموافقات
   - التقارير
   Does not touch sidebar navigation.
*/
(function () {
    "use strict";

    const labels = ["تقديم الطلبات", "الموافقات", "التقارير"];

    function cleanText(value) {
        return (value || "").replace(/\s+/g, " ").trim();
    }

    function removeButtons() {
        document.querySelectorAll("a, button").forEach(el => {
            if (el.closest(".nexora-sidebar") || el.closest(".nexora-nav")) return;

            const text = cleanText(el.textContent);
            if (labels.includes(text)) {
                el.remove();
            }
        });
    }

    document.addEventListener("DOMContentLoaded", removeButtons);
    setTimeout(removeButtons, 150);
    setTimeout(removeButtons, 600);
})();
