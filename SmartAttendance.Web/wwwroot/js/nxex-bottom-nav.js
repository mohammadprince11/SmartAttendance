/* الشريط السفلي للموبايل: درج «المزيد» + منتقي «طلب جديد» + سلوك الأقسام كتطبيق.
 * تفعيل التبويب نفسه يتولّاه nexora-employee-experience.js عبر [data-nxex-tab]. */
(() => {
  const pageEl = document.querySelector('[data-nxex-page]');
  const moreSet = new Set(['profile', 'pulse', 'feedback', 'performance']);
  const moreBtn = document.getElementById('nxex-more-btn');

  // قفل تمرير الخلفية حين تكون شاشة/درج مفتوحاً (منع تسرّب التمرير للخلف).
  const lockScroll = (on) => {
    document.documentElement.classList.toggle('nxex-scroll-lock', on);
    document.body.classList.toggle('nxex-scroll-lock', on);
  };

  // ===== مُنشئ درج سفلي عام (يعمل حتى بلا رسم إطارات: setTimeout لا rAF) =====
  function makeSheet(sheetEl, backdropEl) {
    if (!sheetEl || !backdropEl) return null;
    function open() {
      sheetEl.hidden = false;
      backdropEl.hidden = false;
      lockScroll(true);
      setTimeout(() => { sheetEl.classList.add('open'); backdropEl.classList.add('open'); }, 10);
    }
    function close() {
      sheetEl.classList.remove('open');
      backdropEl.classList.remove('open');
      lockScroll(false);
      setTimeout(() => { sheetEl.hidden = true; backdropEl.hidden = true; }, 220);
    }
    backdropEl.addEventListener('click', close);
    return { open, close, isOpen: () => !sheetEl.hidden };
  }

  const moreSheet = makeSheet(
    document.getElementById('nxex-more-sheet'),
    document.getElementById('nxex-more-backdrop'));
  const reqSheet = makeSheet(
    document.getElementById('nxex-req-sheet'),
    document.getElementById('nxex-req-backdrop'));

  // ===== درج «المزيد» =====
  if (moreBtn && moreSheet) {
    moreBtn.addEventListener('click', () => moreSheet.isOpen() ? moreSheet.close() : moreSheet.open());
  }

  // ===== منتقي «طلب جديد» =====
  document.querySelectorAll('[data-open-reqsheet]').forEach((el) => {
    el.addEventListener('click', () => reqSheet && reqSheet.open());
  });

  // اختيار نوع الطلب: بدّل لتبويب الطلبات، حدّد النوع بالاستوديو، ومرّر للنموذج.
  document.querySelectorAll('#nxex-req-sheet [data-req-type]').forEach((item) => {
    item.addEventListener('click', () => {
      const type = item.getAttribute('data-req-type');
      if (reqSheet) reqSheet.close();
      const reqTab = document.querySelector('.nxex-bnav-item[data-nxex-tab="requests"]');
      if (reqTab) reqTab.click(); // يفعّل تبويب الطلبات عبر الآلية القائمة
      setTimeout(() => {
        const typeBtn = document.querySelector('.nxex-request-type-grid [data-request-type="' + type + '"]');
        if (typeBtn) typeBtn.click(); // setType بالاستوديو
        const form = document.querySelector('.nxex-request-create');
        if (form) form.scrollIntoView({ behavior: 'smooth', block: 'start' });
      }, 70);
    });
  });

  // ===== شاشة طلب الإجازة المنبثقة =====
  const leaveModal = document.getElementById('nxex-leave-modal');
  const leaveBackdrop = document.getElementById('nxex-leave-backdrop');
  if (leaveModal && leaveBackdrop) {
    const modalTabs = leaveModal.querySelector('.nxex-modal-tabs');
    const modalTitle = leaveModal.querySelector('.nxex-modal-head h2');
    const allPanels = () => [...leaveModal.querySelectorAll('[data-leave-panel]')];

    // وضع العرض: catKey ⟹ فئة مستقلة (بلا تبويبات، شاشة خاصة)؛ null ⟹ عادي بتبويبات.
    function showLeaveMode(catKey) {
      const panels = allPanels();
      if (catKey) {
        if (modalTabs) modalTabs.hidden = true;
        panels.forEach((p) => { p.hidden = p.getAttribute('data-leave-panel') !== catKey; });
        const panel = panels.find((p) => p.getAttribute('data-leave-panel') === catKey);
        if (modalTitle && panel) modalTitle.textContent = panel.getAttribute('data-cat-name') || 'طلب';
        if (panel) { const c = panel.querySelector('.nxex-leave-type input[type=radio]:checked'); if (c) applyTypeControls(c); }
      } else {
        if (modalTabs) modalTabs.hidden = false;
        if (modalTitle) modalTitle.textContent = 'طلب إجازة';
        const tabs = modalTabs ? [...modalTabs.querySelectorAll('[data-leave-tab]')] : [];
        tabs.forEach((b, i) => b.classList.toggle('active', i === 0));
        const firstKey = tabs[0] ? tabs[0].getAttribute('data-leave-tab') : null;
        panels.forEach((p) => { p.hidden = p.getAttribute('data-leave-panel') !== firstKey; });
        const fp = panels.find((p) => p.getAttribute('data-leave-panel') === firstKey);
        if (fp) { const c = fp.querySelector('.nxex-leave-type input[type=radio]:checked'); if (c) applyTypeControls(c); }
      }
    }

    const openLeave = (catKey) => {
      showLeaveMode(catKey || null);
      leaveModal.hidden = false; leaveBackdrop.hidden = false;
      lockScroll(true);
      setTimeout(() => { leaveModal.classList.add('open'); leaveBackdrop.classList.add('open'); }, 10);
    };
    const closeLeave = () => {
      leaveModal.classList.remove('open'); leaveBackdrop.classList.remove('open');
      lockScroll(false);
      setTimeout(() => { leaveModal.hidden = true; leaveBackdrop.hidden = true; }, 240);
    };
    document.querySelectorAll('[data-open-leave-modal]').forEach((el) => {
      el.addEventListener('click', () => { if (reqSheet) reqSheet.close(); openLeave(null); });
    });
    document.querySelectorAll('[data-open-cat]').forEach((el) => {
      el.addEventListener('click', () => { if (reqSheet) reqSheet.close(); openLeave(el.getAttribute('data-open-cat')); });
    });
    leaveModal.querySelectorAll('[data-close-leave-modal]').forEach((el) => el.addEventListener('click', closeLeave));
    leaveBackdrop.addEventListener('click', closeLeave);

    // تبويبات الشاشة (الإجازات/العرضية/المغادرات)
    const tabBtns = [...leaveModal.querySelectorAll('[data-leave-tab]')];
    const panels = [...leaveModal.querySelectorAll('[data-leave-panel]')];
    tabBtns.forEach((btn) => btn.addEventListener('click', () => {
      const t = btn.getAttribute('data-leave-tab');
      tabBtns.forEach((b) => b.classList.toggle('active', b === btn));
      panels.forEach((p) => { p.hidden = p.getAttribute('data-leave-panel') !== t; });
    }));

    // تمييز نوع الإجازة المختار
    // تطبيق ضوابط النوع المختار: تمييزه + إظهار حقول الوقت + نجمة المرفق حسب المتجر.
    function applyTypeControls(r) {
      const form = r.closest('.nxex-leave-form');
      const label = r.closest('.nxex-leave-type');
      if (!form || !label) return;
      form.querySelectorAll('.nxex-leave-type').forEach((l) => l.classList.remove('active'));
      if (r.checked && !r.disabled) label.classList.add('active');
      const needsTime = label.getAttribute('data-needs-time') === 'true';
      const timeRow = form.querySelector('.nxex-time-row');
      if (timeRow) timeRow.hidden = !needsTime;
      // الأنواع الزمنية (أوفرتايم/مغادرة): تُقاس بالوقت لا بعدد الأيام.
      const daycount = form.querySelector('[data-daycount]');
      if (daycount) daycount.hidden = needsTime;
      const star = form.querySelector('[data-attach-star]');
      if (star) star.hidden = label.getAttribute('data-attach-req') !== 'true';
      const hint = form.querySelector('[data-attach-hint]');
      if (hint) {
        const req = label.getAttribute('data-attach-req') === 'true';
        const lbl = label.getAttribute('data-attach-label');
        hint.textContent = (req && lbl) ? '(' + lbl + ')' : '';
      }
      checkCross(form);
    }

    // تقاطع منتصف الليل: لو وقت النهاية ≤ وقت البداية والنوع زمني ⟹ الحركة عبر يومين
    // (التاريخ المحدَّد + غد). تُضبط قيمة تاريخ النهاية المخفية تلقائياً.
    function checkCross(form) {
      const label = form.querySelector('.nxex-leave-type input[type=radio]:checked')?.closest('.nxex-leave-type');
      const needsTime = label && label.getAttribute('data-needs-time') === 'true';
      const ft = form.querySelector('input[name=fromTime]')?.value;
      const tt = form.querySelector('input[name=toTime]')?.value;
      const fromD = form.querySelector('[data-dp-from]');
      const toD = form.querySelector('[data-dp-to]');
      const note = form.querySelector('[data-cross-note]');
      const cross = !!(needsTime && ft && tt && tt <= ft);
      if (note) note.hidden = !cross;
      if (needsTime && fromD && toD && fromD.value) {
        if (cross) {
          const d = new Date(fromD.value); d.setDate(d.getDate() + 1);
          toD.value = d.getFullYear() + '-' + String(d.getMonth() + 1).padStart(2, '0') + '-' + String(d.getDate()).padStart(2, '0');
        } else {
          toD.value = fromD.value;
        }
      }
      // المدة المطلوبة (تحسب تقاطع منتصف الليل).
      const dur = form.querySelector('[data-duration]');
      const durVal = form.querySelector('[data-duration-val]');
      if (needsTime && ft && tt) {
        const [fh, fm] = ft.split(':').map(Number);
        const [th, tm] = tt.split(':').map(Number);
        let mins = (th * 60 + tm) - (fh * 60 + fm);
        if (mins <= 0) mins += 24 * 60;
        const h = Math.floor(mins / 60), m = mins % 60;
        if (durVal) durVal.textContent = ((h ? h + 'س ' : '') + (m ? m + 'د' : '')).trim() || '0د';
        if (dur) dur.hidden = false;
      } else if (dur) {
        dur.hidden = true;
      }
    }

    leaveModal.querySelectorAll('.nxex-leave-type input[type=radio]').forEach((r) => {
      r.addEventListener('change', () => applyTypeControls(r));
    });
    // إعادة فحص التقاطع عند تغيّر وقت أو تاريخ.
    leaveModal.addEventListener('change', (e) => {
      if (e.target.matches('[data-tp-input], [data-dp-from], [data-dp-to]')) {
        const form = e.target.closest('.nxex-leave-form');
        if (form) checkCross(form);
      }
    });
    leaveModal.querySelectorAll('.nxex-leave-form').forEach((form) => {
      const checked = form.querySelector('.nxex-leave-type input[type=radio]:checked');
      if (checked) applyTypeControls(checked);
    });

    // حساب عدد الأيام حياً
    const fromI = leaveModal.querySelector('[data-leave-from]');
    const toI = leaveModal.querySelector('[data-leave-to]');
    const daysO = leaveModal.querySelector('[data-leave-days]');
    const calcDays = () => {
      if (!fromI.value || !toI.value) { daysO.textContent = '—'; return; }
      const d = Math.floor((new Date(toI.value) - new Date(fromI.value)) / 86400000) + 1;
      daysO.textContent = d > 0 ? d : '—';
    };
    if (fromI && toI && daysO) { fromI.addEventListener('change', calcDays); toI.addEventListener('change', calcDays); }

    // لوحة التقويم مطلقة (position:absolute) فيقصّها جسم الشاشة المتمرّر — نجعلها
    // «تطفو» ثابتة أسفل الشاشة (fixed) لتظهر كاملة وتهرب من القص. نرصد فتح كل تقويم
    // بـMutationObserver (يعمل رغم إيقاف التقويم لانتشار النقرة)، وinline!important
    // ليتغلّب على تموضع التقويم الداخلي.
    const dockPanel = (picker) => {
      const panel = picker.querySelector('.nxcal__panel');
      if (!panel) return;
      panel.style.setProperty('position', 'fixed', 'important');
      panel.style.setProperty('left', '12px', 'important');
      panel.style.setProperty('right', '12px', 'important');
      panel.style.setProperty('top', 'auto', 'important');
      panel.style.setProperty('bottom', 'calc(14px + env(safe-area-inset-bottom))', 'important');
      panel.style.setProperty('width', 'auto', 'important');
      panel.style.setProperty('max-height', '64dvh', 'important');
      panel.style.setProperty('z-index', '600', 'important');
    };
    leaveModal.querySelectorAll('.nxcal').forEach((picker) => {
      new MutationObserver(() => {
        // التقويم يموضِع لوحته عبر rAF بعد تغيير الصنف؛ setTimeout يلي الـrAF فيتغلّب عليه.
        if (picker.classList.contains('is-open')) setTimeout(() => dockPanel(picker), 40);
      }).observe(picker, { attributes: true, attributeFilter: ['class'] });
    });
  }

  // ===== حالة القسم الحالي: إضاءة «المزيد» + وسم الصفحة (لإخفاء الهيرو) =====
  function syncState() {
    const active = document.querySelector('.nxex-pane.active')?.dataset.nxexPane || 'home';
    if (moreBtn) moreBtn.classList.toggle('active', moreSet.has(active));
    if (pageEl) pageEl.setAttribute('data-current', active);
  }

  // اختيار أي قسم: يقفل «المزيد»، يحدّث الحالة، ويمرّر (لأعلى أو لهدف محدَّد).
  document.addEventListener('click', (e) => {
    const item = e.target.closest('[data-nxex-tab]');
    if (!item) return;
    if (moreSheet && document.getElementById('nxex-more-sheet')?.contains(item)) moreSheet.close();
    if (reqSheet && document.getElementById('nxex-req-sheet')?.contains(item)) reqSheet.close();
    const target = item.getAttribute('data-scroll-target');
    setTimeout(() => {
      syncState();
      const el = target ? document.querySelector(target) : null;
      if (el) el.scrollIntoView({ behavior: 'smooth', block: 'start' });
      else window.scrollTo({ top: 0, behavior: 'smooth' });
    }, 0);
  });

  syncState();
})();
