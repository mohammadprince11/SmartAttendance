// منتقي الموظف المشترك — نظير نافذة «بحث عن الموظف» بكيان.
//
// لا يُحمّل أي موظف حتى تُفتح النافذة أو يُكتب رمز: الشاشات كانت تحمّل 1356
// موظفاً بكل فتحة صفحة داخل <select>.
(function () {
    'use strict';

    var ENDPOINT = '/Employees/Lookup';
    var modal = null;

    function el(tag, cls, text) {
        var n = document.createElement(tag);
        if (cls) n.className = cls;
        if (text != null) n.textContent = text;
        return n;
    }

    // `root` يحدّد نطاق البحث: شاشات ما بعد الإنهاء تطلب منتهيَ الخدمة أيضاً.
    function includesInactive(root) {
        return !!root && root.getAttribute('data-zyep-inactive') === '1';
    }

    function fetchEmployees(q, root) {
        return fetch(ENDPOINT + '?q=' + encodeURIComponent(q || '')
            + (includesInactive(root) ? '&includeInactive=true' : ''), {
            headers: { 'Accept': 'application/json' }
        }).then(function (r) {
            // 403 = خارج نطاق التخويل. تُعامَل كنتيجة فارغة لا كعطل.
            if (!r.ok) return { total: 0, capped: false, items: [] };
            return r.json();
        }).catch(function () { return { total: 0, capped: false, items: [] }; });
    }

    function apply(root, emp) {
        root.querySelector('.zyep-id').value = emp ? emp.id : '';
        root.querySelector('.zyep-code').value = emp ? emp.code : '';
        root.querySelector('.zyep-name').value = emp ? emp.name : '';
        root.dispatchEvent(new CustomEvent('zyep:change', { detail: emp, bubbles: true }));

        if (emp && root.querySelector('.zyep-auto')) {
            var form = root.closest('form');
            if (form) form.submit();
        }
    }

    // ---- النافذة: تُبنى مرة واحدة وتُعاد للجميع ----
    function ensureModal() {
        if (modal) return modal;

        var back = el('div', 'zyep-backdrop');
        var box = el('div', 'zyep-modal');
        var head = el('div', 'zyep-modal-head');
        head.appendChild(el('b', null, 'بحث عن الموظف'));
        var close = el('button', 'zyep-close', '×');
        close.type = 'button';
        close.setAttribute('aria-label', 'إغلاق');
        close.title = 'إغلاق';
        head.appendChild(close);

        var search = el('input', 'zyep-search');
        search.type = 'search';
        search.placeholder = 'إبحث عن الموظف';
        search.setAttribute('autocomplete', 'off');

        var count = el('div', 'zyep-count', '');
        var list = el('div', 'zyep-list');

        // تذييل التعدّد: يظهر بوضع `multi` فقط. بلا زرّ إغلاق صريح تصير النافذة
        // بلا مخرجٍ مفهوم — فالنقر لم يعد يُغلقها كما بالمفرد.
        var foot = el('div', 'zyep-foot');
        var done = el('button', 'zyep-done', 'تمّ');
        done.type = 'button';
        foot.appendChild(done);

        box.appendChild(head);
        box.appendChild(search);
        box.appendChild(count);
        box.appendChild(list);
        box.appendChild(foot);
        back.appendChild(box);
        document.body.appendChild(back);

        modal = { back: back, search: search, count: count, list: list, foot: foot, target: null, mode: 'single' };

        function hide() { back.classList.remove('open'); modal.target = null; }
        done.addEventListener('click', hide);
        close.addEventListener('click', hide);
        back.addEventListener('click', function (e) { if (e.target === back) hide(); });
        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && back.classList.contains('open')) hide();
        });

        var timer = null;
        function run() {
            var q = search.value;
            count.textContent = 'جارٍ البحث…';
            fetchEmployees(q, modal.target).then(function (data) {
                // العدّاد ظاهر كما عندهم: «مجموع النتائج: (925)» — لكن عندنا سقف
                // صفحة. فحين يُبتَر نُعلن السقف («+50» ونداء بالتضييق) بدل أن
                // نعرض حجم الصفحة كأنه المجموع.
                count.textContent = data.capped
                    ? 'مجموع النتائج: (+' + data.total + ') — ضيّق البحث لعرض الباقي'
                    : 'مجموع النتائج: (' + data.total + ')';
                list.innerHTML = '';

                if (!data.items.length) {
                    list.appendChild(el('div', 'zyep-empty', 'لا نتائج مطابقة.'));
                    return;
                }

                var head = el('div', 'zyep-row zyep-head');
                ['رمز', 'اسم الموظف', 'وحدة عمل', 'الهيكلية'].forEach(function (h) {
                    head.appendChild(el('span', null, h));
                });
                list.appendChild(head);

                data.items.forEach(function (emp) {
                    var row = el('div', 'zyep-row');
                    [emp.code, emp.name, emp.unit || '—', emp.hierarchy || '—'].forEach(function (v) {
                        row.appendChild(el('span', null, v));
                    });

                    if (modal.mode === 'multi') {
                        // المختار سلفاً يظهر معلَّماً — وإلا أعاد المستخدم اختيار
                        // من اختاره ولا يدري، ثم بحث عن سبب عدم تكرار الشريحة.
                        if (modal.target && hasChip(modal.target, emp.id)) row.classList.add('is-picked');

                        row.addEventListener('click', function () {
                            if (!modal.target) return;
                            var on = toggleChip(modal.target, emp);
                            row.classList.toggle('is-picked', on);
                        });
                    } else {
                        row.addEventListener('click', function () {
                            if (modal.target) apply(modal.target, emp);
                            hide();
                        });
                    }

                    list.appendChild(row);
                });
            });
        }

        search.addEventListener('input', function () {
            clearTimeout(timer);
            timer = setTimeout(run, 250);
        });

        modal.run = run;
        modal.hide = hide;
        return modal;
    }

    // ---- التعدّد: شرائح + حقل مخفيّ لكلٍّ منها ----
    // الحقول المخفيّة تحمل **نفس الاسم** مكرّراً، فالربط بـint[] بالخادم يبقى
    // كما كان مع `<select multiple>` — لا تتغيّر جهة الخادم إطلاقاً.

    function chipsBox(root) { return root.querySelector('[data-zyepm-chips]'); }

    function hasChip(root, id) {
        return !!chipsBox(root).querySelector('[data-zyepm-chip][data-id="' + id + '"]');
    }

    function syncCount(root) {
        var n = chipsBox(root).querySelectorAll('[data-zyepm-chip]').length;
        var c = root.querySelector('[data-zyepm-count]');
        if (c) c.textContent = 'المحدَّدون: ' + n;
        root.dispatchEvent(new CustomEvent('zyepm:change', { detail: { count: n }, bubbles: true }));
    }

    function removeChip(root, id) {
        var chip = chipsBox(root).querySelector('[data-zyepm-chip][data-id="' + id + '"]');
        if (chip) chip.remove();
        syncCount(root);
    }

    function addChip(root, emp) {
        var name = root.getAttribute('data-zyep-name') || 'employeeIds';
        var chip = el('span', 'zyepm-chip');
        chip.setAttribute('data-zyepm-chip', '');
        chip.setAttribute('data-id', emp.id);
        chip.appendChild(el('span', 'zyepm-chip-code', emp.code));
        chip.appendChild(el('span', 'zyepm-chip-name', emp.name));

        var x = el('button', 'zyepm-remove', '×');
        x.type = 'button';
        x.title = 'إزالة';
        x.setAttribute('aria-label', 'إزالة');
        x.addEventListener('click', function () { removeChip(root, emp.id); });
        chip.appendChild(x);

        var hidden = el('input');
        hidden.type = 'hidden';
        hidden.name = name;
        hidden.value = emp.id;
        chip.appendChild(hidden);

        chipsBox(root).appendChild(chip);
        syncCount(root);
    }

    /// يبدّل الاختيار ويرجع true إذا صار مختاراً.
    function toggleChip(root, emp) {
        if (hasChip(root, emp.id)) { removeChip(root, emp.id); return false; }
        addChip(root, emp);
        return true;
    }

    function open(root, mode) {
        var m = ensureModal();
        m.target = root;
        m.mode = mode || 'single';
        m.back.classList.toggle('is-multi', m.mode === 'multi');
        m.search.value = '';
        m.back.classList.add('open');
        m.run();
        m.search.focus();
    }

    function init(scope) {
        (scope || document).querySelectorAll('[data-zyep]').forEach(function (root) {
            if (root.__zyep) return;
            root.__zyep = true;

            var name = root.querySelector('.zyep-name');
            var openBtn = root.querySelector('.zyep-open');
            var code = root.querySelector('.zyep-code');

            // النقر على حقل الاسم يفتح النافذة — سلوك كيان حرفياً.
            name.addEventListener('click', function () { open(root); });
            openBtn.addEventListener('click', function () { open(root); });

            // كتابة الرمز تحسم الموظف عند مطابقة تامّة؛ ورمزٌ لا يقابل أحداً
            // **يُعلَن** لا يُتجاهَل بصمت.
            var t = null;
            code.addEventListener('input', function () {
                clearTimeout(t);
                root.querySelector('.zyep-id').value = '';
                root.querySelector('.zyep-name').value = '';
                var v = code.value.trim();
                if (!v) { code.classList.remove('zyep-bad'); return; }

                t = setTimeout(function () {
                    fetchEmployees(v, root).then(function (data) {
                        var hit = data.items.filter(function (e) { return e.code === v; })[0];
                        if (hit) { code.classList.remove('zyep-bad'); apply(root, hit); }
                        else { code.classList.add('zyep-bad'); code.title = 'لا موظف بهذا الرمز'; }
                    });
                }, 350);
            });
        });

        // المنتقي المتعدّد
        (scope || document).querySelectorAll('[data-zyep-multi]').forEach(function (root) {
            if (root.__zyepm) return;
            root.__zyepm = true;

            var add = root.querySelector('[data-zyepm-add]');
            if (add) add.addEventListener('click', function () { open(root, 'multi'); });

            // الشرائح المرسومة من الخادم تحتاج ربط زرّ الحذف (المضافة بالـJS
            // تُربَط عند إنشائها).
            root.querySelectorAll('[data-zyepm-chip]').forEach(function (chip) {
                var x = chip.querySelector('.zyepm-remove');
                if (x) x.addEventListener('click', function () {
                    removeChip(root, chip.getAttribute('data-id'));
                });
            });

            syncCount(root);
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () { init(document); });
    } else {
        init(document);
    }

    window.ZyEmployeePicker = { init: init };
})();
