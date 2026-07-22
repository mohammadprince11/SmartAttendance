/* ==========================================================================
   ZYNORA — تأكيد بنقرتين موحد (بديل confirm() الأصلية القديمة الشكل)
   الاستخدام: أي زر submit أو رابط عليه data-zyconfirm="نص التأكيد":
   النقرة الأولى تسلّح الزر (يعرض النص بتوكيد أحمر)، والثانية خلال 3 ثوانٍ
   تنفذ الفعل الأصلي — وإلا يرجع لحاله. مندوب على المستند فيشمل أي محتوى لاحق.
   ========================================================================== */
(function () {
    var ARM_MS = 3000;

    document.addEventListener('click', function (event) {
        var btn = event.target.closest('[data-zyconfirm]');
        if (!btn) return;

        if (btn.dataset.zyArmed === '1') {
            // النقرة الثانية: فك التسليح وتمرير الفعل الأصلي (submit/رابط)
            delete btn.dataset.zyArmed;
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
            delete btn.dataset.zyArmed;
            btn.classList.remove('zy-armed');
            btn.innerHTML = btn.dataset.zyOriginal || btn.innerHTML;
        }, ARM_MS);
    }, true);
})();
