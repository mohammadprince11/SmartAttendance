(() => {
    'use strict';
    const create = document.getElementById('zy-ann-create');
    const frame = create?.querySelector('[data-ann-frame]');
    document.querySelector('[data-ann-create]')?.addEventListener('click', e => {
        e.preventDefault(); frame.src = e.currentTarget.href; create.showModal();
    });
    document.querySelectorAll('[data-ann-open]').forEach(row => {
        const open = () => document.getElementById(row.dataset.annOpen)?.showModal();
        row.addEventListener('click', open);
        row.addEventListener('keydown', event => {
            if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); open(); }
        });
    });
    document.querySelectorAll('.zy-ann-dialog').forEach(dialog => {
        dialog.querySelector('[data-ann-close]')?.addEventListener('click', () => dialog.close());
        dialog.addEventListener('close', () => {
            if (dialog === create) frame.src = 'about:blank';
            const editor = dialog.querySelector('.zy-ann-edit');
            if (editor) editor.hidden = true;
        });
        dialog.querySelector('[data-ann-edit]')?.addEventListener('click', () => {
            const editor = dialog.querySelector('.zy-ann-edit');
            editor.hidden = false; editor.querySelector('input:not([type=hidden])')?.focus();
        });
        dialog.querySelector('[data-ann-cancel-edit]')?.addEventListener('click', () => {
            const editor = dialog.querySelector('.zy-ann-edit'); editor.reset(); editor.hidden = true;
        });
    });
    window.addEventListener('message', event => {
        if (event.origin === window.location.origin && event.source === frame?.contentWindow &&
            event.data?.type === 'zynora-announcement-saved' && create?.open) window.location.reload();
    });
})();
