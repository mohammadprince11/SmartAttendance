/* Keep the server's existing authorization and rendering; replace only employee content. */
(() => {
    'use strict';
    const page = document.querySelector('.nxupd-page');
    const form = page?.querySelector('.nxupd-employee-select');
    const content = page?.querySelector('.nxupd-content');
    const tabs = page?.querySelector('.nxupd-top-tabs');
    const picker = form?.querySelector('[data-zyep]');
    if (!picker || !content || !tabs || form.dataset.asyncSelection) return;
    form.dataset.asyncSelection = 'true';
    const id = picker.querySelector('.zyep-id');
    const code = picker.querySelector('.zyep-code');
    const name = picker.querySelector('.zyep-name');
    const notice = document.createElement('div');
    notice.className = 'nxupd-alert';
    notice.setAttribute('role', 'status');
    notice.setAttribute('aria-live', 'polite');
    notice.hidden = true;
    form.after(notice);
    let loaded = { id: id.value, code: code.value, name: name.value };
    let dirty = false;
    let version = 0;
    let controller;

    function message(text, retry) {
        notice.replaceChildren(document.createTextNode(text));
        notice.hidden = !text;
        if (retry) {
            const button = document.createElement('button');
            button.type = 'button';
            button.textContent = 'إعادة المحاولة';
            button.addEventListener('click', () => load(id.value));
            notice.append(' ', button);
        }
    }
    function suspend() {
        version++;
        controller?.abort();
        content.inert = true;
        tabs.inert = true;
        content.setAttribute('aria-busy', 'true');
    }
    function resume() {
        content.inert = false;
        tabs.inert = false;
        content.removeAttribute('aria-busy');
    }
    function restore() {
        id.value = loaded.id;
        code.value = loaded.code;
        name.value = loaded.name;
        content.hidden = false;
        resume();
        message('');
    }
    async function load(selectedId) {
        selectedId = String(selectedId || '');
        if (selectedId === loaded.id) { suspend(); restore(); return; }
        if (dirty && !window.confirm('توجد تعديلات غير محفوظة. هل تريد تركها واختيار موظف آخر؟')) {
            suspend(); restore(); return;
        }
        suspend();
        const request = version;
        controller = new AbortController();
        const signal = controller.signal;
        const timeout = setTimeout(() => controller?.signal === signal && controller.abort(), 30000);
        content.hidden = true;
        message('جارٍ تحميل بيانات الموظف…');
        const url = new URL(form.action || location.href, location.origin);
        url.search = '';
        new FormData(form).forEach((value, key) => url.searchParams.set(key, value));
        url.searchParams.set('employeeId', selectedId || '0');
        url.searchParams.set('employeeSelected', selectedId ? 'true' : 'false');
        try {
            const response = await fetch(url, { credentials: 'same-origin', cache: 'no-store', signal });
            if (!response.ok || response.redirected) throw new Error('selection unavailable');
            const html = await response.text();
            if (request !== version || id.value !== selectedId) return;
            const documentResult = new DOMParser().parseFromString(html, 'text/html');
            const freshPage = documentResult.querySelector('.nxupd-page');
            const freshContent = freshPage?.querySelector('.nxupd-content');
            const freshTabs = freshPage?.querySelector('.nxupd-top-tabs');
            const freshPicker = freshPage?.querySelector('.nxupd-employee-select [data-zyep]');
            if (!freshContent || !freshTabs || !freshPicker ||
                freshPicker.querySelector('.zyep-id')?.value !== selectedId) throw new Error('selection mismatch');
            // Never execute fetched scripts or replace the shell, picker, or confirmation dialog.
            freshContent.querySelectorAll('script').forEach(script => script.remove());
            content.replaceChildren(...freshContent.childNodes);
            tabs.replaceChildren(...freshTabs.childNodes);
            const count = page.querySelector('.nxupd-hero-status strong');
            if (count) count.textContent = freshPage.querySelector('.nxupd-hero-status strong')?.textContent || '0';
            code.value = freshPicker.querySelector('.zyep-code').value;
            name.value = freshPicker.querySelector('.zyep-name').value;
            loaded = { id: selectedId, code: code.value, name: name.value };
            dirty = false;
            content.hidden = false;
            resume();
            message('');
            page.querySelectorAll(':scope > .nxupd-alert').forEach(alert => alert.remove());
            history.replaceState(history.state, '', url);
            window.ZynoraRefreshSelectSystem?.();
            document.dispatchEvent(new CustomEvent('nxupd:employee-loaded', { detail: { employeeId: selectedId } }));
        } catch (_) {
            if (request !== version) return;
            // Keep previous employee forms hidden/inert; never leave them writable under a new name.
            message('تعذّر تحميل بيانات الموظف. أعد المحاولة أو اختر موظفاً آخر.', true);
        } finally { clearTimeout(timeout); }
    }
    page.addEventListener('input', event => {
        if (content.contains(event.target)) dirty = true;
    });
    page.addEventListener('change', event => {
        if (content.contains(event.target)) dirty = true;
    });
    page.addEventListener('submit', event => {
        if (content.inert && content.contains(event.target)) {
            event.preventDefault();
            event.stopImmediatePropagation();
        }
    }, true);
    form.addEventListener('input', event => {
        if (event.target !== code) return;
        suspend();
        message('اختر موظفاً أو أدخل رمزاً صحيحاً لعرض بياناته.');
        if (!code.value.trim()) load('');
    });
    picker.addEventListener('zyep:change', () => load(id.value));
    form.addEventListener('submit', event => { event.preventDefault(); load(id.value); });
})();
