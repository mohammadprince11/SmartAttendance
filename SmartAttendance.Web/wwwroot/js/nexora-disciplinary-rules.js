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

// NEXORA Drag Snap V8 Hotfix
(() => {
    const snapStep = 1;

    function clamp(value, min, max) {
        return Math.min(max, Math.max(min, value));
    }

    function snap(value, event) {
        if (event && event.shiftKey) return value;
        return Math.round(value / snapStep) * snapStep;
    }

    function cssPercent(value) {
        return `${Number(value).toFixed(2).replace(/\.00$/, "")}%`;
    }

    function findLayerForm(layerId) {
        return document.querySelector(`[data-layer-form][data-layer-id="${layerId}"]`);
    }

    function openLayerEditor(layerId) {
        document.querySelectorAll(".nxpen-edit-item.nxpen-layer-open").forEach(x => x.classList.remove("nxpen-layer-open"));
        const details = document.querySelector(`[data-layer-details="${layerId}"]`);
        if (details) {
            details.open = true;
            details.classList.add("nxpen-layer-open");
            details.scrollIntoView({ behavior: "smooth", block: "nearest" });
        }
    }

    function updateInputs(layerId, x, y) {
        const form = findLayerForm(layerId);
        if (!form) return;

        const xInput = form.querySelector('input[name="xPercent"]');
        const yInput = form.querySelector('input[name="yPercent"]');

        if (xInput) xInput.value = Number(x).toFixed(2).replace(/\.00$/, "");
        if (yInput) yInput.value = Number(y).toFixed(2).replace(/\.00$/, "");
    }

    function selectLayer(layer) {
        document.querySelectorAll(".nxpen-text-block.nxpen-layer-selected").forEach(x => x.classList.remove("nxpen-layer-selected"));
        layer.classList.add("nxpen-layer-selected");
        const layerId = layer.getAttribute("data-layer-id");
        if (layerId) openLayerEditor(layerId);
    }

    document.querySelectorAll(".nxpen-text-block[data-layer-id]").forEach(layer => {
        layer.addEventListener("click", event => {
            event.stopPropagation();
            selectLayer(layer);
        });

        layer.addEventListener("mousedown", event => {
            if (event.button !== 0) return;

            const parent = layer.closest(".nxpen-a4-section");
            if (!parent) return;

            event.preventDefault();
            selectLayer(layer);

            const layerId = layer.getAttribute("data-layer-id");
            const parentRect = parent.getBoundingClientRect();
            const layerRect = layer.getBoundingClientRect();

            const startMouseX = event.clientX;
            const startMouseY = event.clientY;

            const startRightPx = parentRect.right - layerRect.right;
            const startTopPx = layerRect.top - parentRect.top;

            layer.classList.add("nxpen-layer-dragging");

            function onMove(moveEvent) {
                const dx = moveEvent.clientX - startMouseX;
                const dy = moveEvent.clientY - startMouseY;

                let rightPx = startRightPx - dx;
                let topPx = startTopPx + dy;

                let rightPercent = (rightPx / parentRect.width) * 100;
                let topPercent = (topPx / parentRect.height) * 100;

                rightPercent = clamp(snap(rightPercent, moveEvent), 0, 100);
                topPercent = clamp(snap(topPercent, moveEvent), 0, 100);

                layer.style.right = cssPercent(rightPercent);
                layer.style.top = cssPercent(topPercent);

                updateInputs(layerId, rightPercent, topPercent);
            }

            function onUp() {
                layer.classList.remove("nxpen-layer-dragging");
                document.removeEventListener("mousemove", onMove);
                document.removeEventListener("mouseup", onUp);
            }

            document.addEventListener("mousemove", onMove);
            document.addEventListener("mouseup", onUp);
        });
    });
})();

// NEXORA Drag Pointer Hotfix
(() => {
    const snapStep = 1;

    function clamp(value, min, max) {
        return Math.min(max, Math.max(min, value));
    }

    function snap(value, event) {
        if (event && event.shiftKey) return value;
        return Math.round(value / snapStep) * snapStep;
    }

    function cssPercent(value) {
        return `${Number(value).toFixed(2).replace(/\.00$/, "")}%`;
    }

    function findLayerForm(layerId) {
        return document.querySelector(`[data-layer-form][data-layer-id="${layerId}"]`);
    }

    function openLayerEditor(layerId) {
        document.querySelectorAll(".nxpen-edit-item.nxpen-layer-open").forEach(x => x.classList.remove("nxpen-layer-open"));
        const details = document.querySelector(`[data-layer-details="${layerId}"]`);
        if (details) {
            details.open = true;
            details.classList.add("nxpen-layer-open");
        }
    }

    function updateInputs(layerId, x, y) {
        const form = findLayerForm(layerId);
        if (!form) return;

        const xInput = form.querySelector('input[name="xPercent"]');
        const yInput = form.querySelector('input[name="yPercent"]');

        if (xInput) xInput.value = Number(x).toFixed(2).replace(/\.00$/, "");
        if (yInput) yInput.value = Number(y).toFixed(2).replace(/\.00$/, "");
    }

    function selectLayer(layer) {
        document.querySelectorAll(".nxpen-text-block.nxpen-layer-selected").forEach(x => x.classList.remove("nxpen-layer-selected"));
        layer.classList.add("nxpen-layer-selected");
        const layerId = layer.getAttribute("data-layer-id");
        if (layerId) openLayerEditor(layerId);
    }

    function attachDrag() {
        document.querySelectorAll(".nxpen-text-block[data-layer-id]").forEach(layer => {
            if (layer.dataset.dragReady === "1") return;
            layer.dataset.dragReady = "1";

            layer.addEventListener("pointerdown", event => {
                if (event.button !== 0) return;

                const parent = layer.closest(".nxpen-a4-section");
                if (!parent) return;

                event.preventDefault();
                selectLayer(layer);
                layer.setPointerCapture?.(event.pointerId);

                const layerId = layer.getAttribute("data-layer-id");
                const parentRect = parent.getBoundingClientRect();
                const layerRect = layer.getBoundingClientRect();

                const startMouseX = event.clientX;
                const startMouseY = event.clientY;

                const startRightPx = parentRect.right - layerRect.right;
                const startTopPx = layerRect.top - parentRect.top;

                layer.classList.add("nxpen-layer-dragging");

                function onMove(moveEvent) {
                    const dx = moveEvent.clientX - startMouseX;
                    const dy = moveEvent.clientY - startMouseY;

                    let rightPx = startRightPx - dx;
                    let topPx = startTopPx + dy;

                    let rightPercent = (rightPx / parentRect.width) * 100;
                    let topPercent = (topPx / parentRect.height) * 100;

                    rightPercent = clamp(snap(rightPercent, moveEvent), 0, 100);
                    topPercent = clamp(snap(topPercent, moveEvent), 0, 100);

                    layer.style.right = cssPercent(rightPercent);
                    layer.style.top = cssPercent(topPercent);

                    updateInputs(layerId, rightPercent, topPercent);
                }

                function onUp() {
                    layer.classList.remove("nxpen-layer-dragging");
                    document.removeEventListener("pointermove", onMove);
                    document.removeEventListener("pointerup", onUp);
                    document.removeEventListener("pointercancel", onUp);
                }

                document.addEventListener("pointermove", onMove);
                document.addEventListener("pointerup", onUp);
                document.addEventListener("pointercancel", onUp);
            });
        });
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", attachDrag);
    } else {
        attachDrag();
    }
})();
