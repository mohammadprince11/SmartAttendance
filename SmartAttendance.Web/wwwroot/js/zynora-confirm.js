/* ==========================================================================
   ZYNORA — تأكيد بنقرتين موحد (بديل confirm() الأصلية القديمة الشكل)
   الاستخدام على عنصرين:
   1) زر submit أو رابط عليه data-zyconfirm="نص التأكيد": النقرة الأولى تسلّح الزر
      (يعرض النص بتوكيد أحمر)، والثانية خلال 3 ثوانٍ تنفّذ الفعل الأصلي.
   2) <form data-zyconfirm="نص التأكيد">: أول محاولة إرسال تُسلّح زر الإرسال،
      والإرسال الثاني خلال 3 ثوانٍ يمرّ. (بديل onsubmit="return confirm(...)").
   مندوب على المستند فيشمل أي محتوى مُحمّل لاحقاً.
   ========================================================================== */
(function () {
    var ARM_MS = 3000;

    function disarm(el, original) {
        delete el.dataset.zyArmed;
        el.classList.remove('zy-armed');
        if (original != null) el.innerHTML = original;
    }

    // (1) الأزرار والروابط
    document.addEventListener('click', function (event) {
        var btn = event.target.closest('[data-zyconfirm]');
        if (!btn || btn.tagName === 'FORM') return;

        if (btn.dataset.zyArmed === '1') {
            delete btn.dataset.zyArmed;   // النقرة الثانية: تمرير الفعل الأصلي
            return;
        }

        event.preventDefault();
        event.stopPropagation();

        btn.dataset.zyArmed = '1';
        btn.dataset.zyOriginal = btn.innerHTML;
        btn.classList.add('zy-armed');
        btn.innerHTML = btn.getAttribute('data-zyconfirm') || 'تأكيد؟';

        setTimeout(function () {
            if (btn.dataset.zyArmed !== '1') return;
            disarm(btn, btn.dataset.zyOriginal);
        }, ARM_MS);
    }, true);

    // (2) النماذج (form data-zyconfirm) — تسليح زر الإرسال
    document.addEventListener('submit', function (event) {
        var form = event.target;
        if (!form || !form.matches || !form.matches('form[data-zyconfirm]')) return;

        if (form.dataset.zyArmed === '1') {
            delete form.dataset.zyArmed;   // الإرسال الثاني: يمرّ
            return;
        }

        event.preventDefault();
        form.dataset.zyArmed = '1';

        var btn = event.submitter || form.querySelector('button[type=submit], [type=submit], button');
        var original = btn ? btn.innerHTML : null;
        if (btn) {
            btn.classList.add('zy-armed');
            btn.innerHTML = form.getAttribute('data-zyconfirm') || 'تأكيد؟';
        }

        setTimeout(function () {
            if (form.dataset.zyArmed !== '1') return;
            delete form.dataset.zyArmed;
            if (btn) disarm(btn, original);
        }, ARM_MS);
    }, true);
})();
