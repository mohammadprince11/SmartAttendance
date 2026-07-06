// NEXORA Employee Row Click V12.1
(() => {
    function isInteractiveElement(target) {
        return Boolean(target.closest('a, button, input, select, textarea, label, summary, details, [data-no-row-click]'));
    }

    function openProfile(row) {
        const url = row.getAttribute('data-profile-url');
        if (!url) return;
        window.location.href = url;
    }

    document.addEventListener('click', (event) => {
        const row = event.target.closest('[data-employee-row]');
        if (!row) return;
        if (isInteractiveElement(event.target)) return;
        openProfile(row);
    });

    document.addEventListener('keydown', (event) => {
        const row = event.target.closest('[data-employee-row]');
        if (!row) return;
        if (event.key !== 'Enter' && event.key !== ' ') return;
        if (isInteractiveElement(event.target) && event.target !== row) return;

        event.preventDefault();
        openProfile(row);
    });
})();
