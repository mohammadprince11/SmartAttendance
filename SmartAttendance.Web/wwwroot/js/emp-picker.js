/*
 * منتقي الموظف (نمط كيان) — مكوّن قابل لإعادة الاستخدام.
 * حقلان ملتصقان: «رمز» (كتابة الكود تظهّر الاسم فوراً) + «اسم الموظف» (نقر يفتح
 * قائمة بحث منبثقة بالاسم/الرمز مع عدّاد نتائج). كل شي client-side على مصفوفة
 * موظفين محمّلة بالصفحة {Id, No, Name}. يحقن أنماطه مرة واحدة.
 *
 * الاستخدام:
 *   var picker = EmpPicker.init({ code, name, hidden, employees });
 *   picker.set(employeeObjOrNull);   // لتعبئة/تفريغ برمجياً (عند التعديل)
 */
(function () {
    if (window.EmpPicker) return;

    var styleInjected = false;
    function injectStyle() {
        if (styleInjected) return; styleInjected = true;
        var css = ''
            + '.empk-field{display:flex;align-items:stretch}'
            + '.empk-field input{min-height:38px;border:1px solid rgba(73,111,151,.25);background:rgba(0,0,0,.16);color:inherit;padding:6px 10px;font-family:inherit}'
            + 'html[data-theme="light"] .empk-field input{background:#fff}'
            + '.empk-code{width:34%;border-radius:10px 0 0 10px;border-inline-start:0;text-align:center;direction:ltr;font-weight:900}'
            + '.empk-name{flex:1;border-radius:0 10px 10px 0;cursor:pointer;background:rgba(0,0,0,.24)!important}'
            + 'html[data-theme="light"] .empk-name{background:#f2f7fb!important}'
            + '.empk-backdrop{position:fixed;inset:0;background:rgba(2,8,15,.6);backdrop-filter:blur(2px);z-index:2200;display:none;align-items:flex-start;justify-content:center}'
            + '.empk-backdrop.open{display:flex}'
            + '.empk-modal{margin-top:6vh;width:min(560px,94vw);max-height:82vh;display:flex;flex-direction:column;background:var(--sa-surface,#0a1f36);border:1px solid rgba(73,111,151,.3);border-radius:18px;overflow:hidden;box-shadow:0 20px 60px rgba(0,0,0,.5)}'
            + 'html[data-theme="light"] .empk-modal{background:#fff}'
            + '.empk-head{display:flex;align-items:center;justify-content:space-between;padding:14px 18px;border-bottom:1px solid rgba(73,111,151,.22)}'
            + '.empk-head h3{margin:0;font-size:15px;font-weight:1000;color:var(--sa-text,#DCEAF4)}'
            + 'html[data-theme="light"] .empk-head h3{color:#0b1d31}'
            + '.empk-x{background:transparent;border:0;color:var(--sa-muted,#8FA8BC);font-size:22px;cursor:pointer;line-height:1}'
            + '.empk-search{padding:12px 18px}'
            + '.empk-search input{width:100%;min-height:40px;border-radius:10px;border:1px solid rgba(73,111,151,.3);background:rgba(0,0,0,.18);color:inherit;padding:8px 12px;font-size:14px;font-weight:800}'
            + 'html[data-theme="light"] .empk-search input{background:#f2f7fb}'
            + '.empk-count{padding:0 18px 8px;font-size:12px;font-weight:900;color:#12D9E3}'
            + '.empk-list{overflow-y:auto;padding:0 8px 10px}'
            + '.empk-row{display:flex;align-items:center;gap:10px;padding:9px 12px;border-radius:10px;cursor:pointer}'
            + '.empk-row:hover{background:rgba(18,217,227,.08)}'
            + '.empk-av{width:30px;height:30px;border-radius:9px;display:flex;align-items:center;justify-content:center;font-weight:1000;color:#04141d;background:linear-gradient(135deg,#12D9E3,#4ade80);flex:0 0 auto}'
            + '.empk-nm{flex:1;font-weight:900;color:var(--sa-text,#DCEAF4);font-size:14px}'
            + 'html[data-theme="light"] .empk-nm{color:#0b1d31}'
            + '.empk-cd{direction:ltr;font-weight:900;color:var(--sa-muted,#8FA8BC);font-size:12px}'
            + '.empk-empty{padding:24px;text-align:center;color:var(--sa-muted,#8FA8BC);font-weight:800}';
        var s = document.createElement('style'); s.textContent = css; document.head.appendChild(s);
    }

    var modal, listEl, searchEl, countEl, current;
    function ensureModal() {
        if (modal) return;
        injectStyle();
        modal = document.createElement('div');
        modal.className = 'empk-backdrop';
        modal.innerHTML =
            '<div class="empk-modal">' +
              '<div class="empk-head"><h3>بحث عن الموظف</h3><button type="button" class="empk-x">&times;</button></div>' +
              '<div class="empk-search"><input type="text" placeholder="إبحث عن الموظف — بالاسم أو الرمز" /></div>' +
              '<div class="empk-count"></div>' +
              '<div class="empk-list"></div>' +
            '</div>';
        document.body.appendChild(modal);
        listEl = modal.querySelector('.empk-list');
        searchEl = modal.querySelector('.empk-search input');
        countEl = modal.querySelector('.empk-count');
        modal.querySelector('.empk-x').onclick = close;
        modal.addEventListener('click', function (e) { if (e.target === modal) close(); });
        searchEl.addEventListener('input', function () { render(this.value); });
        document.addEventListener('keydown', function (e) { if (e.key === 'Escape') close(); });
    }

    function open(ctx) {
        ensureModal();
        current = ctx;
        modal.classList.add('open');
        searchEl.value = '';
        render('');
        setTimeout(function () { searchEl.focus(); }, 40);
    }
    function close() { if (modal) modal.classList.remove('open'); }

    function render(q) {
        q = (q || '').trim().toLowerCase();
        var emps = (current && current.employees) || [];
        var list = q ? emps.filter(function (e) {
            return ((e.No || '').toLowerCase().indexOf(q) >= 0) || ((e.Name || '').toLowerCase().indexOf(q) >= 0);
        }) : emps;
        countEl.textContent = 'مجموع النتائج (' + list.length + ')';
        listEl.innerHTML = list.length
            ? list.slice(0, 400).map(function (e) {
                var initial = (e.Name || '؟').trim().charAt(0) || '؟';
                return '<div class="empk-row" data-id="' + e.Id + '">' +
                    '<span class="empk-av">' + initial + '</span>' +
                    '<span class="empk-nm">' + (e.Name || '') + '</span>' +
                    '<span class="empk-cd">' + (e.No || '') + '</span></div>';
            }).join('')
            : '<div class="empk-empty">لا نتائج مطابقة</div>';
        Array.prototype.forEach.call(listEl.querySelectorAll('.empk-row'), function (row) {
            row.onclick = function () {
                var e = current.employees.find(function (x) { return String(x.Id) === row.getAttribute('data-id'); });
                if (current.onSelect) current.onSelect(e);
                close();
            };
        });
    }

    function init(opts) {
        var byCode = {};
        opts.employees.forEach(function (e) { byCode[String(e.No || '').trim().toLowerCase()] = e; });

        function fire(e) { if (opts.onChange) opts.onChange(e || null); }

        function set(e) {
            opts.hidden.value = e ? e.Id : '';
            opts.code.value = e ? (e.No || '') : '';
            opts.name.value = e ? (e.Name || '') : '';
            fire(e);
        }

        // كتابة الكود ← يظهّر الاسم فوراً
        opts.code.addEventListener('input', function () {
            var e = byCode[this.value.trim().toLowerCase()];
            if (e) { opts.hidden.value = e.Id; opts.name.value = e.Name || ''; }
            else { opts.hidden.value = ''; opts.name.value = ''; }
            fire(e);
        });

        function openPopup() { open({ employees: opts.employees, onSelect: set }); }
        opts.name.addEventListener('click', openPopup);
        opts.name.addEventListener('focus', openPopup);

        return { set: set };
    }

    window.EmpPicker = { init: init, open: open };
})();
