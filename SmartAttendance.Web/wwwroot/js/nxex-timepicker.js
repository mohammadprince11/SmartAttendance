/* منتقي وقت مخصّص للموبايل: عمودا ساعة/دقيقة قابلان للتمرير.
 * يكتب "HH:mm" في input[data-tp-input] داخل نفس .nxex-field ويحدّث النص. */
(() => {
  const sheet = document.getElementById('nxex-tp-sheet');
  const backdrop = document.getElementById('nxex-tp-backdrop');
  if (!sheet || !backdrop) return;

  const hoursCol = sheet.querySelector('[data-tp-hours]');
  const minsCol = sheet.querySelector('[data-tp-mins]');
  const preview = sheet.querySelector('[data-tp-preview]');
  const applyBtn = sheet.querySelector('[data-tp-apply]');
  const pad = (n) => String(n).padStart(2, '0');

  let field = null;    // .nxex-field الحاوية
  let hour = null, minute = null;

  // بناء الأعمدة مرة واحدة
  for (let h = 0; h < 24; h++) {
    const b = document.createElement('button');
    b.type = 'button'; b.textContent = pad(h); b.dataset.h = h;
    b.addEventListener('click', () => { hour = h; sync(); });
    hoursCol.appendChild(b);
  }
  for (let m = 0; m < 60; m++) {
    const b = document.createElement('button');
    b.type = 'button'; b.textContent = pad(m); b.dataset.m = m;
    b.addEventListener('click', () => { minute = m; sync(); });
    minsCol.appendChild(b);
  }

  function sync() {
    hoursCol.querySelectorAll('button').forEach((b) => b.classList.toggle('sel', +b.dataset.h === hour));
    minsCol.querySelectorAll('button').forEach((b) => b.classList.toggle('sel', +b.dataset.m === minute));
    preview.textContent = (hour != null && minute != null) ? `${pad(hour)}:${pad(minute)}` : '--:--';
    applyBtn.disabled = !(hour != null && minute != null);
  }

  function scrollToSel() {
    hoursCol.querySelector('button.sel')?.scrollIntoView({ block: 'center' });
    minsCol.querySelector('button.sel')?.scrollIntoView({ block: 'center' });
  }

  const lock = (on) => { document.documentElement.classList.toggle('nxex-scroll-lock', on); document.body.classList.toggle('nxex-scroll-lock', on); };

  function open(trigger) {
    field = trigger.closest('.nxex-field');
    const cur = field?.querySelector('[data-tp-input]')?.value || '';
    const parts = cur.split(':');
    hour = parts.length === 2 ? +parts[0] : null;
    minute = parts.length === 2 ? +parts[1] : null;
    sync();
    sheet.hidden = false; backdrop.hidden = false; lock(true);
    setTimeout(() => { sheet.classList.add('open'); backdrop.classList.add('open'); scrollToSel(); }, 10);
  }
  function close() {
    sheet.classList.remove('open'); backdrop.classList.remove('open'); lock(false);
    setTimeout(() => { sheet.hidden = true; backdrop.hidden = true; }, 240);
  }

  document.querySelectorAll('[data-open-timepicker]').forEach((el) => el.addEventListener('click', () => open(el)));
  backdrop.addEventListener('click', close);
  sheet.querySelector('[data-tp-cancel]').addEventListener('click', close);
  applyBtn.addEventListener('click', () => {
    if (applyBtn.disabled || !field) return;
    const val = `${pad(hour)}:${pad(minute)}`;
    const input = field.querySelector('[data-tp-input]');
    if (input) { input.value = val; input.dispatchEvent(new Event('change', { bubbles: true })); }
    const disp = field.querySelector('[data-time-display]');
    if (disp) disp.textContent = val;
    close();
  });
})();
