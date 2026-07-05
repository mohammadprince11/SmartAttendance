
(function () {
    "use strict";

    var keys = [
        "NEXORA.Unified.Announcements",
        "NEXORA.Unified.EmployeeFeedback",
        "NEXORA.Announcements.Shared",
        "NEXORA.WallPosts.Shared",
        "NEXORA.Announcements.Published",
        "NEXORA.EmployeePortal.Announcements",
        "NEXORA.EmployeeComplaints.Shared",
        "NEXORA.Engagement.EmployeeComplaints.Flow",
        "NEXORA.EmployeePortal.Complaints",
        "NEXORA.EmployeePortal.Feedback",
        "NEXORA.EmployeePortal.FeedbackItems",
        "NEXORA.EmployeePortal.Suggestions",
        "NEXORA.EmployeePortal.ComplaintItems",
        "NEXORA.Announcements.DeletedRows",
        "NEXORA.ExplosionCleanup.Done"
    ];

    try {
        keys.forEach(function (key) {
            localStorage.removeItem(key);
        });
        localStorage.setItem("NEXORA.ExplosionCleanup.Done", "1");
    } catch (_) { }

    function cleanupInjectedNodes() {
        var selectors = [
            "[data-unified-announcements]",
            "[data-unified-wall-posts]",
            "[data-unified-admin-feedback]",
            "[data-unified-employee-wall]",
            "[data-unified-feedback-list]",
            "[data-nx-ann-sync-panel]",
            "[data-nx-wall-sync-panel]",
            "[data-nx-ann-wall-list]",
            ".nx-ann-sync-panel",
            ".nx-ann-sync-table-card",
            ".nx-ann-published-card",
            ".nx-ann-wall-list",
            ".nx-unified-toast",
            ".nx-ann-sync-toast",
            ".nx-ann-publish-toast",
            ".nx-complaint-sync-toast"
        ];

        selectors.forEach(function (selector) {
            document.querySelectorAll(selector).forEach(function (el) {
                el.remove();
            });
        });
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", cleanupInjectedNodes);
    } else {
        cleanupInjectedNodes();
    }

    setTimeout(cleanupInjectedNodes, 500);
    setTimeout(cleanupInjectedNodes, 1500);
})();
