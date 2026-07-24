/* نسيان البصمة (صفحة مستقلة): معاينة حيّة بالأسبقية الزمنية — دخول/خروج تلقائي +
 * ساعات العمل بعد الإضافة. يقرأ بصمات اليوم عبر ?handler=DayPunches على نفس الصفحة. */
(() => {
    const form = document.querySelector('[data-mp-form]');
    if (!form) return;

    const dateEl = form.querySelector('[data-mp-date]');
    const timeEl = form.querySelector('[data-mp-time]');
    const box = form.querySelector('[data-mp-preview]');
    const timeline = form.querySelector('[data-mp-timeline]');
    const note = form.querySelector('[data-mp-note]');
    const hoursEl = form.querySelector('[data-mp-hours]');
    if (!dateEl || !timeEl || !box || !timeline) return;

    let existing = [];   // [{at:'HH:mm', type:'In'|'Out'}] من الخادم
    let lastDate = '';

    const label = t => (t === 'Out' ? 'خروج' : 'دخول');
    const toMin = s => { const p = s.split(':'); return (+p[0]) * 60 + (+p[1]); };
    const fmtDur = m => {
        const h = Math.floor(m / 60), mm = m % 60;
        if (h && mm) return `${h} ساعة و${mm} دقيقة`;
        if (h) return `${h} ساعة`;
        return `${mm} دقيقة`;
    };

    async function loadDay(date) {
        if (!date) { existing = []; render(); return; }
        try {
            const res = await fetch(`${location.pathname}?handler=DayPunches&date=${encodeURIComponent(date)}`, {
                headers: { 'X-Requested-With': 'XMLHttpRequest' }
            });
            const data = await res.json();
            existing = Array.isArray(data.punches) ? data.punches : [];
        } catch { existing = []; }
        render();
    }

    // يدمج الوقت المُدخَل مع الموجود، يرتّب زمنياً، ويصنّف بالتناوب (الأول دخول).
    function render() {
        const newTime = timeEl.value;
        const items = existing.map(p => ({ at: p.at, isNew: false }));
        if (newTime) items.push({ at: newTime, isNew: true });

        if (items.length === 0) { box.hidden = true; return; }
        box.hidden = false;

        items.sort((a, b) => a.at.localeCompare(b.at));
        items.forEach((it, i) => { it.type = (i % 2 === 0) ? 'In' : 'Out'; });

        timeline.innerHTML = items.map(it => `
            <span class="nxex-mp-punch ${it.type === 'Out' ? 'out' : 'in'} ${it.isNew ? 'new' : ''}">
                <b>${it.at}</b><small>${label(it.type)}${it.isNew ? ' • جديد' : ''}</small>
            </span>`).join('<span class="nxex-mp-arrow">→</span>');

        // ساعات العمل: أزواج (دخول→خروج) متتالية؛ فردية أخيرة = خروج ناقص.
        if (hoursEl) {
            let totalMin = 0, incomplete = false;
            for (let i = 0; i < items.length; i += 2) {
                if (i + 1 < items.length) totalMin += toMin(items[i + 1].at) - toMin(items[i].at);
                else incomplete = true;
            }
            if (totalMin > 0) {
                hoursEl.hidden = false;
                hoursEl.innerHTML = `⏱ ساعات العمل بعد الإضافة: <b>${fmtDur(totalMin)}</b>` +
                    (incomplete ? ' <span class="nxex-mp-warn">(بصمة خروج ناقصة)</span>' : '');
            } else if (incomplete) {
                hoursEl.hidden = false;
                hoursEl.innerHTML = '⏱ <span class="nxex-mp-warn">بصمة خروج ناقصة — لن تُحتسب ساعات عمل.</span>';
            } else {
                hoursEl.hidden = true;
            }
        }

        const mine = items.find(it => it.isNew);
        if (note) {
            note.textContent = mine
                ? `بصمتك الساعة ${mine.at} ستُسجَّل «${label(mine.type)}» تلقائياً حسب ترتيبها بين بصمات اليوم.`
                : 'أدخل وقت البصمة لمعاينة تصنيفها.';
        }
    }

    dateEl.addEventListener('change', () => {
        if (dateEl.value !== lastDate) { lastDate = dateEl.value; loadDay(dateEl.value); }
    });
    timeEl.addEventListener('change', render);
})();
