
(function () {
    const root = document.getElementById('nxApprovedAnnouncementStudio');
    if (!root) return;

    const templates = [
        { key: 'marriage', category: 'تهنئة', title: 'تهنئة بمناسبة الزواج', body: 'تتقدم الشركة بأصدق التهاني إلى زميلنا العزيز بمناسبة الزواج. نتمنى له حياة سعيدة وموفقة.', date: '', department: 'قسم الموارد البشرية' },
        { key: 'condolence', category: 'تعزية', title: 'إِنَّا لِلّهِ وَإِنَّا إِلَيْهِ رَاجِعونَ', body: 'تتقدم الشركة بخالص العزاء والمواساة إلى عائلة الفقيد. سائلين الله أن يتغمده بواسع رحمته.', date: '', department: 'قسم الموارد البشرية' },
        { key: 'newborn', category: 'تهنئة', title: 'مبارك المولود الجديد', body: 'تتقدم الشركة بأصدق التهاني بمناسبة المولود الجديد. نسأل الله أن يجعله من مواليد السعادة.', date: '', department: 'قسم الموارد البشرية' },
        { key: 'holiday', category: 'عطلة رسمية', title: 'عطلة رسمية', body: 'تعلن إدارة الموارد البشرية أن الدوام سيكون متوقفاً يوم الخميس الموافق 15 أيار 2026', date: '15/05/2026', department: 'قسم الموارد البشرية' },
        { key: 'promotion', category: 'تهنئة', title: 'تهنئة بالترقية', body: 'نبارك الترقية ونتمنى دوام النجاح والتوفيق في المهام الجديدة، مع الشكر والتقدير للجهود المميزة.', date: '', department: 'قسم الموارد البشرية' },
        { key: 'welcome', category: 'ترحيب', title: 'ترحيب بموظف جديد', body: 'نرحب بانضمام الزميل الجديد إلى فريق العمل ونتمنى له بداية موفقة ومسيرة ناجحة.', date: '', department: 'قسم الموارد البشرية' },
        { key: 'farewell', category: 'وداع', title: 'وداع وشكر', body: 'تتقدم الشركة بالشكر والتقدير على الجهود المبذولة ونتمنى دوام التوفيق والنجاح.', date: '', department: 'قسم الموارد البشرية' }
    ];

    let index = templates.findIndex(t => t.key === 'holiday');
    if (index < 0) index = 0;

    const form = document.getElementById('nxAnnCreateForm');
    const preview = document.getElementById('nxApprovedPreviewImage');
    const select = root.querySelector('[data-ann-template-select]');

    function field(name) {
        return form ? form.querySelector(`[data-ann-field="${name}"]`) : null;
    }

    function setField(name, value, force) {
        const el = field(name);
        if (!el) return;
        if (force || !el.value || el.dataset.auto === 'true') {
            el.value = value || '';
            el.dataset.auto = 'true';
        }
    }

    function getTemplate(key) {
        return templates.find(t => t.key === key) || templates[0];
    }

    function setActive(key) {
        root.querySelectorAll('[data-ann-template-card]').forEach(card => {
            card.classList.toggle('active', card.dataset.annTemplateCard === key);
        });
        root.querySelectorAll('[data-ann-template-button]').forEach(btn => {
            btn.classList.toggle('active', btn.dataset.annTemplateButton === key);
        });
        root.querySelectorAll('.nx-dots i').forEach((dot, i) => {
            dot.classList.toggle('active', i === index);
        });
    }

    function syncInputs(key, force) {
        const template = getTemplate(key);
        const radio = root.querySelector(`input[name="Announcement.TemplateKey"][value="${key}"]`);
        if (radio) radio.checked = true;
        if (select && select.value !== key) select.value = key;

        setField('title', template.title, force);
        setField('body', template.body, force);
        setField('category', template.category, true);
        setField('date', template.date || '', force);
        setField('department', template.department, force);
    }

    function render(key, force) {
        const template = getTemplate(key);
        index = templates.findIndex(t => t.key === template.key);
        if (index < 0) index = 0;

        syncInputs(template.key, force);

        if (preview) {
            preview.src = `/brand/announcement-studio/previews/${template.key}.png`;
        }

        setActive(template.key);
    }

    root.querySelectorAll('input[name="Announcement.TemplateKey"]').forEach(radio => {
        radio.addEventListener('change', () => render(radio.value, true));
    });

    root.querySelectorAll('[data-ann-template-button]').forEach(btn => {
        btn.addEventListener('click', () => render(btn.dataset.annTemplateButton, true));
    });

    if (select) {
        select.addEventListener('change', () => render(select.value, true));
    }

    root.querySelectorAll('[data-ann-step]').forEach(btn => {
        btn.addEventListener('click', () => {
            index = btn.dataset.annStep === 'next'
                ? (index + 1) % templates.length
                : (index - 1 + templates.length) % templates.length;

            render(templates[index].key, true);
        });
    });

    root.querySelectorAll('[data-ann-field]').forEach(input => {
        input.addEventListener('input', () => {
            input.dataset.auto = 'false';
        });
    });

    const previewButton = root.querySelector('[data-ann-preview-button]');
    if (previewButton) {
        previewButton.addEventListener('click', () => {
            const key = select ? select.value : 'holiday';
            render(key, false);
        });
    }

    render('holiday', true);
})();

