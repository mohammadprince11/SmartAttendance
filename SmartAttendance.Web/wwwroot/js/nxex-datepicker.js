/* منتقي تاريخ مخصّص للموبايل: «يوم واحد» أو «أيام متعددة» بتظليل مدى.
 * يكتب القيم في input[data-dp-from]/[data-dp-to] (يطلق change) ويحدّث نص الحقل والعدّاد. */
(() => {
  const sheet = document.getElementById('nxex-dp-sheet');
  const backdrop = document.getElementById('nxex-dp-backdrop');
  if (!sheet || !backdrop) return;

  const grid = sheet.querySelector('[data-dp-grid]');
  const monthLabel = sheet.querySelector('[data-dp-month]');
  const fromLabel = sheet.querySelector('[data-dp-from-label]');
  const toLabel = sheet.querySelector('[data-dp-to-label]');
  const singleLabel = sheet.querySelector('[data-dp-single-label]');
  const boxFrom = sheet.querySelector('[data-dp-fieldbox="from"]');
  const boxTo = sheet.querySelector('[data-dp-fieldbox="to"]');
  const boxSingle = sheet.querySelector('[data-dp-fieldbox="single"]');
  const applyBtn = sheet.querySelector('[data-dp-apply]');
  const modeBtns = [...sheet.querySelectorAll('[data-dp-mode]')];

  const AR_MONTHS = ['يناير', 'فبراير', 'مارس', 'أبريل', 'مايو', 'يونيو', 'يوليو', 'أغسطس', 'سبتمبر', 'أكتوبر', 'نوفمبر', 'ديسمبر'];
  const EN_MONTHS = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];

  const pad = (n) => String(n).padStart(2, '0');
  const iso = (d) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
  const parse = (s) => { const p = (s || '').split('-'); return p.length === 3 ? new Date(+p[0], +p[1] - 1, +p[2]) : null; };
  const same = (a, b) => a && b && a.getTime() === b.getTime();
  const dayNum = (d) => Math.floor(d.getTime() / 86400000);

  let mode = 'single';
  let from = null, to = null;
  let view = new Date(); view.setDate(1);
  let ctx = document; // نموذج التبويب الذي فتح المنتقي (لكتابة الحقول الصحيحة)

  function daysBetween(a, b) { return Math.floor((dayNum(b) - dayNum(a))) + 1; }

  function render() {
    // العنوان
    monthLabel.textContent = `${EN_MONTHS[view.getMonth()]} ${view.getFullYear()}`;
    grid.innerHTML = '';
    const y = view.getFullYear(), m = view.getMonth();
    const firstWeekday = new Date(y, m, 1).getDay();       // 0=أحد
    const daysInMonth = new Date(y, m + 1, 0).getDate();
    const prevDays = new Date(y, m, 0).getDate();

    const cells = [];
    for (let i = firstWeekday - 1; i >= 0; i--) cells.push({ n: prevDays - i, out: true, date: new Date(y, m - 1, prevDays - i) });
    for (let d = 1; d <= daysInMonth; d++) cells.push({ n: d, out: false, date: new Date(y, m, d) });
    while (cells.length % 7 !== 0) { const d = cells.length - (firstWeekday + daysInMonth) + 1; cells.push({ n: d, out: true, date: new Date(y, m + 1, d) }); }

    const today = new Date(); today.setHours(0, 0, 0, 0);
    cells.forEach((c) => {
      const b = document.createElement('button');
      b.type = 'button';
      b.textContent = c.n;
      if (c.out) b.classList.add('dp-out');
      if (same(c.date, today)) b.classList.add('dp-today');
      // تظليل التحديد
      if (from && to) {
        if (same(c.date, from)) b.classList.add('dp-sel', 'dp-start');
        if (same(c.date, to)) b.classList.add('dp-sel', 'dp-end');
        if (dayNum(c.date) > dayNum(from) && dayNum(c.date) < dayNum(to)) b.classList.add('dp-in-range');
      } else if (from && same(c.date, from)) {
        b.classList.add('dp-sel');
      }
      if (!c.out) b.addEventListener('click', () => pick(c.date));
      grid.appendChild(b);
    });
    syncFields();
  }

  function pick(date) {
    if (mode === 'single') { from = date; to = date; }
    else {
      if (!from || (from && to)) { from = date; to = null; }
      else if (dayNum(date) >= dayNum(from)) { to = date; }
      else { from = date; to = null; }
    }
    render();
  }

  function syncFields() {
    const arr = (d) => d ? `${d.getDate()} ${AR_MONTHS[d.getMonth()]} ${d.getFullYear()}` : '—';
    if (singleLabel) singleLabel.textContent = arr(from);
    if (fromLabel) fromLabel.textContent = arr(from);
    if (toLabel) toLabel.textContent = arr(to);
    let n = 0;
    if (mode === 'single') n = from ? 1 : 0;
    else n = (from && to) ? daysBetween(from, to) : 0;
    const ok = mode === 'single' ? !!from : !!(from && to);
    applyBtn.disabled = !ok;
    applyBtn.textContent = ok ? `تطبيق (${n} ${n === 1 ? 'يوم' : 'أيام'})` : 'تطبيق';
  }

  function setMode(next) {
    mode = next;
    modeBtns.forEach((b) => b.classList.toggle('active', b.getAttribute('data-dp-mode') === next));
    if (next === 'single') {
      boxFrom.hidden = true; boxTo.hidden = true; boxSingle.hidden = false;
      if (from) to = from;
    } else {
      boxFrom.hidden = false; boxTo.hidden = false; boxSingle.hidden = true;
    }
    render();
  }

  // فتح/إغلاق
  const lock = (on) => { document.documentElement.classList.toggle('nxex-scroll-lock', on); document.body.classList.toggle('nxex-scroll-lock', on); };
  function open(trigger) {
    ctx = (trigger && trigger.closest('form')) || document;
    // ابدأ من القيم الحالية إن وُجدت
    const fEl = ctx.querySelector('[data-dp-from]'), tEl = ctx.querySelector('[data-dp-to]');
    from = parse(fEl && fEl.value); to = parse(tEl && tEl.value);
    if (from) { view = new Date(from.getFullYear(), from.getMonth(), 1); }
    setMode(from && to && !same(from, to) ? 'range' : 'single');
    sheet.hidden = false; backdrop.hidden = false; lock(true);
    setTimeout(() => { sheet.classList.add('open'); backdrop.classList.add('open'); }, 10);
  }
  function close() {
    sheet.classList.remove('open'); backdrop.classList.remove('open'); lock(false);
    setTimeout(() => { sheet.hidden = true; backdrop.hidden = true; }, 240);
  }

  document.querySelectorAll('[data-open-datepicker]').forEach((el) => el.addEventListener('click', () => open(el)));
  backdrop.addEventListener('click', close);
  sheet.querySelector('[data-dp-cancel]').addEventListener('click', close);
  sheet.querySelector('[data-dp-prev]').addEventListener('click', () => { view = new Date(view.getFullYear(), view.getMonth() - 1, 1); render(); });
  sheet.querySelector('[data-dp-next]').addEventListener('click', () => { view = new Date(view.getFullYear(), view.getMonth() + 1, 1); render(); });
  modeBtns.forEach((b) => b.addEventListener('click', () => setMode(b.getAttribute('data-dp-mode'))));

  applyBtn.addEventListener('click', () => {
    if (applyBtn.disabled) return;
    const f = from, t = mode === 'single' ? from : to;
    const fEl = ctx.querySelector('[data-dp-from]'), tEl = ctx.querySelector('[data-dp-to]');
    if (fEl) { fEl.value = iso(f); fEl.dispatchEvent(new Event('change', { bubbles: true })); }
    if (tEl) { tEl.value = iso(t); tEl.dispatchEvent(new Event('change', { bubbles: true })); }
    const disp = ctx.querySelector('[data-date-display]');
    if (disp) {
      const arr = (d) => `${d.getDate()} ${AR_MONTHS[d.getMonth()]} ${d.getFullYear()}`;
      disp.textContent = same(f, t) ? arr(f) : `${arr(f)} ← ${arr(t)}`;
      disp.classList.remove('is-placeholder');
    }
    // حدّث عدّاد الأيام مباشرة في النموذج نفسه (إن وُجد).
    const daysEl = ctx.querySelector('[data-leave-days]');
    if (daysEl) daysEl.textContent = String(daysBetween(f, t));
    close();
  });
})();
