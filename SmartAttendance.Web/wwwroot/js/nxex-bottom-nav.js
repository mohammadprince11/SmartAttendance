/* الشريط السفلي للموبايل: درج «المزيد» + منتقي «طلب جديد» + سلوك الأقسام كتطبيق.
 * تفعيل التبويب نفسه يتولّاه zynora-employee-experience.js عبر [data-nxex-tab]. */
(() => {
  const pageEl = document.querySelector('[data-nxex-page]');
  const moreSet = new Set(['profile', 'pulse', 'feedback', 'performance']);
  const moreBtn = document.getElementById('nxex-more-btn');

  // قفل تمرير الخلفية حين تكون شاشة/درج مفتوحاً (منع تسرّب التمرير للخلف).
  const lockScroll = (on) => {
    document.documentElement.classList.toggle('nxex-scroll-lock', on);
    document.body.classList.toggle('nxex-scroll-lock', on);
  };

  // ===== حاجز اكتمال ملف الموظف قبل أي طلب =====
  // هذا فحص تجربة مستخدم فقط؛ كل مسار POST يعيد الفحص في الخادم أيضاً.
  const eligibilityAlert = document.getElementById('nxex-request-eligibility-alert');
  const eligibilityMessage = eligibilityAlert && eligibilityAlert.querySelector('[data-request-eligibility-message]');
  const eligibilityFields = eligibilityAlert && eligibilityAlert.querySelector('[data-request-eligibility-fields]');

  function hideEligibilityAlert() {
    if (eligibilityAlert) eligibilityAlert.hidden = true;
  }

  function showEligibilityAlert(result) {
    if (!eligibilityAlert) return;
    const fallback = 'تعذّر التحقق من اكتمال بيانات الموظف. أعد المحاولة، وإذا استمرت المشكلة راجع الموارد البشرية.';
    if (eligibilityMessage) eligibilityMessage.textContent = (result && result.message) || fallback;
    if (eligibilityFields) {
      eligibilityFields.textContent = '';
      const fields = result && Array.isArray(result.missingFields) ? result.missingFields : [];
      fields.forEach((field) => {
        const item = document.createElement('li');
        item.textContent = field;
        eligibilityFields.appendChild(item);
      });
      eligibilityFields.hidden = fields.length === 0;
    }
    eligibilityAlert.hidden = false;
    eligibilityAlert.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
  }

  async function hasCompleteRequestProfile() {
    try {
      const response = await fetch('/EmployeePortal?handler=RequestEligibility', {
        method: 'GET',
        credentials: 'same-origin',
        cache: 'no-store',
        headers: { 'Accept': 'application/json' }
      });
      if (!response.ok) throw new Error('eligibility-http-' + response.status);
      const result = await response.json();
      if (result && result.eligible === true) {
        hideEligibilityAlert();
        return true;
      }
      showEligibilityAlert(result);
      return false;
    } catch (error) {
      showEligibilityAlert(null);
      return false;
    }
  }

  if (eligibilityAlert) {
    eligibilityAlert.querySelectorAll('[data-request-eligibility-close]')
      .forEach((button) => button.addEventListener('click', hideEligibilityAlert));
  }

  // ===== اكتمال نموذج الطلب داخل الشاشة =====
  // بعض الحقول المخصّصة (التاريخ/الوقت والقوائم المصممة) تُخزَّن في inputs مخفية؛
  // المتصفح لا يطبّق required على input[type=hidden]، ولذلك كان الإرسال يصل للخادم
  // ثم يعيد المستخدم إلى صفحة الطلبات برسالة. نفحصها صراحة ونقفل زر الإرسال حتى
  // تكتمل كل القيم المطلوبة، مع إبقاء تحقق الخادم كحاجز نهائي.
  const requestForms = [...document.querySelectorAll('form[data-request-submit]')];

  function addMissing(list, label) {
    const clean = (label || '').trim();
    if (clean && !list.includes(clean)) list.push(clean);
  }

  function controlLabel(control) {
    const explicit = control.getAttribute('data-request-required-label');
    if (explicit) return explicit;
    const owner = control.closest('.nxex-field, .efr-field, .esr-field, .edr-field, .ef-field, label');
    const label = owner && owner.querySelector('label, span');
    const text = (label ? label.textContent : owner ? owner.childNodes[0]?.textContent : '') || '';
    return text.replace(/\*/g, '').replace(/\s+/g, ' ').trim()
      || control.getAttribute('aria-label')
      || control.name
      || 'حقل مطلوب';
  }

  function isEmptyControl(control) {
    if (control.type === 'checkbox') return !control.checked;
    if (control.type === 'radio') {
      return ![...control.form.querySelectorAll('input[type="radio"]')]
        .some((item) => item.name === control.name && item.checked);
    }
    if (control.type === 'file') return !control.files || control.files.length === 0;
    if (control.tagName === 'SELECT' && control.multiple) return control.selectedOptions.length === 0;
    return !String(control.value || '').trim();
  }

  function collectMissingRequestFields(form) {
    const missing = [];
    const candidates = [...form.querySelectorAll('[required], [data-val-required], [data-request-required]')];
    candidates.forEach((control) => {
      if (control.disabled || control.closest('[disabled]')) return;
      if (isEmptyControl(control) || (control.willValidate && !control.validity.valid))
        addMissing(missing, controlLabel(control));
    });

    if (form.hasAttribute('data-request-requires-change')) {
      const changed = [...form.querySelectorAll('[name^="new_"], [name="EmployeePhoto"]')]
        .some((control) => !isEmptyControl(control));
      if (!changed) addMissing(missing, 'قيمة واحدة جديدة على الأقل');
    }

    if (form.hasAttribute('data-document-request')) {
      const template = form.querySelector('[name="TemplateId"]');
      const option = template && template.selectedOptions && template.selectedOptions[0];
      if (!template || Number(template.value) <= 0) {
        addMissing(missing, 'نوع الوثيقة');
      } else {
        const reason = form.querySelector('[name="Reason"]');
        const attachment = form.querySelector('[name="Attachment"]');
        if (option?.getAttribute('data-reason-required') === 'true' && (!reason || isEmptyControl(reason)))
          addMissing(missing, 'سبب الطلب');
        if (option?.getAttribute('data-attachment-required') === 'true' && (!attachment || isEmptyControl(attachment)))
          addMissing(missing, 'المرفق');
      }
    }

    if (form.classList.contains('nxex-leave-form')) {
      const selectedType = form.querySelector('[data-leave-select] .nxex-csel-opt[aria-selected="true"]');
      if (!selectedType || selectedType.getAttribute('data-disabled') === 'true')
        addMissing(missing, 'نوع الطلب');

      if (selectedType?.getAttribute('data-needs-time') === 'true') {
        const times = [...form.querySelectorAll('[data-tp-input]')];
        if (!times[0] || isEmptyControl(times[0])) addMissing(missing, 'وقت البداية');
        if (!times[1] || isEmptyControl(times[1])) addMissing(missing, 'وقت النهاية');
      }

      if (selectedType?.getAttribute('data-attach-req') === 'true') {
        const attachment = form.querySelector('input[type="file"][name="attachment"]');
        if (!attachment || isEmptyControl(attachment)) addMissing(missing, 'المرفق المطلوب');
      }
    }

    return missing;
  }

  function validationPanel(form) {
    let panel = form.querySelector('[data-request-validation]');
    if (panel) return panel;
    panel = document.createElement('div');
    panel.className = 'nxex-request-validation';
    panel.setAttribute('data-request-validation', '');
    panel.setAttribute('role', 'status');
    panel.setAttribute('aria-live', 'polite');
    const submit = form.querySelector('button[type="submit"], input[type="submit"]');
    if (submit) form.insertBefore(panel, submit);
    else form.appendChild(panel);
    return panel;
  }

  function syncRequestFormState(form, urgent) {
    const missing = collectMissingRequestFields(form);
    const punchBlocked = form.dataset.punchBlocked === 'true';
    const blocked = missing.length > 0 || punchBlocked;
    form.querySelectorAll('button[type="submit"], input[type="submit"]').forEach((button) => {
      button.disabled = blocked;
      button.setAttribute('aria-disabled', blocked ? 'true' : 'false');
    });

    const panel = validationPanel(form);
    if (missing.length === 0) {
      panel.hidden = true;
      panel.textContent = '';
      return true;
    }

    panel.classList.toggle('is-urgent', !!urgent);
    panel.textContent = '';
    const title = document.createElement('strong');
    title.textContent = urgent ? 'لا يمكن إرسال الطلب قبل إكمال:' : 'أكمل الحقول المطلوبة لتفعيل الإرسال:';
    const list = document.createElement('span');
    list.textContent = missing.join(' • ');
    panel.append(title, list);
    panel.hidden = false;
    if (urgent) panel.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    return false;
  }

  function setPunchBlocked(form, blocked) {
    form.dataset.punchBlocked = blocked ? 'true' : 'false';
    syncRequestFormState(form, false);
  }

  requestForms.forEach((form) => {
    syncRequestFormState(form, false);
    form.addEventListener('input', () => syncRequestFormState(form, false));
    form.addEventListener('change', () => syncRequestFormState(form, false));
  });

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

  // الدرجان (المزيد + منتقي «طلب جديد») موجودان بالتخطيط على كل الصفحات (نسخة كاملة
  // بصفحة البوابة، ونسخة روابط بالصفحات الفرعية)، فيفتحان بمكانهما بلا أي انتقال.
  const moreSheet = makeSheet(
    document.getElementById('nxex-more-sheet'),
    document.getElementById('nxex-more-backdrop'));
  const reqSheet = makeSheet(
    document.getElementById('nxex-req-sheet'),
    document.getElementById('nxex-req-backdrop'));
  // درج «الإعدادات»: يُفتح بزر الترس ⚙️ بالشريط العلوي (على كل الصفحات والمقاسات).
  const settingsSheet = makeSheet(
    document.getElementById('nxex-settings-sheet'),
    document.getElementById('nxex-settings-backdrop'));
  const settingsBtn = document.getElementById('nxex-settings-btn');
  if (settingsBtn && settingsSheet) {
    settingsBtn.addEventListener('click', () => settingsSheet.isOpen() ? settingsSheet.close() : settingsSheet.open());
  }
  if (moreBtn && moreSheet) {
    moreBtn.addEventListener('click', () => moreSheet.isOpen() ? moreSheet.close() : moreSheet.open());
  }
  document.querySelectorAll('[data-open-reqsheet]').forEach((el) => {
    el.addEventListener('click', async () => {
      if (await hasCompleteRequestProfile()) reqSheet && reqSheet.open();
    });
  });
  // توافق مع روابط قديمة (?new=1): يفتح المنتقي تلقائياً.
  if (reqSheet && new URLSearchParams(location.search).get('new') === '1') {
    setTimeout(async () => { if (await hasCompleteRequestProfile()) reqSheet.open(); }, 80);
  }

  // ===== مودال التضمين: يفتح نموذج أي طلب داخل البوابة (iframe) بمكانه بلا انتقال =====
  (function () {
    const modal = document.getElementById('nxex-embed-modal');
    const backdrop = document.getElementById('nxex-embed-backdrop');
    const frame = document.getElementById('nxex-embed-frame');
    const titleEl = document.getElementById('nxex-embed-title');
    if (!modal || !backdrop || !frame) return;
    let loadCount = 0;
    function openEmbed(src, title) {
      loadCount = 0;
      if (titleEl) titleEl.textContent = title || 'طلب';
      frame.src = src;
      backdrop.hidden = false; modal.hidden = false; lockScroll(true);
      setTimeout(() => { backdrop.classList.add('open'); modal.classList.add('open'); }, 10);
    }
    function closeEmbed() {
      backdrop.classList.remove('open'); modal.classList.remove('open'); lockScroll(false);
      setTimeout(() => { backdrop.hidden = true; modal.hidden = true; frame.src = 'about:blank'; }, 260);
    }
    document.querySelectorAll('[data-embed-src]').forEach((el) => {
      el.addEventListener('click', async () => {
        if (el.hasAttribute('data-request-entry') && !(await hasCompleteRequestProfile())) {
          if (reqSheet) reqSheet.close();
          return;
        }
        if (reqSheet) reqSheet.close();
        if (settingsSheet) settingsSheet.close();
        openEmbed(el.getAttribute('data-embed-src'), el.getAttribute('data-embed-title'));
      });
    });
    backdrop.addEventListener('click', closeEmbed);
    modal.querySelectorAll('[data-embed-close]').forEach((b) => b.addEventListener('click', closeEmbed));
    // إغلاق تلقائي عند نجاح الإرسال داخل الإطار (نفس الأصل): بعد أول تحميل، لو ظهرت
    // رسالة نجاح نغلق ونحدّث البوابة لعكس الطلب الجديد.
    frame.addEventListener('load', () => {
      loadCount++;
      if (loadCount < 2) return;
      try {
        const doc = frame.contentDocument;
        const ok = doc && doc.querySelector('.efr-alert:not(.danger), .hrms-alert.success, [data-request-success]');
        if (ok) { closeEmbed(); setTimeout(() => window.location.reload(), 300); }
      } catch (e) { /* تجاهل */ }
    });
  })();

  // روابط الطلب المباشرة في الصفحات الفرعية تمرّ بالفحص قبل التنقّل.
  document.querySelectorAll('a[data-request-entry]').forEach((link) => {
    link.addEventListener('click', async (event) => {
      event.preventDefault();
      if (await hasCompleteRequestProfile()) window.location.href = link.href;
      else if (reqSheet) reqSheet.close();
    });
  });

  // فحص ثانٍ لحظة الإرسال يحمي السيناريو الذي تُرك فيه النموذج مفتوحاً ثم تغيّرت
  // بيانات الملف. WeakSet يمنع حلقة submit عند إعادة الإرسال بعد نجاح الفحص.
  const approvedRequestForms = new WeakSet();
  document.addEventListener('submit', async (event) => {
    const form = event.target && event.target.closest
      ? event.target.closest('form[data-request-submit]')
      : null;
    if (!form) return;
    if (!syncRequestFormState(form, true) || form.dataset.punchBlocked === 'true') {
      event.preventDefault();
      event.stopImmediatePropagation();
      return;
    }
    if (approvedRequestForms.has(form)) {
      approvedRequestForms.delete(form);
      return;
    }

    event.preventDefault();
    const submitter = event.submitter;
    if (!(await hasCompleteRequestProfile())) return;
    approvedRequestForms.add(form);
    if (typeof form.requestSubmit === 'function') form.requestSubmit(submitter || undefined);
    else form.submit();
  }, true);

  // الصفحات الفرعية: لا توجد أقسام [.nxex-pane]، وأزرار تبويب الشريط السفلي مبدّلات
  // داخل صفحة البوابة فقط ⇒ نحوّلها لتنقّل حقيقي. «المزيد» و«+» أعلاه يفتحان بمكانهما.
  const isHub = !!document.querySelector('.nxex-pane');
  if (!isHub) {
    const tabUrl = (tab) => '/EmployeePortal?tab=' + encodeURIComponent(tab || 'home');
    document.querySelectorAll('.nxex-bnav-item[data-nxex-tab]').forEach((btn) => {
      btn.addEventListener('click', (e) => {
        e.preventDefault();
        window.location.href = tabUrl(btn.getAttribute('data-nxex-tab'));
      }, true);
    });
    return; // بقية المنطق (الأقسام/الشاشات) خاص بصفحة البوابة فقط
  }

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
        if (panel) { const s = panel.querySelector('[data-leave-select]'); if (s) applyTypeControls(s); }
      } else {
        if (modalTabs) modalTabs.hidden = false;
        if (modalTitle) modalTitle.textContent = 'طلب إجازة';
        const tabs = modalTabs ? [...modalTabs.querySelectorAll('[data-leave-tab]')] : [];
        tabs.forEach((b, i) => b.classList.toggle('active', i === 0));
        const firstKey = tabs[0] ? tabs[0].getAttribute('data-leave-tab') : null;
        panels.forEach((p) => { p.hidden = p.getAttribute('data-leave-panel') !== firstKey; });
        const fp = panels.find((p) => p.getAttribute('data-leave-panel') === firstKey);
        if (fp) { const s = fp.querySelector('[data-leave-select]'); if (s) applyTypeControls(s); }
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
      el.addEventListener('click', async () => {
        if (!(await hasCompleteRequestProfile())) { if (reqSheet) reqSheet.close(); return; }
        if (reqSheet) reqSheet.close();
        openLeave(null);
      });
    });
    // القدوم من رابط «إجازة» بصفحة فرعية (?open=leave): افتح شاشة الإجازة تلقائياً.
    if (new URLSearchParams(location.search).get('open') === 'leave') {
      setTimeout(async () => { if (await hasCompleteRequestProfile()) openLeave(null); }, 60);
    }
    document.querySelectorAll('[data-open-cat]').forEach((el) => {
      el.addEventListener('click', async () => {
        if (!(await hasCompleteRequestProfile())) { if (reqSheet) reqSheet.close(); return; }
        if (reqSheet) reqSheet.close();
        openLeave(el.getAttribute('data-open-cat'));
      });
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
    // تُستدعى بـ<select> النوع (أو أي عنصر داخل نموذجه). تقرأ ضوابط الخيار المختار
    // (وقت/مرفق) من data-* على <option> وتظهر/تخفي الحقول تبعاً له.
    function applyTypeControls(sel) {
      const select = sel && sel.matches && sel.matches('[data-leave-select]')
        ? sel
        : (sel.closest('.nxex-leave-form') || document).querySelector('[data-leave-select]');
      const form = select ? select.closest('.nxex-leave-form') : (sel.closest ? sel.closest('.nxex-leave-form') : null);
      if (!form || !select) return;
      const opt = select.querySelector('.nxex-csel-opt[aria-selected="true"]')
        || select.querySelector('.nxex-csel-opt:not([data-disabled="true"])');
      const needsTime = !!opt && opt.getAttribute('data-needs-time') === 'true';
      const timeRow = form.querySelector('.nxex-time-row');
      if (timeRow) timeRow.hidden = !needsTime;
      // الأنواع الزمنية (أوفرتايم/مغادرة): تُقاس بالوقت لا بعدد الأيام.
      const daycount = form.querySelector('[data-daycount]');
      if (daycount) daycount.hidden = needsTime;
      const star = form.querySelector('[data-attach-star]');
      if (star) star.hidden = !opt || opt.getAttribute('data-attach-req') !== 'true';
      const hint = form.querySelector('[data-attach-hint]');
      if (hint) {
        const req = !!opt && opt.getAttribute('data-attach-req') === 'true';
        const lbl = opt && opt.getAttribute('data-attach-label');
        hint.textContent = (req && lbl) ? '(' + lbl + ')' : '';
      }
      checkCross(form);
      checkPunchGate(form);
      syncRequestFormState(form, false);
    }

    // ملخص البصمات الحيّ: فور اختيار التاريخ لنوع زمني نعرض كل بصمات الدخول والخروج
    // الموجودة. إذا كان العدد ناقصاً نظهر التحذير ونقفل الإرسال، وإلا يبقى الملخص ظاهراً.
    // نُلغي الطلبات القديمة بعلامة تسلسل لكل نموذج حتى لا يكتب ردّ متأخر فوق حالة أحدث.
    async function checkPunchGate(form) {
      if (!form) return;
      const submit = form.querySelector('.nxex-leave-submit');
      const block = form.querySelector('[data-punch-block]');
      const clear = () => {
        if (block) { block.hidden = true; block.textContent = ''; }
        setPunchBlocked(form, false);
      };
      const typeSel = form.querySelector('[data-leave-select]');
      const typeOpt = typeSel && typeSel.querySelector('.nxex-csel-opt[aria-selected="true"]');
      const needsTime = !!typeOpt && typeOpt.getAttribute('data-needs-time') === 'true';
      const fromD = form.querySelector('[data-dp-from]');
      const toD = form.querySelector('[data-dp-to]');
      const from = fromD && fromD.value;
      const to = (toD && toD.value) || from;
      if (!needsTime) { clear(); return; }
      if (!block) return;
      if (!from) {
        block.classList.remove('is-blocked');
        block.replaceChildren();
        const title = document.createElement('strong');
        title.textContent = 'بصمات الحضور المرتبطة بالطلب';
        const hint = document.createElement('span');
        hint.className = 'nxex-punch-empty';
        hint.textContent = 'اختر تاريخ الطلب حتى تظهر بصمة الدخول أو الخروج أو كلاهما.';
        block.append(title, hint);
        block.hidden = false;
        setPunchBlocked(form, false);
        return;
      }
      const seq = (form._punchSeq = (form._punchSeq || 0) + 1);
      block.classList.remove('is-blocked');
      block.replaceChildren();
      const loading = document.createElement('span');
      loading.className = 'nxex-punch-empty';
      loading.textContent = 'جاري تحميل البصمات المسجلة…';
      block.appendChild(loading);
      block.hidden = false;
      setPunchBlocked(form, true);
      try {
        const endpoint = new URL(location.pathname, location.origin);
        endpoint.searchParams.set('handler', 'TimeGate');
        endpoint.searchParams.set('from', from);
        endpoint.searchParams.set('to', to);
        const res = await fetch(endpoint, {
          cache: 'no-store',
          credentials: 'same-origin',
          headers: { 'X-Requested-With': 'fetch', 'Accept': 'application/json' }
        });
        if (!res.ok) throw new Error('Punch gate request failed: ' + res.status);
        const data = await res.json();
        if (seq !== form._punchSeq) return; // تغيّرت الحالة أثناء الجلب — تجاهل الرد القديم
        if (!block) return;

        block.replaceChildren();
        block.classList.toggle('is-blocked', !!(data && data.blocked));

        const title = document.createElement('strong');
        title.textContent = 'البصمات المسجلة ضمن تاريخ الطلب';
        block.appendChild(title);

        const days = data && Array.isArray(data.punches) ? data.punches : [];
        if (days.length === 0) {
          const empty = document.createElement('span');
          empty.className = 'nxex-punch-empty';
          empty.textContent = 'لا توجد بصمات مسجلة في التاريخ المحدد.';
          block.appendChild(empty);
        } else {
          days.forEach((day) => {
            const row = document.createElement('div');
            row.className = 'nxex-punch-day';

            const date = document.createElement('b');
            date.textContent = day.date || '';
            row.appendChild(date);

            const ins = Array.isArray(day.checkIns) ? day.checkIns : [];
            const outs = Array.isArray(day.checkOuts) ? day.checkOuts : [];
            const values = document.createElement('span');
            values.textContent = [
              ins.length ? 'دخول: ' + ins.join('، ') : '',
              outs.length ? 'خروج: ' + outs.join('، ') : ''
            ].filter(Boolean).join(' • ') || 'لا توجد بصمات';
            row.appendChild(values);
            block.appendChild(row);
          });
        }

        if (data && data.blocked) {
          const warning = document.createElement('span');
          warning.className = 'nxex-punch-warning';
          warning.textContent = data.message || 'تعذّر تقديم الطلب: يوجد نسيان بصمة في التاريخ المحدد.';
          block.appendChild(warning);
        }

        block.hidden = false;
        setPunchBlocked(form, !!(data && data.blocked));
      } catch (e) {
        if (seq !== form._punchSeq) return;
        block.classList.remove('is-blocked');
        block.replaceChildren();
        const failed = document.createElement('span');
        failed.className = 'nxex-punch-warning';
        failed.textContent = 'تعذّر تحميل البصمات الآن. أعد اختيار التاريخ وحاول مرة أخرى.';
        block.appendChild(failed);
        block.hidden = false;
        // في الأنواع الزمنية لا نسمح بالإرسال إذا تعذّر التأكد من البصمات؛
        // إعادة اختيار التاريخ تعيد المحاولة، والخادم يبقى الحارس النهائي أيضاً.
        setPunchBlocked(form, true);
      }
    }

    // تقاطع منتصف الليل: لو وقت النهاية ≤ وقت البداية والنوع زمني ⟹ الحركة عبر يومين
    // (التاريخ المحدَّد + غد). تُضبط قيمة تاريخ النهاية المخفية تلقائياً.
    function checkCross(form) {
      const typeSel = form.querySelector('[data-leave-select]');
      const typeOpt = typeSel && typeSel.querySelector('.nxex-csel-opt[aria-selected="true"]');
      const needsTime = !!typeOpt && typeOpt.getAttribute('data-needs-time') === 'true';
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

    // ===== القائمة المنسدلة المخصّصة (قائمة مفتوحة بتصميم التطبيق) =====
    // القائمة position:absolute نسبةً لحاوية .nxex-csel، فتُلصَق تحت المربّع مباشرةً
    // وتبقى ملتصقة عند تمرير جسم المودال (لا transform/viewport يربكها).
    function initCustomSelect(widget) {
      const trigger = widget.querySelector('[data-csel-trigger]');
      const list = widget.querySelector('[data-csel-list]');
      const labelEl = widget.querySelector('[data-csel-label]');
      const input = widget.querySelector('[data-csel-input]');
      if (!trigger || !list || !input) return;
      const opts = [...widget.querySelectorAll('.nxex-csel-opt')];
      const close = () => { list.hidden = true; widget.classList.remove('open'); trigger.setAttribute('aria-expanded', 'false'); };
      const open = () => { list.hidden = false; widget.classList.add('open'); trigger.setAttribute('aria-expanded', 'true'); list.scrollIntoView({ block: 'nearest' }); };
      trigger.addEventListener('click', (e) => { e.stopPropagation(); if (list.hidden) open(); else close(); });
      opts.forEach((li) => li.addEventListener('click', () => {
        if (li.getAttribute('data-disabled') === 'true') return;
        opts.forEach((o) => o.setAttribute('aria-selected', o === li ? 'true' : 'false'));
        labelEl.textContent = li.getAttribute('data-label') || li.textContent.trim();
        input.value = li.getAttribute('data-value') || '';
        close();
        applyTypeControls(widget);
      }));
      document.addEventListener('click', (e) => { if (!widget.contains(e.target)) close(); });
    }
    leaveModal.querySelectorAll('[data-leave-select]').forEach(initCustomSelect);
    // إعادة فحص التقاطع عند تغيّر وقت أو تاريخ.
    leaveModal.addEventListener('change', (e) => {
      if (e.target.matches('[data-tp-input], [data-dp-from], [data-dp-to]')) {
        const form = e.target.closest('.nxex-leave-form');
        if (form) { checkCross(form); checkPunchGate(form); }
      }
    });
    leaveModal.querySelectorAll('.nxex-leave-form').forEach((form) => {
      const select = form.querySelector('[data-leave-select]');
      if (select) applyTypeControls(select);
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
