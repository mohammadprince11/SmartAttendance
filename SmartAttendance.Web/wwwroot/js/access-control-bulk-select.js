(() => {
    const q = (root, selector) => Array.from(root.querySelectorAll(selector));

    function syncToggle(toggle, boxes) {
        if (!toggle || boxes.length === 0) return;
        const checked = boxes.filter(x => x.checked).length;
        toggle.checked = checked === boxes.length;
        toggle.indeterminate = checked > 0 && checked < boxes.length;
    }

    function syncRow(row) {
        const boxes = q(row, '.js-ac-permission');
        syncToggle(row.querySelector('.js-ac-row-all'), boxes);
    }

    function syncColumns(table) {
        q(table, '.js-ac-column-all').forEach(toggle => {
            const action = toggle.dataset.action;
            const boxes = q(table, `.js-ac-permission[data-action="${action}"]`);
            syncToggle(toggle, boxes);
        });
    }
    function syncTable(table) {
        q(table, '[data-ac-permission-row]').forEach(syncRow);
        syncColumns(table);
        syncToggle(table.querySelector('.js-ac-module-all'), q(table, '.js-ac-permission'));
    }

    function setBoxes(boxes, checked) {
        boxes.forEach(box => {
            box.checked = checked;
            box.indeterminate = false;
        });
    }

    document.querySelectorAll('[data-ac-permission-table]').forEach(table => {
        const moduleToggle = table.querySelector('.js-ac-module-all');
        moduleToggle?.addEventListener('change', () => {
            setBoxes(q(table, '.js-ac-permission'), moduleToggle.checked);
            syncTable(table);
        });

        q(table, '.js-ac-column-all').forEach(toggle => {
            toggle.addEventListener('change', () => {
                setBoxes(q(table, `.js-ac-permission[data-action="${toggle.dataset.action}"]`), toggle.checked);
                syncTable(table);
            });
        });
        q(table, '[data-ac-permission-row]').forEach(row => {
            const rowToggle = row.querySelector('.js-ac-row-all');
            rowToggle?.addEventListener('change', () => {
                setBoxes(q(row, '.js-ac-permission'), rowToggle.checked);
                syncTable(table);
            });
        });

        q(table, '.js-ac-permission').forEach(box => {
            box.addEventListener('change', () => syncTable(table));
        });

        syncTable(table);
    });
})();
