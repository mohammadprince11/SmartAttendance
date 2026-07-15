(() => {
    "use strict";

    const root = document.querySelector(
        "[data-identity-management]");

    if (!root) {
        return;
    }

    const roleSelect = root.querySelector(
        "[data-role-select]");
    const templateLabel = root.querySelector(
        "[data-system-template]");

    const syncRoleTemplate = () => {
        if (!roleSelect || !templateLabel) {
            return;
        }

        const selected =
            roleSelect.options[roleSelect.selectedIndex];

        templateLabel.textContent =
            selected?.dataset.template || "Viewer";
    };

    roleSelect?.addEventListener(
        "change",
        syncRoleTemplate);

    syncRoleTemplate();

    const passwordInput = root.querySelector(
        "[data-password-input]");
    const passwordConfirm = root.querySelector(
        "[data-password-confirm]");
    const togglePassword = root.querySelector(
        "[data-toggle-password]");

    togglePassword?.addEventListener("click", () => {
        if (!passwordInput) {
            return;
        }

        const show = passwordInput.type === "password";

        passwordInput.type = show
            ? "text"
            : "password";

        if (passwordConfirm) {
            passwordConfirm.type = show
                ? "text"
                : "password";
        }

        togglePassword.textContent = show
            ? "إخفاء"
            : "عرض";
    });

    root.querySelectorAll("[data-toggle-identity]")
        .forEach((form) => {
            form.addEventListener("submit", (event) => {
                const message =
                    form.dataset.message ||
                    "هل تريد متابعة العملية؟";

                if (!window.confirm(message)) {
                    event.preventDefault();
                }
            });
        });
})();
