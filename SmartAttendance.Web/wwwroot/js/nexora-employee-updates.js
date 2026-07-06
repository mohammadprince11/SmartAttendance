(() => {
    const page = document.querySelector('.nxupd-page');
    if (!page) return;

    document.querySelectorAll('.nxupd-field input, .nxupd-field select').forEach(input => {
        input.addEventListener('change', () => {
            input.closest('.nxupd-field')?.classList.add('is-changed');
        });
    });
})();


(() => {
    const modalBackdrop = document.querySelector('[data-nxupd-modal]');
    if (!modalBackdrop) return;

    const modal = modalBackdrop.querySelector('.nxupd-modal');
    const title = modalBackdrop.querySelector('[data-nxupd-modal-title]');
    const message = modalBackdrop.querySelector('[data-nxupd-modal-message]');
    const icon = modalBackdrop.querySelector('[data-nxupd-modal-icon]');
    const confirmButton = modalBackdrop.querySelector('[data-nxupd-modal-confirm]');
    const cancelButtons = modalBackdrop.querySelectorAll('[data-nxupd-modal-cancel]');

    let pendingForm = null;

    function iconForTone(tone) {
        if (tone === 'danger') return '!';
        if (tone === 'lock') return '🔒';
        return '✓';
    }

    function openModal(form) {
        pendingForm = form;

        const tone = form.dataset.nxupdConfirmTone || 'default';
        modal.dataset.tone = tone;
        title.textContent = form.dataset.nxupdConfirmTitle || 'تأكيد الإجراء';
        message.textContent = form.dataset.nxupdConfirmMessage || 'هل أنت متأكد من تنفيذ هذا الإجراء؟';
        confirmButton.textContent = form.dataset.nxupdConfirmButton || 'تأكيد';
        icon.textContent = iconForTone(tone);

        modalBackdrop.hidden = false;
        document.body.classList.add('nxupd-modal-open');

        setTimeout(() => confirmButton.focus(), 20);
    }

    function closeModal() {
        modalBackdrop.hidden = true;
        document.body.classList.remove('nxupd-modal-open');
        pendingForm = null;
    }

    document.querySelectorAll('form[data-nxupd-confirm]').forEach(form => {
        form.addEventListener('submit', event => {
            if (form.dataset.nxupdConfirmed === 'true') {
                return;
            }

            event.preventDefault();
            openModal(form);
        });
    });

    confirmButton.addEventListener('click', () => {
        if (!pendingForm) {
            closeModal();
            return;
        }

        pendingForm.dataset.nxupdConfirmed = 'true';
        const formToSubmit = pendingForm;
        closeModal();

        if (typeof formToSubmit.requestSubmit === 'function') {
            formToSubmit.requestSubmit();
        } else {
            formToSubmit.submit();
        }
    });

    cancelButtons.forEach(button => {
        button.addEventListener('click', closeModal);
    });

    modalBackdrop.addEventListener('click', event => {
        if (event.target === modalBackdrop) {
            closeModal();
        }
    });

    document.addEventListener('keydown', event => {
        if (event.key === 'Escape' && !modalBackdrop.hidden) {
            closeModal();
        }
    });
})();
