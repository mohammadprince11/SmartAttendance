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

    function fetchEmployees(q) {
        return fetch(ENDPOINT + '?q=' + encodeURIComponent(q || ''), {
            headers: { 'Accept': 'application/json' }
        }).then(function (r) {
            // 403 = خارج نطاق التخويل. تُعامَل كنتيجة فارغة لا كعطل.
            if (!r.ok) return { total: 0, items: [] };
            return r.json();
        }).catch(function () { return { total: 0, items: [] }; });
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
        head.appendChild(close);

        var search = el('input', 'zyep-search');
        search.type = 'search';
        search.placeholder = 'إبحث عن الموظف';
        search.setAttribute('autocomplete', 'off');

        var count = el('div', 'zyep-count', '');
        var list = el('div', 'zyep-list');

        box.appendChild(head);
        box.appendChild(search);
        box.appendChild(count);
        box.appendChild(list);
        back.appendChild(box);
        document.body.appendChild(back);

        modal = { back: back, search: search, count: count, list: list, target: null };

        function hide() { back.classList.remove('open'); modal.target = null; }
        close.addEventListener('click', hide);
        back.addEventListener('click', function (e) { if (e.target === back) hide(); });
        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && back.classList.contains('open')) hide();
        });

        var timer = null;
        function run() {
            var q = search.value;
            count.textContent = 'جارٍ البحث…';
            fetchEmployees(q).then(function (data) {
                // العدّاد ظاهر كما عندهم: «مجموع النتائج: (925)».
                count.textContent = 'مجموع النتائج: (' + data.total + ')';
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
                    row.addEventListener('click', function () {
                        if (modal.target) apply(modal.target, emp);
                        hide();
                    });
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

    function open(root) {
        var m = ensureModal();
        m.target = root;
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
                    fetchEmployees(v).then(function (data) {
                        var hit = data.items.filter(function (e) { return e.code === v; })[0];
                        if (hit) { code.classList.remove('zyep-bad'); apply(root, hit); }
                        else { code.classList.add('zyep-bad'); code.title = 'لا موظف بهذا الرمز'; }
                    });
                }, 350);
            });
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () { init(document); });
    } else {
        init(document);
    }

    window.ZyEmployeePicker = { init: init };
})();
