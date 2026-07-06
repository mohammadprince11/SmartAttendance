
(function () {
    const root = document.getElementById('nxAnnouncementStudioPro');
    if (!root) return;

    const templates = [
        { key: 'marriage', name: 'زواج', icon: '💍', category: 'تهنئة', title: 'تهنئة بمناسبة الزواج', body: 'تتقدم الشركة بأصدق التهاني إلى {person} بمناسبة الزواج. نتمنى له حياة سعيدة وموفقة ومليئة بالمودة والرحمة.', footer: 'قسم الموارد البشرية' },
        { key: 'condolence', name: 'وفاة', icon: '🕊️', category: 'تعزية', title: 'إِنَّا لِلّهِ وَإِنَّـا إِلَيْهِ رَاجِعونَ', body: 'تتقدم الشركة بخالص العزاء والمواساة إلى {person}. سائلين الله أن يتغمد الفقيد بواسع رحمته وأن يلهم أهله وذويه الصبر والسلوان.', footer: 'قسم الموارد البشرية' },
        { key: 'newborn', name: 'مولود جديد', icon: '👶', category: 'تهنئة', title: 'مبارك المولود الجديد', body: 'تتقدم الشركة بأصدق التهاني إلى {person} بمناسبة المولود الجديد {secondary}. نسأل الله أن يجعله من مواليد السعادة والبركة.', footer: 'قسم الموارد البشرية' },
        { key: 'holiday', name: 'عطلة رسمية', icon: '📅', category: 'عطلة رسمية', title: 'عطلة رسمية', body: 'تعلن إدارة الشركة عن عطلة رسمية {date}. يرجى من جميع الموظفين الالتزام بالتعليمات الخاصة بالدوام حسب توجيهات الإدارة.', footer: 'إدارة الموارد البشرية' },
        { key: 'promotion', name: 'ترقية', icon: '📈', category: 'تهنئة', title: 'تهنئة بالترقية', body: 'نبارك إلى {person} ترقيته إلى {secondary}. نتمنى له دوام النجاح والتوفيق في مهامه الجديدة، مع الشكر والتقدير لجهوده المميزة.', footer: 'قسم الموارد البشرية' },
        { key: 'welcome', name: 'ترحيب بموظف جديد', icon: '🤝', category: 'ترحيب', title: 'ترحيب بموظف جديد', body: 'نرحب بانضمام {person} إلى فريق العمل بمنصب {secondary}. نتمنى له بداية موفقة ومسيرة ناجحة ضمن عائلة الشركة.', footer: 'قسم الموارد البشرية' },
        { key: 'farewell', name: 'وداع موظف', icon: '🧳', category: 'وداع', title: 'وداع وشكر', body: 'تتقدم الشركة بالشكر والتقدير إلى {person} على ما قدمه من جهود خلال فترة عمله. نتمنى له دوام التوفيق والنجاح في مسيرته القادمة.', footer: 'قسم الموارد البشرية' },
        { key: 'custom', name: 'إعلان مخصص', icon: '📣', category: 'عام', title: 'إعلان مخصص', body: 'اكتب نص الإعلان هنا وسيظهر في المعاينة بشكل مباشر.', footer: 'قسم الموارد البشرية' }
    ];

    let index = Math.max(0, templates.findIndex(t => t.key === 'condolence'));

    const form = document.getElementById('nxAnnCreateForm');
    const liveCard = document.getElementById('nxAnnLiveCard');
    const liveImage = document.getElementById('nxAnnLiveImage');
    const liveCategory = document.getElementById('nxAnnLiveCategory');
    const liveTitle = document.getElementById('nxAnnLiveTitle');
    const liveBody = document.getElementById('nxAnnLiveBody');
    const liveFooter = document.getElementById('nxAnnLiveFooter');
    const select = root.querySelector('[data-ann-template-select]');

    function field(name) { return form ? form.querySelector(`[data-ann-field="${name}"]`) : null; }
    function getValue(name) { const el = field(name); return el ? (el.value || '').trim() : ''; }
    function getTemplate(key) { return templates.find(t => t.key === key) || templates[templates.length - 1]; }
    function selectedKey() {
        const radio = root.querySelector('input[name="Announcement.TemplateKey"]:checked');
        return radio ? radio.value : (select ? select.value : 'custom');
    }
    function text(templateText) {
        const person = getValue('person') || 'زميلنا العزيز';
        const secondary = getValue('secondary') || 'المهمة الجديدة';
        const date = getValue('date') || 'حسب التاريخ المحدد';
        return templateText.replaceAll('{person}', person).replaceAll('{secondary}', secondary).replaceAll('{date}', date);
    }
    function setActive(key) {
        root.querySelectorAll('[data-ann-template-card]').forEach(card => card.classList.toggle('active', card.dataset.annTemplateCard === key));
        root.querySelectorAll('[data-ann-template-button]').forEach(btn => btn.classList.toggle('active', btn.dataset.annTemplateButton === key));
        root.querySelectorAll('.nxap-dots i').forEach((dot, i) => dot.classList.toggle('active', i === index));
    }
    function syncInputs(key, force) {
        const template = getTemplate(key);
        const title = field('title');
        const body = field('body');
        const category = field('category');

        if (select && select.value !== key) select.value = key;
        const radio = root.querySelector(`input[name="Announcement.TemplateKey"][value="${key}"]`);
        if (radio) radio.checked = true;

        if (category && key !== 'custom') category.value = template.category;
        if (title && key !== 'custom' && (force || !title.value.trim() || title.dataset.auto === 'true')) {
            title.value = template.title;
            title.dataset.auto = 'true';
        }
        if (body && key !== 'custom' && (force || !body.value.trim() || body.dataset.auto === 'true')) {
            body.value = text(template.body);
            body.dataset.auto = 'true';
        }
    }
    function render(key, forceText) {
        const template = getTemplate(key);
        const visible = templates.filter(t => t.key !== 'custom');
        index = visible.findIndex(t => t.key === key);
        if (index < 0) index = 0;

        syncInputs(key, forceText);

        const category = getValue('category') || template.category;
        const title = getValue('title') || template.title;
        const body = getValue('body') || text(template.body);
        const footer = getValue('department') || template.footer;

        liveCard.className = `nxap-live-card ${template.key}`;
        liveImage.src = `/brand/announcements/${template.key}-art.svg`; 
        liveImage.alt = template.name;
        liveCategory.textContent = category;
        liveTitle.textContent = title;
        liveBody.textContent = body;
        liveFooter.textContent = footer;

        setActive(template.key);
    }

    root.querySelectorAll('input[name="Announcement.TemplateKey"]').forEach(radio => radio.addEventListener('change', () => render(radio.value, true)));
    root.querySelectorAll('[data-ann-template-button]').forEach(btn => btn.addEventListener('click', () => render(btn.dataset.annTemplateButton, true)));
    if (select) select.addEventListener('change', () => render(select.value, true));

    root.querySelectorAll('[data-ann-step]').forEach(btn => {
        btn.addEventListener('click', () => {
            const visible = templates.filter(t => t.key !== 'custom');
            index = btn.dataset.annStep === 'next' ? (index + 1) % visible.length : (index - 1 + visible.length) % visible.length;
            render(visible[index].key, true);
        });
    });

    root.querySelectorAll('[data-ann-field]').forEach(input => {
        input.addEventListener('input', () => {
            if (input.dataset.annField === 'title' || input.dataset.annField === 'body') input.dataset.auto = 'false';
            const key = selectedKey();
            const body = field('body');
            if (body && body.dataset.auto === 'true' && key !== 'custom') body.value = text(getTemplate(key).body);
            render(key, false);
        });
    });

    const previewBtn = root.querySelector('[data-ann-preview-button]');
    if (previewBtn) previewBtn.addEventListener('click', () => render(selectedKey(), false));

    render('condolence', true);
})();
