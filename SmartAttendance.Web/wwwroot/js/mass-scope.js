/*
 * محدّد نطاق الإدخال الجماعي (نمط كيان «النطاق») — منطق مشترك بين شاشات الحركات.
 * تضبط الصفحة window.MASS_EMP = [{Id, No, Name}] قبل فتح النافذة.
 * تبقى massOpen() و massItemChanged() خاصة بكل صفحة (تُعيد ضبط حقولها الخاصة).
 */
function massTab(mode) {
    var m = document.getElementById('mass-ScopeMode'); if (m) m.value = mode;
    document.querySelectorAll('#mass-slide .mass-tab').forEach(function (t) { t.classList.toggle('on', t.getAttribute('data-m') === mode); });
    ['Manual', 'Paste', 'File', 'Criteria'].forEach(function (k) {
        var p = document.getElementById('mass-pane-' + k); if (p) p.hidden = (k !== mode);
    });
    if (mode === 'Paste') massCodeCount();
}

function massCount() {
    var el = document.getElementById('mass-count');
    if (el) el.textContent = '(' + document.querySelectorAll('#mass-emplist input:checked').length + ' محدد)';
}
function massEmpToggle(cb) { var l = cb.closest('.mass-emp'); if (l) l.classList.toggle('sel', cb.checked); massCount(); }

function massBuildEmps() {
    var list = document.getElementById('mass-emplist'); if (!list) return;
    var emps = window.MASS_EMP || [];
    list.innerHTML = emps.map(function (e) {
        var key = ((e.No || '') + ' ' + (e.Name || '')).toLowerCase();
        var initial = ((e.Name || '؟').trim().charAt(0)) || '؟';
        return '<label class="mass-emp" data-s="' + key + '">'
            + '<input type="checkbox" name="MassEmployeeIds" value="' + e.Id + '" onchange="massEmpToggle(this)" />'
            + '<span class="av">' + initial + '</span>'
            + '<span class="nm">' + (e.Name || '') + '</span>'
            + '<span class="cd">' + (e.No || '') + '</span></label>';
    }).join('');
}
function massRenderEmps() {
    var box = document.getElementById('mass-empsearch');
    var q = ((box && box.value) || '').trim().toLowerCase();
    document.querySelectorAll('#mass-emplist .mass-emp').forEach(function (l) {
        l.classList.toggle('hidden', !(!q || l.getAttribute('data-s').indexOf(q) >= 0));
    });
}
function massSelectAll(on) {
    document.querySelectorAll('#mass-emplist .mass-emp').forEach(function (l) {
        if (!l.classList.contains('hidden')) { var cb = l.querySelector('input'); cb.checked = on; l.classList.toggle('sel', on); }
    });
    massCount();
}

// ===== لصق الأكواد (تحويل تلقائي لفواصل — نمط كيان) =====
function massTokens(s) { return (s || '').split(/[\s,;]+/).map(function (x) { return x.trim(); }).filter(Boolean); }
function massCodeCount() {
    var el = document.getElementById('mass-codecount'); var ta = document.getElementById('mass-codes');
    if (el && ta) el.textContent = '(' + massTokens(ta.value).length + ' كود)';
}
function massOnPaste(e) {
    e.preventDefault();
    var text = ((e.clipboardData || window.clipboardData).getData('text') || '');
    var toks = massTokens(text);
    var ta = document.getElementById('mass-codes'); var cur = (ta.value || '').trim();
    ta.value = (cur ? cur + ', ' : '') + toks.join(', ');
    massCodeCount();
}
function massPasteClip() {
    if (navigator.clipboard && navigator.clipboard.readText) {
        navigator.clipboard.readText().then(function (t) {
            document.getElementById('mass-codes').value = massTokens(t).join(', ');
            massCodeCount();
        }).catch(function () { alert('تعذّر الوصول للحافظة — ألصق يدوياً (Ctrl+V) داخل الحقل.'); });
    } else { alert('المتصفح لا يدعم القراءة من الحافظة — ألصق يدوياً (Ctrl+V).'); }
}
