/* UI-only navigation: never submits or changes settings. All panels remain available without JS. */
(() => {
    'use strict';
    const navigation = document.querySelector('[data-pai-tabs]');
    if (!navigation) return;
    const buttons = Array.from(navigation.querySelectorAll('[data-pai-tab]'));
    const panels = buttons.map(button => document.getElementById(button.dataset.paiTab));
    if (panels.some(panel => !panel)) return;
    const storageKey = 'zynora.people-ai.section.' + navigation.dataset.company;
    let stored;
    try { stored = sessionStorage.getItem(storageKey); } catch (_) { /* Optional UI preference. */ }
    function activate(id, focus = false) {
        if (!buttons.some(button => button.dataset.paiTab === id)) id = buttons[0].dataset.paiTab;
        buttons.forEach((button, index) => {
            const selected = button.dataset.paiTab === id;
            button.setAttribute('aria-selected', String(selected));
            button.tabIndex = selected ? 0 : -1;
            panels[index].hidden = !selected;
            panels[index].setAttribute('role', 'tabpanel');
            if (selected && focus) button.focus();
        });
        try { sessionStorage.setItem(storageKey, id); } catch (_) { /* No effect on saving. */ }
    }
    buttons.forEach((button, index) => {
        button.addEventListener('click', () => activate(button.dataset.paiTab));
        button.addEventListener('keydown', event => {
            let next;
            const rtl = getComputedStyle(navigation).direction === 'rtl';
            if (event.key === 'Home') next = 0;
            if (event.key === 'End') next = buttons.length - 1;
            if (event.key === 'ArrowRight') next = (index + (rtl ? -1 : 1) + buttons.length) % buttons.length;
            if (event.key === 'ArrowLeft') next = (index + (rtl ? 1 : -1) + buttons.length) % buttons.length;
            if (next === undefined) return;
            event.preventDefault();
            activate(buttons[next].dataset.paiTab, true);
        });
    });
    panels.forEach(panel => {
        const search = panel.querySelector('[data-pai-search]');
        if (!search) return;
        panel.querySelector('[data-pai-search-label]').hidden = false;
        const records = Array.from(panel.querySelectorAll('[data-pai-record]'));
        const pagination = document.createElement('nav'); pagination.className = 'pai-pagination';
        pagination.setAttribute('aria-label', 'صفحات القائمة');
        const previous = document.createElement('button'); previous.type = 'button'; previous.className = 'zy-btn'; previous.textContent = 'السابق';
        const next = document.createElement('button'); next.type = 'button'; next.className = 'zy-btn'; next.textContent = 'التالي';
        const status = document.createElement('span'); status.setAttribute('aria-live', 'polite');
        pagination.append(previous, status, next);
        const list = panel.querySelector('.pai-doc-type-list, .pai-policy-list, .pai-field-policy-list');
        if (!list) return;
        list.after(pagination);
        let page = 0;
        const pageSize = 8;
        function render() {
            const query = search.value.trim().toLocaleLowerCase();
            const matching = records.filter(record => (record.dataset.paiSearchText || '').toLocaleLowerCase().includes(query));
            page = Math.max(0, Math.min(page, Math.ceil(matching.length / pageSize) - 1));
            const visible = new Set(matching.slice(page * pageSize, (page + 1) * pageSize));
            records.forEach(record => { record.hidden = !visible.has(record); });
            panel.querySelector('[data-pai-no-results]').hidden = !query || matching.length > 0;
            pagination.hidden = matching.length <= pageSize;
            previous.disabled = page === 0;
            next.disabled = (page + 1) * pageSize >= matching.length;
            status.textContent = 'صفحة ' + (page + 1) + ' من ' + Math.max(1, Math.ceil(matching.length / pageSize)) + ' · ' + matching.length + ' سجل';
        }
        previous.addEventListener('click', () => { page--; render(); });
        next.addEventListener('click', () => { page++; render(); });
        search.addEventListener('input', () => { page = 0; render(); });
        render();
    });
    activate(stored);
    navigation.hidden = false;
})();
