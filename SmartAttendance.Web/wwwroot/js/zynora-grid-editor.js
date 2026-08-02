/*
 * ZYNORA — شبكة محرَّرة داخل الصفّ.
 *
 * نمطٌ منقول عن كيان بعد فحصٍ حيّ لشاشتَي «فئات المخالفات» و«قائمة المخالفات»:
 * الشبكة **نموذج واحد**، وزرّ «صفّ جديد» يُلحق صفّاً فارغاً بأسفلها من قالب
 * <template>، وزرّ حفظٍ واحد يرسل الصفوف كلّها دفعةً واحدة. لا مودال ولا إعادة
 * تحميل لكل صفّ.
 *
 * والقوّة كلّها بترتيب الحقول: الخادم يقرأ `rowId[]` و`rowName[]` … متوازيةً
 * بالفهرس، والمتصفّح يرسل حقول النموذج **بترتيب ظهورها بالمستند** — فإلحاق
 * الصفّ بأسفل الجدول يضمن تطابق الفهارس بلا ترقيمٍ يدويّ يسهل أن يختلّ.
 *
 * ويتولّى أيضاً إظهار حقول قاعدة الجزاء بحسب نوع الإجراء: «اقتطاع من الراتب»
 * وحده يفتح الوعاء والمقام، و«إنهاء الخدمة» وحده يفتح سبب الإيقاف — فلا تُعرض
 * تسعة حقول مالية لإنذارٍ شفويّ لا يخصم فلساً.
 *
 * بلا اعتماديات خارجية — نفس نهج بقية سكربتات المشروع.
 */
(function () {
    'use strict';

    // ── إلحاق صفّ جديد ────────────────────────────────────────────────────────

    function addRow(form) {
        var template = form.querySelector('[data-zyw-template]');
        var body = form.querySelector('[data-zyw-rows]');
        if (!template || !body) return;

        var row = template.content.cloneNode(true);
        body.appendChild(row);

        // التركيز على أول حقلٍ نصّيّ بالصفّ الجديد: من ضغط «جديد» يريد الكتابة.
        var added = body.lastElementChild;
        if (!added) return;
        var first = added.querySelector('input[type="text"], input:not([type]), select');
        if (first) first.focus();
    }

    document.addEventListener('click', function (event) {
        var addButton = event.target.closest('[data-zyw-add]');
        if (addButton) {
            var form = addButton.closest('form');
            if (form) addRow(form);
            return;
        }

        // إزالة صفٍّ **لم يُحفظ بعد**: حذفٌ من الشاشة لا من القاعدة.
        var removeButton = event.target.closest('[data-zyw-remove]');
        if (removeButton) {
            var row = removeButton.closest('tr');
            if (row) row.remove();
            return;
        }

        // فتح نموذج تعديل قاعدة الجزاء بجانب صفّها.
        var editButton = event.target.closest('[data-zyw-edit-rule]');
        if (editButton) {
            var id = editButton.getAttribute('data-zyw-edit-rule');
            var target = document.querySelector('[data-zyw-rule-form="' + id + '"]');
            if (target) {
                target.hidden = !target.hidden;
                editButton.textContent = target.hidden ? 'تعديل' : 'إغلاق';
            }
        }
    });

    // ── حقول الجزاء تتبع نوع الإجراء ─────────────────────────────────────────

    function syncPenaltyFields(select) {
        var scope = select.closest('[data-zyw-penalty]');
        if (!scope) return;

        var value = select.value;
        var blocks = scope.querySelectorAll('[data-zyw-when]');

        for (var i = 0; i < blocks.length; i++) {
            var wanted = blocks[i].getAttribute('data-zyw-when');
            blocks[i].hidden = wanted !== value;
        }
    }

    document.addEventListener('change', function (event) {
        var select = event.target.closest('[data-zyw-action-type]');
        if (select) syncPenaltyFields(select);
    });

    document.addEventListener('DOMContentLoaded', function () {
        var selects = document.querySelectorAll('[data-zyw-action-type]');
        for (var i = 0; i < selects.length; i++) syncPenaltyFields(selects[i]);
    });
})();
