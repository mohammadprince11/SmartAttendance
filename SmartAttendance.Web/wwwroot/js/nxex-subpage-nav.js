/* الصفحات المستقلة (نسيان البصمة / تعديل البيانات): لا تحتوي ألواح تبويبات، لذا
 * شريط التنقّل السفلي وأزرار «المزيد» يجب أن تنتقل فعلياً لبوابة التبويب المطلوب
 * بدل محاولة تبديل لوح غير موجود (كان يسبّب تعليق المستخدم). */
(() => {
    // إن وُجدت ألواح (الصفحة الرئيسية) فلا تتدخّل — آليتها القائمة تكفي.
    if (document.querySelector('[data-nxex-pane]')) return;

    document.addEventListener('click', (e) => {
        const item = e.target.closest('[data-nxex-tab]');
        if (!item) return;
        const tab = item.getAttribute('data-nxex-tab');
        if (!tab) return;
        e.preventDefault();
        e.stopPropagation();
        window.location.href = '/EmployeePortal?tab=' + encodeURIComponent(tab);
    }, true);

    // زر «طلب جديد» العائم: على الصفحات المستقلة يعيد للرئيسية حيث منتقي الأنواع.
    document.querySelectorAll('[data-open-reqsheet]').forEach((el) => {
        el.addEventListener('click', (e) => {
            e.preventDefault();
            e.stopPropagation();
            window.location.href = '/EmployeePortal?tab=requests';
        }, true);
    });

    // كلندر النظام (nexora-calendar-v18) لوحته مطلقة فيقصّها جسم الصفحة على الموبايل.
    // نُرسيها ثابتةً أسفل الشاشة (fixed) لتظهر كاملة — نفس معالجة شاشة الإجازة.
    function dockPanel(picker) {
        const panel = picker.querySelector('.nxcal__panel');
        if (!panel) return;
        // توسيط اللوحة كاملةً عمودياً (رأس الشهر + الشبكة + زر التطبيق كلها ظاهرة).
        panel.style.setProperty('display', 'block', 'important');
        panel.style.setProperty('position', 'fixed', 'important');
        panel.style.setProperty('left', '12px', 'important');
        panel.style.setProperty('right', '12px', 'important');
        panel.style.setProperty('bottom', 'auto', 'important');
        panel.style.setProperty('top', '50%', 'important');
        panel.style.setProperty('transform', 'translateY(-50%)', 'important');
        panel.style.setProperty('width', 'auto', 'important');
        panel.style.setProperty('max-width', 'none', 'important');
        panel.style.setProperty('max-height', '92dvh', 'important');
        panel.style.setProperty('overflow-y', 'auto', 'important');
        panel.style.setProperty('z-index', '650', 'important');
    }
    function observeCalendars() {
        document.querySelectorAll('.nxcal').forEach((picker) => {
            if (picker.dataset.nxexDocked) return;
            picker.dataset.nxexDocked = '1';
            new MutationObserver(() => {
                if (picker.classList.contains('is-open')) setTimeout(() => dockPanel(picker), 30);
            }).observe(picker, { attributes: true, attributeFilter: ['class'] });
        });
    }
    observeCalendars();
    // التقويمات تُبنى بعد التحميل (v18 يمسح لاحقاً) — نراقب ظهورها.
    new MutationObserver(observeCalendars).observe(document.body, { childList: true, subtree: true });
})();
