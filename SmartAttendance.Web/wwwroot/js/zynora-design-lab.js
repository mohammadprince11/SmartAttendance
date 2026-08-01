// مختبر المقاسات — يطبّق التغيير على الصفحة لحظياً قبل الاعتماد.
//
// المتغيّرات تُكتب على <html> لا على حاوية المعاينة وحدها: هكذا ترى أثر المقاس
// على **الصفحة نفسها** (شريطها العلوي · درجها · أزرار المختبر) لا على مربّعٍ
// معزول يكذب عليك.
(function () {
    'use strict';

    var form = document.getElementById('zdl-form');
    if (!form) return;

    var root = document.documentElement;
    var rows = [].slice.call(form.querySelectorAll('.zdl-row'));

    // القيم المعتمَدة عند فتح الصفحة — مرجعُ «تراجع».
    var applied = {};
    rows.forEach(function (row) {
        applied[row.getAttribute('data-token')] = row.querySelector('input[type=range]').value;
    });

    function paint(row) {
        var key = row.getAttribute('data-token');
        var unit = row.getAttribute('data-unit') || '';
        var input = row.querySelector('input[type=range]');
        var out = row.querySelector('.zdl-value');
        var value = input.value;

        root.style.setProperty('--zy-' + key, value + unit);
        if (out) out.textContent = value + unit;

        // ثلاث حالات مميّزة بصرياً: افتراضي · معتمَد مخصَّص · معدَّل بلا اعتماد.
        var isDefault = parseFloat(value) === parseFloat(row.getAttribute('data-default'));
        var isDirty = value !== applied[key];
        row.classList.toggle('zdl-row-custom', !isDefault);
        row.classList.toggle('zdl-row-dirty', isDirty);
    }

    rows.forEach(function (row) {
        var input = row.querySelector('input[type=range]');
        input.addEventListener('input', function () { paint(row); });
        paint(row);
    });

    var revert = document.getElementById('zdl-revert');
    if (revert) {
        revert.addEventListener('click', function () {
            rows.forEach(function (row) {
                row.querySelector('input[type=range]').value = applied[row.getAttribute('data-token')];
                paint(row);
            });
        });
    }
})();
