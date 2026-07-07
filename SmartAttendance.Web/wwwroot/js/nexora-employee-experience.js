(() => {
    const page = document.querySelector('[data-nxex-page]');
    if (!page) return;

    const tabs = [...document.querySelectorAll('[data-nxex-tab]')];
    const panes = [...document.querySelectorAll('[data-nxex-pane]')];

    const allowed = new Set(panes.map(p => p.dataset.nxexPane));
    const url = new URL(window.location.href);
    const initial = url.searchParams.get('tab') || page.dataset.activeTab || 'home';

    function activate(tab, updateUrl = true) {
        if (!allowed.has(tab)) tab = 'home';

        tabs.forEach(button => button.classList.toggle('active', button.dataset.nxexTab === tab));
        panes.forEach(pane => pane.classList.toggle('active', pane.dataset.nxexPane === tab));

        document.querySelectorAll('[data-nxex-jump]').forEach(button => {
            button.onclick = () => activate(button.dataset.nxexJump);
        });

        if (updateUrl) {
            const next = new URL(window.location.href);
            next.searchParams.set('tab', tab);
            window.history.replaceState({}, '', next);
        }
    }

    tabs.forEach(button => {
        button.addEventListener('click', () => activate(button.dataset.nxexTab));
    });

    activate(initial, false);
})();


(() => {
    const grid = document.querySelector('[data-request-type-grid]');
    const form = document.querySelector('[data-request-form]');
    if (!grid || !form) return;

    const input = document.querySelector('[data-request-type-input]');
    const title = document.querySelector('[data-request-type-title]');
    const help = document.querySelector('[data-request-type-help]');

    const meta = {
        "إجازة": {
            title: "طلب إجازة",
            help: "اختر تاريخ البداية والنهاية وسبب الإجازة."
        },
        "نسيان بصمة": {
            title: "طلب نسيان بصمة",
            help: "حدد يوم البصمة المفقودة واكتب هل هي دخول أو خروج."
        },
        "خروج شخصي": {
            title: "طلب خروج شخصي",
            help: "حدد تاريخ الخروج واكتب سبب الخروج الشخصي."
        },
        "خروج عمل": {
            title: "طلب خروج عمل",
            help: "حدد التاريخ واكتب جهة العمل أو المهمة المطلوبة."
        },
        "أوفر تايم": {
            title: "طلب أوفر تايم",
            help: "حدد تاريخ العمل الإضافي واكتب سبب الحاجة للأوفر تايم."
        }
    };

    function setType(type) {
        const data = meta[type] || meta["إجازة"];
        if (input) input.value = type;
        if (title) title.textContent = data.title;
        if (help) help.textContent = data.help;

        grid.querySelectorAll('[data-request-type]').forEach(button => {
            button.classList.toggle('active', button.dataset.requestType === type);
        });
    }

    grid.querySelectorAll('[data-request-type]').forEach(button => {
        button.addEventListener('click', () => setType(button.dataset.requestType));
    });

    setType(input ? input.value || "إجازة" : "إجازة");
})();

