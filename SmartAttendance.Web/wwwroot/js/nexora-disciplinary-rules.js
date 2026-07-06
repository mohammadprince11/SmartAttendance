(() => {
    function syncFinancialValue(select) {
        const form = select.closest('form');
        const valueInput = form?.querySelector('input[name="financialValue"]');
        if (!valueInput) return;
        if (select.value === 'None') {
            valueInput.value = '0';
            valueInput.setAttribute('readonly', 'readonly');
        } else {
            valueInput.removeAttribute('readonly');
            if (!valueInput.value || valueInput.value === '0') {
                valueInput.value = select.value === 'Days' ? '0.5' : '1';
            }
        }
    }
    document.querySelectorAll('select[name="financialImpactType"]').forEach(select => {
        select.addEventListener('change', () => syncFinancialValue(select));
        syncFinancialValue(select);
    });

    function syncViolationOptions(form) {
        const categorySelect = form.querySelector('[data-nxpen-category-filter]');
        const violationSelect = form.querySelector('[data-nxpen-violation-list]');
        if (!categorySelect || !violationSelect) return;
        const categoryId = categorySelect.value;
        let firstVisibleValue = '';
        Array.from(violationSelect.options).forEach(option => {
            if (!option.value) { option.hidden = false; return; }
            const match = !categoryId || option.dataset.categoryId === categoryId;
            option.hidden = !match;
            if (match && !firstVisibleValue) firstVisibleValue = option.value;
        });
        const selectedOption = violationSelect.options[violationSelect.selectedIndex];
        if (selectedOption && selectedOption.hidden) violationSelect.value = firstVisibleValue || '';
    }
    document.querySelectorAll('[data-nxpen-rule-form]').forEach(form => {
        const categorySelect = form.querySelector('[data-nxpen-category-filter]');
        categorySelect?.addEventListener('change', () => syncViolationOptions(form));
        syncViolationOptions(form);
    });

    let activeTextarea = null;
    document.querySelectorAll('.template-form textarea[name="body"]').forEach(textarea => {
        textarea.addEventListener('focus', () => activeTextarea = textarea);
    });
    document.querySelectorAll('[data-nxpen-token]').forEach(button => {
        button.addEventListener('click', () => {
            activeTextarea = activeTextarea || document.querySelector('.template-form textarea[name="body"]');
            if (!activeTextarea) return;
            const token = button.dataset.nxpenToken;
            const start = activeTextarea.selectionStart || activeTextarea.value.length;
            const end = activeTextarea.selectionEnd || activeTextarea.value.length;
            activeTextarea.value = activeTextarea.value.slice(0, start) + token + activeTextarea.value.slice(end);
            activeTextarea.focus();
            activeTextarea.selectionStart = activeTextarea.selectionEnd = start + token.length;
        });
    });
})();
