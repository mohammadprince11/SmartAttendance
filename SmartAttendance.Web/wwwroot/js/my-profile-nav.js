(function () {
    function addMyProfileLink() {
        const role = window.SA_AUTH && String(window.SA_AUTH.role || "").toLowerCase();
        const employeeId = window.SA_AUTH && String(window.SA_AUTH.employeeId || "").trim();

        if (!employeeId) return;

        const selfLinks = Array.from(document.querySelectorAll("a")).filter(a => {
            const href = (a.getAttribute("href") || "").toLowerCase();
            return href.includes("/selfservices");
        });

        if (!selfLinks.length) return;

        const parent = selfLinks[0].parentElement;
        if (!parent || parent.querySelector("[data-my-profile-link]")) return;

        const link = document.createElement("a");
        link.href = "/MyProfile";
        link.setAttribute("data-my-profile-link", "true");
        link.innerHTML = '<span class="nav-icon">🪪</span><span class="nav-text">صفحتي</span>';

        parent.insertBefore(link, selfLinks[0]);
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", addMyProfileLink);
    } else {
        addMyProfileLink();
    }
})();
