// Re-entrant designer setup. Single pointer/animation-frame drag, no DOM-wide cleanup timers.
(() => {
    'use strict';
    if (window.ZynoraDisciplinary) { window.ZynoraDisciplinary.init(document); return; }
    const previewObservers = new WeakMap();
    function preview(root) {
        root.querySelectorAll('[data-disciplinary-paper-viewport]').forEach(viewport => {
            if (previewObservers.has(viewport)) return;
            const resize = () => {
                const width = viewport.clientWidth;
                if (width > 0) viewport.style.setProperty('--nxpen-preview-scale', String(Math.min(1, width / 480)));
            };
            const observer = new ResizeObserver(resize);
            previewObservers.set(viewport, observer); observer.observe(viewport); resize();
        });
    }
    function dispose(scope) {
        scope.querySelectorAll('[data-disciplinary-paper-viewport]').forEach(viewport => {
            previewObservers.get(viewport)?.disconnect(); previewObservers.delete(viewport);
        });
    }
    function show(root, key) {
        root.querySelectorAll('[data-designer-panel]').forEach(node => { node.hidden = node.dataset.designerPanel !== key; });
        root.querySelectorAll('[data-designer-tab]').forEach(node => node.setAttribute('aria-selected', String(node.dataset.designerTab === key)));
    }
    function financial(select) {
        const input = select.closest('form')?.querySelector('[name="financialValue"]');
        if (!input) return;
        input.readOnly = select.value === 'None';
        if (input.readOnly) input.value = '0';
        else if (!input.value || input.value === '0') input.value = select.value === 'Days' ? '0.5' : '1';
    }
    function violations(form) {
        const category = form.querySelector('[data-nxpen-category-filter]'), select = form.querySelector('[data-nxpen-violation-list]');
        if (!category || !select) return;
        Array.from(select.options).forEach(option => { option.hidden = !!option.value && !!category.value && option.dataset.categoryId !== category.value; });
        if (select.selectedOptions[0]?.hidden) select.value = Array.from(select.options).find(option => option.value && !option.hidden)?.value || '';
    }
    function init(scope) {
        preview(scope);
        scope.querySelectorAll('select[name="financialImpactType"]').forEach(financial);
        scope.querySelectorAll('[data-nxpen-rule-form]').forEach(violations);
        scope.querySelectorAll('[data-disciplinary-designer]').forEach(root => {
            if (root.dataset.designerReady) return;
            root.dataset.designerReady = 'true'; show(root, 'background');
        });
    }
    document.addEventListener('change', event => {
        if (!event.target.closest('[data-disciplinary-workspace]')) return;
        if (event.target.matches('[name="financialImpactType"]')) financial(event.target);
        if (event.target.matches('[data-nxpen-category-filter]')) violations(event.target.closest('form'));
    });
    document.addEventListener('click', event => {
        const button = event.target.closest('[data-designer-tab]');
        if (button) show(button.closest('[data-disciplinary-designer]'), button.dataset.designerTab);
    });
    document.addEventListener('pointerdown', event => {
        const layer = event.target.closest('.nxpen-text-block[data-layer-id]'), root = layer?.closest('[data-disciplinary-designer]');
        if (!root || event.button !== 0 || !event.isPrimary) return;
        const parent = layer.closest('.nxpen-a4-section')?.getBoundingClientRect(), rect = layer.getBoundingClientRect();
        if (!parent?.width || !parent.height) return;
        event.preventDefault(); show(root, 'text');
        root.querySelectorAll('.nxpen-layer-selected').forEach(node => node.classList.remove('nxpen-layer-selected'));
        layer.classList.add('nxpen-layer-selected', 'nxpen-layer-dragging');
        const id = layer.dataset.layerId, editor = root.querySelector('[data-layer-details="' + id + '"]');
        if (editor) editor.open = true;
        const form = root.querySelector('[data-layer-form][data-layer-id="' + id + '"]');
        const xInput = form?.querySelector('[name="xPercent"]'), yInput = form?.querySelector('[name="yPercent"]');
        const startX = event.clientX, startY = event.clientY, startRight = parent.right - rect.right, startTop = rect.top - parent.top;
        let latest, frame = 0;
        layer.setPointerCapture?.(event.pointerId);
        function apply() {
            frame = 0; if (!latest) return;
            let x = (startRight - latest.clientX + startX) / parent.width * 100;
            let y = (startTop + latest.clientY - startY) / parent.height * 100;
            if (!latest.shiftKey) { x = Math.round(x); y = Math.round(y); }
            x = Math.min(100, Math.max(0, x)); y = Math.min(100, Math.max(0, y));
            layer.style.right = x.toFixed(2) + '%'; layer.style.top = y.toFixed(2) + '%';
            if (xInput) xInput.value = x.toFixed(2); if (yInput) yInput.value = y.toFixed(2);
        }
        function move(next) {
            if (next.pointerId !== event.pointerId) return;
            latest = next; if (!frame) frame = requestAnimationFrame(apply);
        }
        function stop(next) {
            if (next.pointerId !== event.pointerId) return;
            if (frame) cancelAnimationFrame(frame); apply();
            layer.classList.remove('nxpen-layer-dragging');
            layer.removeEventListener('pointermove', move);
            ['pointerup', 'pointercancel', 'lostpointercapture'].forEach(name => layer.removeEventListener(name, stop));
        }
        layer.addEventListener('pointermove', move);
        ['pointerup', 'pointercancel', 'lostpointercapture'].forEach(name => layer.addEventListener(name, stop));
    });
    window.ZynoraDisciplinary = {init, dispose};
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', () => init(document));
    else init(document);
})();
