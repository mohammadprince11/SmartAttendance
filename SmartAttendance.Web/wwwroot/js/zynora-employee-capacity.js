(() => {
    'use strict';
    const trigger = document.querySelector('[data-employee-add]');
    const modal = document.getElementById('employee-capacity-modal');
    const backdrop = document.getElementById('employee-capacity-backdrop');
    if (!trigger || !modal || !backdrop) return;
    const closeButton = modal.querySelector('[data-capacity-close]');
    let busy = false;
    let previousOverflow = '';
    function close() {
        modal.classList.remove('zy-open');
        backdrop.classList.remove('zy-open');
        modal.setAttribute('inert', '');
        backdrop.setAttribute('inert', '');
        document.documentElement.style.overflow = previousOverflow;
        trigger.focus();
    }
    function open(message) {
        document.getElementById('employee-capacity-message').textContent = message;
        previousOverflow = document.documentElement.style.overflow;
        modal.removeAttribute('inert');
        backdrop.removeAttribute('inert');
        modal.classList.add('zy-open');
        backdrop.classList.add('zy-open');
        document.documentElement.style.overflow = 'hidden';
        closeButton.focus();
    }
    trigger.addEventListener('click', async event => {
        event.preventDefault();
        if (busy) return;
        busy = true;
        trigger.setAttribute('aria-busy', 'true');
        try {
            const response = await fetch(trigger.dataset.capacityUrl, { cache: 'no-store', credentials: 'same-origin' });
            if (!response.ok) throw new Error('Capacity check failed');
            const capacity = await response.json();
            if (capacity.canAdd === true) window.location.assign(trigger.href);
            else open(capacity.message || 'لا يمكن إضافة موظف جديد. راجع إدارة المنصة.');
        } catch {
            open('تعذر التحقق من حد الموظفين. أعد المحاولة بعد قليل.');
        } finally {
            busy = false;
            trigger.removeAttribute('aria-busy');
        }
    });
    closeButton.addEventListener('click', close);
    backdrop.addEventListener('click', close);
    modal.addEventListener('keydown', event => {
        if (event.key === 'Escape') close();
        if (event.key === 'Tab') { event.preventDefault(); closeButton.focus(); }
    });
})();
