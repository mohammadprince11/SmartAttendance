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

        // **آخر** جدولٍ لا أوّله: الشبكة قد تكون مجموعةً بمجموعات مطويّة، وإلحاق
        // الصفّ بأعلاها يدفنه داخل قسمٍ مغلق. وترتيب الحقول يبقى صحيحاً بأي حال
        // لأن الخادم يزاوجها بترتيب المستند.
        var bodies = form.querySelectorAll('[data-zyw-rows]');
        var body = bodies.length ? bodies[bodies.length - 1] : null;
        if (!template || !body) return;

        // يُفتح القسم الحاوي كي يُرى الصفّ الجديد لا أن يُضاف بالخفاء.
        var group = body.closest('details');
        if (group) group.open = true;

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

    // ── لا يُرسَل إلا ما تغيّر ────────────────────────────────────────────────
    //
    // ⚠️ كان «حفظ» يرسل الشبكة كاملةً: تعديل اسمٍ واحد بشاشة فيها سبعون مخالفة
    // يبعث 421 حقلاً ويُعيد كتابة السبعين صفّاً بالقاعدة. قِيست الدورة 1.5 ثانية،
    // ورسالة النجاح تقول «عُدّلت 70» لمن عدّل واحداً.
    //
    // الحلّ **بتعطيل حقول الصفّ غير المتغيّر** لا بحذفها: الخادم يزاوج المصفوفات
    // بالفهرس، والحقل المعطَّل لا يُرسَل — فتسقط كل حقول الصفّ معاً وتبقى الفهارس
    // متطابقة. حذفُ حقلٍ واحد كان سيُزيح الأعمدة فيُحفظ اسمٌ بفئةٍ غيرها.

    // JSON لا وصلٌ بفاصل: قيمةٌ تحوي الفاصل نفسه كانت ستجعل صفَّين مختلفين
    // يتطابقان توقيعاً فلا يُرسَل تعديلٌ حقيقيّ — وJSON يهرب كل شيء بنفسه.
    function rowSignature(row) {
        return JSON.stringify(Array.prototype.map.call(
            row.querySelectorAll('input:not([type=checkbox]), select, textarea'),
            function (field) { return field.value; }
        ));
    }

    function stampRows(form) {
        form.querySelectorAll('[data-zyw-rows] > tr').forEach(function (row) {
            if (row.dataset.zywClean === undefined) row.dataset.zywClean = rowSignature(row);
        });
    }

    function isNewRow(row) {
        var id = row.querySelector('[name="rowId"]');
        return !id || id.value === '0' || id.value === '';
    }

    function isMarkedForDelete(row) {
        var box = row.querySelector('[name="deleteId"]');
        return !!(box && box.checked);
    }

    document.addEventListener('submit', function (event) {
        var form = event.target;
        if (!form.matches || !form.matches('[data-zyw-grid]')) return;

        // ⚠️ **زرّ الحذف يمرّ بلا ترشيح.** هو داخل نفس النموذج ويحمل `id` الصفّ
        // بـ`name`/`value`؛ ولو عُطّل صفُّه لكونه غير متغيّر لسقط المعرّف معه
        // فيصل للخادم أمرُ حذفٍ بلا هدف.
        if (event.submitter && event.submitter.hasAttribute('data-zyw-delete')) return;

        var kept = 0;

        form.querySelectorAll('[data-zyw-rows] > tr').forEach(function (row) {
            var changed = row.dataset.zywClean !== rowSignature(row);

            // الجديد والمحذوف يُرسلان دائماً؛ وغيرهما لا يُرسَل إن لم يتغيّر.
            if (changed || isNewRow(row) || isMarkedForDelete(row)) { kept++; return; }

            row.querySelectorAll('input, select, textarea').forEach(function (field) {
                field.disabled = true;
            });
        });

        // شبكةٌ بلا تغييرٍ واحد: لا داعي لدورةٍ كاملة تعيد تحميل الصفحة.
        if (kept === 0) {
            event.preventDefault();
            window.alert('لا تغييرات للحفظ.');
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

        document.querySelectorAll('[data-zyw-grid]').forEach(stampRows);
    });
})();
