(() => {
    function bindPhotoDrop(drop) {
        if (!drop || drop.dataset.photoReady === "1") return;
        drop.dataset.photoReady = "1";

        const input = drop.querySelector('input[type="file"]');
        const preview = drop.querySelector('[data-employee-photo-preview]');
        const form = drop.closest('[data-employee-photo-form]');
        const autoSubmit = form && form.hasAttribute('data-auto-submit-photo');

        if (!input || !preview) return;

        function showFile(file) {
            if (!file || !file.type || !file.type.startsWith("image/")) return false;
            const url = URL.createObjectURL(file);
            preview.onload = () => URL.revokeObjectURL(url);
            preview.src = url;
            drop.classList.add("has-image");
            return true;
        }

        function maybeSubmit(file) {
            if (!autoSubmit || !form || !file) return;
            window.setTimeout(() => form.submit(), 120);
        }

        drop.addEventListener("click", (event) => {
            if (event.target !== input) input.click();
        });

        input.addEventListener("change", () => {
            const file = input.files && input.files.length > 0 ? input.files[0] : null;
            if (showFile(file)) {
                maybeSubmit(file);
            }
        });

        drop.addEventListener("dragover", (event) => {
            event.preventDefault();
            drop.classList.add("is-dragover");
        });

        drop.addEventListener("dragleave", () => {
            drop.classList.remove("is-dragover");
        });

        drop.addEventListener("drop", (event) => {
            event.preventDefault();
            drop.classList.remove("is-dragover");

            const file = event.dataTransfer && event.dataTransfer.files.length > 0
                ? event.dataTransfer.files[0]
                : null;

            if (!file || !file.type.startsWith("image/")) return;

            const transfer = new DataTransfer();
            transfer.items.add(file);
            input.files = transfer.files;

            if (showFile(file)) {
                maybeSubmit(file);
            }
        });
    }

    document.querySelectorAll("[data-employee-photo-drop]").forEach(bindPhotoDrop);
})();
