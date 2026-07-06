// NEXORA HR Settings V16.3
(() => {
    function applySearch() {
        const search = document.querySelector('[data-nxhs-table-search]');
        const table = document.querySelector('[data-nxhs-table]');
        if (!search || !table) return;

        const rows = Array.from(table.querySelectorAll('tbody tr'));
        search.addEventListener('input', () => {
            const q = (search.value || '').trim().toLowerCase();
            rows.forEach((row) => {
                row.hidden = q && !row.textContent.toLowerCase().includes(q);
            });
        });
    }

    function applyExtensionToggle() {
        const checkbox = document.querySelector('[data-nxhs-toggle-target]');
        const days = document.querySelector('[data-nxhs-extension-days]');
        if (!checkbox || !days) return;

        const sync = () => {
            days.disabled = !checkbox.checked;
            if (!checkbox.checked && !days.value) {
                days.value = '0';
            }
        };

        checkbox.addEventListener('change', sync);
        sync();
    }

    function applyDeleteModal() {
        const modal = document.querySelector('[data-nxhs-delete-modal]');
        if (!modal) return;

        const nameTarget = modal.querySelector('[data-nxhs-delete-name]');
        const idTarget = modal.querySelector('[data-nxhs-delete-id]');
        const openButtons = Array.from(document.querySelectorAll('[data-nxhs-delete-open]'));
        const closeButtons = Array.from(document.querySelectorAll('[data-nxhs-delete-close]'));

        function openModal(button) {
            const id = button.dataset.id || '';
            const name = button.dataset.name || '';

            if (idTarget) idTarget.value = id;
            if (nameTarget) nameTarget.textContent = name;

            modal.hidden = false;
            document.body.classList.add('nxhs-modal-open');
            window.setTimeout(() => modal.querySelector('.nxhs-modal-cancel')?.focus(), 50);
        }

        function closeModal() {
            modal.hidden = true;
            document.body.classList.remove('nxhs-modal-open');
        }

        openButtons.forEach((button) => {
            button.addEventListener('click', () => openModal(button));
        });

        closeButtons.forEach((button) => {
            button.addEventListener('click', closeModal);
        });

        document.addEventListener('keydown', (event) => {
            if (event.key === 'Escape' && !modal.hidden) {
                closeModal();
            }
        });
    }

    applySearch();
    applyExtensionToggle();
    applyDeleteModal();
})();
