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

    const VIOLATION_TEXT = {
        chooseCategoryFirst: "\u0627\u062e\u062a\u0631 \u0627\u0644\u0641\u0626\u0629 \u062d\u062a\u0649 \u062a\u0638\u0647\u0631 \u0627\u0644\u0645\u062e\u0627\u0644\u0641\u0627\u062a",
        chooseViolationType: "\u0627\u062e\u062a\u0631 \u0646\u0648\u0639 \u0627\u0644\u0645\u062e\u0627\u0644\u0641\u0629",
        noTypes: "\u0644\u0627 \u062a\u0648\u062c\u062f \u0645\u062e\u0627\u0644\u0641\u0627\u062a \u062f\u0627\u062e\u0644 \u0647\u0630\u0647 \u0627\u0644\u0641\u0626\u0629"
    };

    const violationOptionCache = violationType
        ? Array.from(violationType.options)
            .map((option, index) => ({
                index,
                value: option.value || '',
                text: (option.textContent || '').trim(),
                categoryId: option.dataset.categoryId || option.getAttribute('data-category-id') || '',
                category: option.dataset.category || option.getAttribute('data-category') || '',
                penalty: option.dataset.penalty || option.getAttribute('data-penalty') || '',
                impactType: option.dataset.impactType || option.getAttribute('data-impact-type') || 'None',
                impactValue: option.dataset.impactValue || option.getAttribute('data-impact-value') || '0'
            }))
            .filter((option) => option.index > 0 && option.value && option.categoryId)
        : [];

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

    function createViolationOption(item) {
        const option = document.createElement('option');
        option.value = item.value;
        option.textContent = item.text;

        option.dataset.categoryId = item.categoryId;
        option.setAttribute('data-category-id', item.categoryId);

        option.dataset.category = item.category;
        option.setAttribute('data-category', item.category);

        option.dataset.penalty = item.penalty;
        option.setAttribute('data-penalty', item.penalty);

        option.dataset.impactType = item.impactType;
        option.setAttribute('data-impact-type', item.impactType);

        option.dataset.impactValue = item.impactValue;
        option.setAttribute('data-impact-value', item.impactValue);

        return option;
    }

    function appendViolationPlaceholder(text) {
        const placeholder = document.createElement('option');
        placeholder.value = '';
        placeholder.textContent = text;
        violationType.appendChild(placeholder);
        return placeholder;
    }

    function filterViolationTypes() {
        if (!categorySelect || !violationType) return;

        const categoryId = categorySelect.value || '';
        const previousValue = violationType.value || '';

        violationType.innerHTML = '';
        resetPenalty();

        const placeholder = appendViolationPlaceholder(categoryId ? VIOLATION_TEXT.chooseViolationType : VIOLATION_TEXT.chooseCategoryFirst);

        if (!categoryId) {
            violationType.disabled = true;
            violationType.value = '';
            return;
        }

        const matched = violationOptionCache.filter((item) => String(item.categoryId) === String(categoryId));

        matched.forEach((item) => {
            violationType.appendChild(createViolationOption(item));
        });

        violationType.disabled = matched.length === 0;

        if (matched.length === 0) {
            placeholder.textContent = VIOLATION_TEXT.noTypes;
            violationType.value = '';
            return;
        }

        if (previousValue && matched.some((item) => String(item.value) === String(previousValue))) {
            violationType.value = previousValue;
            fillPenaltyFromViolation();
            return;
        }

        violationType.value = '';
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
