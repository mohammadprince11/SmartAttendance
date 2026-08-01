(function () {
    "use strict";

    // تاريخياً كان هذا الملف يفرض الداكن حصراً (قرار 2026-07 بإلغاء الفاتح)، ويعيد
    // فرضه بكل تحميل صفحة وبكل حدث storage. بعد اعتماد Design System v1 صار للمستخدم
    // زرّ تبديل فعلي — فبقاء الفرض أنتج العطل المُبلَّغ: «اختر فاتح ثم افتح صفحة
    // أخرى فيعود داكناً». السبب أن البوتستراب بالرأس يطبّق المحفوظ قبل أول رسمة،
    // ثم يأتي هذا الملف بذيل الصفحة فيسحقه.
    //
    // أُزيل الفرض. مصدر الحقيقة الوحيد: مفتاح "ZY.Theme" بـlocalStorage يطبّقه
    // الرأس مبكراً — وهنا **لا نطبّق شيئاً عند التحميل إطلاقاً**، فقط نُبقي واجهة
    // عامة قد يناديها كود قديم، ونزامن بين التبويبات.

    var storageKey = "ZY.Theme";

    function normalize(value) {
        return value === "light" ? "light" : "dark";
    }

    function current() {
        return normalize(document.documentElement.getAttribute("data-theme"));
    }

    function apply(theme) {
        var next = normalize(theme);
        document.documentElement.setAttribute("data-theme", next);
        try { localStorage.setItem(storageKey, next); } catch (_) { }
        return next;
    }

    // مزامنة بين التبويبات: تبديل الوضع بتبويب ينعكس على الباقي بلا إعادة تحميل.
    window.addEventListener("storage", function (event) {
        if (event.key === storageKey && event.newValue) {
            document.documentElement.setAttribute("data-theme", normalize(event.newValue));
        }
    });

    window.ZynoraTheme = {
        get: current,
        set: apply,
        toggle: function () { return apply(current() === "light" ? "dark" : "light"); }
    };
})();
