// NEXORA Violations Flow + Print V15.2
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

    const employeeSearch = document.querySelector('[data-nxv-employee-search]');
    const employeeId = document.querySelector('[data-nxv-employee-id]');
    const categorySelect = document.querySelector('[data-nxv-category-select]');
    const violationType = document.querySelector('[data-nxv-violation-type]');
    const penaltyAction = document.querySelector('[data-nxv-penalty-action]');
    const impactType = document.querySelector('[data-nxv-impact-type]');
    const impactValue = document.querySelector('[data-nxv-impact-value]');
    const impactPreview = document.querySelector('[data-nxv-impact-preview]');
    const deductionAmount = document.querySelector('[data-nxv-deduction-amount]');

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

        if (empty) {
            empty.hidden = visible !== 0;
            const cell = empty.querySelector('td');
            if (cell && rows.length > 0) {
                cell.textContent = 'لا توجد نتائج مطابقة للبحث أو الفلترة الحالية.';
            }
        }

        if (count) count.textContent = `${visible} حالة`;
    }

    function openModal() {
        if (!modal) return;
        modal.hidden = false;
        document.body.classList.add('nxv-modal-open');

        const firstField = modal.querySelector('[data-nxv-employee-search], select, input, textarea, button');
        window.setTimeout(() => firstField?.focus(), 50);
    }

    function closeModal() {
        if (!modal) return;
        modal.hidden = true;
        document.body.classList.remove('nxv-modal-open');
    }

    function updateEmployeeId() {
        if (!employeeSearch || !employeeId) return;

        const value = employeeSearch.value.trim();
        const options = Array.from(document.querySelectorAll('#nxv-employees-list option'));
        const match = options.find((option) => option.value === value);

        employeeId.value = match ? (match.dataset.id || '') : '';
    }

    function resetPenalty() {
        if (penaltyAction) penaltyAction.value = '';
        if (impactType) impactType.value = 'None';
        if (impactValue) impactValue.value = '0';
        if (impactPreview) impactPreview.textContent = 'اختر نوع المخالفة حتى يظهر أثر اللائحة.';
        if (deductionAmount && !deductionAmount.value) deductionAmount.value = '0';
    }

    function filterViolationTypes() {
        if (!categorySelect || !violationType) return;

        const categoryId = categorySelect.value;
        const options = Array.from(violationType.options);

        violationType.value = '';
        resetPenalty();

        if (!categoryId) {
            violationType.disabled = true;
            options.forEach((option, index) => {
                option.hidden = index !== 0;
                option.disabled = index !== 0;
            });
            options[0].textContent = 'اختر الفئة حتى تظهر المخالفات';
            return;
        }

        let visibleCount = 0;

        options.forEach((option, index) => {
            if (index === 0) {
                option.hidden = false;
                option.disabled = false;
                option.textContent = 'اختر نوع المخالفة';
                return;
            }

            const show = option.dataset.categoryId === categoryId;
            option.hidden = !show;
            option.disabled = !show;
            if (show) visibleCount++;
        });

        violationType.disabled = visibleCount === 0;

        if (visibleCount === 0) {
            options[0].textContent = 'لا توجد مخالفات داخل هذه الفئة';
        }
    }

    function displayImpact(type, value) {
        const numeric = Number(value || 0);
        if (type === 'Days') return `${numeric.toLocaleString('ar-IQ')} يوم حسب اللائحة`;
        if (type === 'Hours') return `${numeric.toLocaleString('ar-IQ')} ساعة حسب اللائحة`;
        if (type === 'Amount') return `${numeric.toLocaleString('ar-IQ')} مبلغ ثابت حسب اللائحة`;
        return 'لا يوجد أثر مالي في اللائحة.';
    }

    function fillPenaltyFromViolation() {
        if (!violationType) return;

        const option = violationType.options[violationType.selectedIndex];

        if (!option || !option.value) {
            resetPenalty();
            return;
        }

        const penalty = option.dataset.penalty || '';
        const type = option.dataset.impactType || 'None';
        const value = option.dataset.impactValue || '0';

        if (penaltyAction) penaltyAction.value = penalty;
        if (impactType) impactType.value = type;
        if (impactValue) impactValue.value = value;
        if (impactPreview) impactPreview.textContent = displayImpact(type, value);

        if (deductionAmount && type === 'Amount') {
            deductionAmount.value = value;
        } else if (deductionAmount && !deductionAmount.value) {
            deductionAmount.value = '0';
        }
    }

    search?.addEventListener('input', applyFilters);
    statusFilter?.addEventListener('change', applyFilters);
    sourceFilter?.addEventListener('change', applyFilters);
    employeeSearch?.addEventListener('input', updateEmployeeId);
    employeeSearch?.addEventListener('change', updateEmployeeId);
    categorySelect?.addEventListener('change', filterViolationTypes);
    violationType?.addEventListener('change', fillPenaltyFromViolation);

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

    if (modal?.dataset.nxvOpenOnLoad === 'true') {
        openModal();
    }

    updateEmployeeId();
    filterViolationTypes();
    fillPenaltyFromViolation();
    applyFilters();
})();
