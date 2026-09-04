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


// ===== عدّاد ساعات العمل الحيّ (من أول بصمة اليوم) =====
(() => {
    const box = document.querySelector('[data-workclock]');
    if (!box) return;

    let times = [];
    try { times = JSON.parse(box.getAttribute('data-workclock') || '[]').map(s => new Date(s)); } catch { times = []; }
    times = times.filter(d => !isNaN(d)).sort((a, b) => a - b);
    if (times.length === 0) { box.hidden = true; return; }

    const timeEl = box.querySelector('[data-wc-time]');
    const stateEl = box.querySelector('[data-wc-state]');
    const sinceEl = box.querySelector('[data-wc-since]');
    const pad = n => String(n).padStart(2, '0');
    const working = times.length % 2 === 1; // فردي = ما زال داخلاً (آخر بصمة دخول)

    if (sinceEl) sinceEl.textContent = `— من أول بصمة ${pad(times[0].getHours())}:${pad(times[0].getMinutes())}`;
    box.hidden = false;
    box.classList.toggle('working', working);
    box.classList.toggle('done', !working);

    function totalMs() {
        let ms = 0;
        for (let i = 0; i + 1 < times.length; i += 2) ms += times[i + 1] - times[i];
        if (working) ms += Date.now() - times[times.length - 1]; // الزوج المفتوح يحسب حتى الآن
        return Math.max(0, ms);
    }

    function tick() {
        const s = Math.floor(totalMs() / 1000);
        const h = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60), sec = s % 60;
        if (timeEl) timeEl.textContent = `${pad(h)}:${pad(m)}:${pad(sec)}`;
    }

    if (stateEl) stateEl.textContent = working ? '🟢 يعمل الآن' : '⏹ انتهى الدوام';
    tick();
    if (working) setInterval(tick, 1000);
})();

