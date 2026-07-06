// NEXORA Violations Refine Modal V14.1
(() => {
    const rows = Array.from(document.querySelectorAll('[data-nxv-row]'));
    const empty = document.querySelector('[data-nxv-empty]');
    const count = document.querySelector('[data-nxv-count]');
    const search = document.querySelector('[data-nxv-search]');
    const statusFilter = document.querySelector('[data-nxv-filter="status"]');
    const sourceFilter = document.querySelector('[data-nxv-filter="source"]');
    const reset = document.querySelector('[data-nxv-reset]');
    const tabs = Array.from(document.querySelectorAll('[data-nxv-tab]'));
    const printButton = document.querySelector('[data-nxv-print]');
    const modal = document.querySelector('[data-nxv-modal]');
    const openModalButtons = Array.from(document.querySelectorAll('[data-nxv-open-modal]'));
    const closeModalButtons = Array.from(document.querySelectorAll('[data-nxv-close-modal]'));

    let activeTab = 'all';

    function normalize(value) {
        return (value || '').toString().trim().toLowerCase();
    }

    function rowMatchesTab(row) {
        const status = row.dataset.status || '';
        const action = row.dataset.action || '';

        if (activeTab === 'pending') return status === 'بانتظار الاعتماد' || action === 'بانتظار الإجراء';
        if (activeTab === 'approved') return status === 'موافق عليه';
        if (activeTab === 'completed') return status === 'تم اتخاذ الإجراء';

        return true;
    }

    function applyFilters() {
        const q = normalize(search?.value);
        const status = statusFilter?.value || '';
        const source = sourceFilter?.value || '';

        let visible = 0;

        rows.forEach((row) => {
            const haystack = normalize(row.dataset.search);
            const okSearch = !q || haystack.includes(q);
            const okStatus = !status || row.dataset.status === status;
            const okSource = !source || row.dataset.source === source;
            const okTab = rowMatchesTab(row);

            const show = okSearch && okStatus && okSource && okTab;
            row.hidden = !show;

            if (show) visible++;
        });

        if (empty) empty.hidden = visible !== 0;
        if (count) count.textContent = `${visible} حالة`;
    }

    function openModal() {
        if (!modal) return;
        modal.hidden = false;
        document.body.classList.add('nxv-modal-open');

        const firstInput = modal.querySelector('input, select, button');
        window.setTimeout(() => firstInput?.focus(), 50);
    }

    function closeModal() {
        if (!modal) return;
        modal.hidden = true;
        document.body.classList.remove('nxv-modal-open');
    }

    search?.addEventListener('input', applyFilters);
    statusFilter?.addEventListener('change', applyFilters);
    sourceFilter?.addEventListener('change', applyFilters);

    reset?.addEventListener('click', () => {
        if (search) search.value = '';
        if (statusFilter) statusFilter.value = '';
        if (sourceFilter) sourceFilter.value = '';

        activeTab = 'all';
        tabs.forEach((tab) => tab.classList.toggle('active', tab.dataset.nxvTab === 'all'));

        applyFilters();
    });

    tabs.forEach((tab) => {
        tab.addEventListener('click', () => {
            activeTab = tab.dataset.nxvTab || 'all';
            tabs.forEach((item) => item.classList.toggle('active', item === tab));
            applyFilters();
        });
    });

    printButton?.addEventListener('click', () => window.print());

    openModalButtons.forEach((button) => button.addEventListener('click', openModal));
    closeModalButtons.forEach((button) => button.addEventListener('click', closeModal));

    document.addEventListener('keydown', (event) => {
        if (event.key === 'Escape' && modal && !modal.hidden) {
            closeModal();
        }
    });

    applyFilters();
})();
