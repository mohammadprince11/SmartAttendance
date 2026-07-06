
(function () {
    const root = document.getElementById('nxApprovedAnnouncementStudio');
    if (!root) return;

    const templates = [
        { key: 'marriage', category: 'تهنئة', pre: 'تهنئة رسمية', title: 'تهنئة بمناسبة الزواج', body: 'تتقدم الشركة بأصدق التهاني إلى {person} بمناسبة الزواج. نتمنى له حياة سعيدة وموفقة ومليئة بالمودة والرحمة.', date: '', department: 'قسم الموارد البشرية' },
        { key: 'condolence', category: 'تعزية', pre: 'تعزية ومواساة', title: 'إِنَّا لِلّهِ وَإِنَّا إِلَيْهِ رَاجِعونَ', body: 'تتقدم الشركة بخالص العزاء والمواساة إلى {person}. سائلين الله أن يتغمد الفقيد بواسع رحمته وأن يلهم أهله وذويه الصبر والسلوان.', date: '', department: 'قسم الموارد البشرية' },
        { key: 'newborn', category: 'تهنئة', pre: 'تهنئة مولود', title: 'مبارك المولود الجديد', body: 'تتقدم الشركة بأصدق التهاني إلى {person} بمناسبة المولود الجديد {secondary}. نسأل الله أن يجعله من مواليد السعادة والبركة.', date: '', department: 'قسم الموارد البشرية' },
        { key: 'holiday', category: 'عطلة رسمية', pre: 'بمناسبة المناسبة الوطنية', title: 'عطلة رسمية', body: 'تعلن إدارة الموارد البشرية أن الدوام سيكون متوقفاً يوم الخميس الموافق 15 أيار 2026', date: '15/05/2026', department: 'قسم الموارد البشرية' },
        { key: 'promotion', category: 'تهنئة', pre: 'تهنئة وظيفية', title: 'تهنئة بالترقية', body: 'نبارك إلى {person} ترقيته إلى {secondary}. نتمنى له دوام النجاح والتوفيق في مهامه الجديدة، مع الشكر والتقدير لجهوده المميزة.', date: '', department: 'قسم الموارد البشرية' },
        { key: 'welcome', category: 'ترحيب', pre: 'انضمام جديد', title: 'ترحيب بموظف جديد', body: 'نرحب بانضمام {person} إلى فريق العمل بمنصب {secondary}. نتمنى له بداية موفقة ومسيرة ناجحة ضمن عائلة الشركة.', date: '', department: 'قسم الموارد البشرية' },
        { key: 'farewell', category: 'وداع', pre: 'شكر وتقدير', title: 'وداع وشكر', body: 'تتقدم الشركة بالشكر والتقدير إلى {person} على ما قدمه من جهود خلال فترة عمله. نتمنى له دوام التوفيق والنجاح في مسيرته القادمة.', date: '', department: 'قسم الموارد البشرية' },
        { key: 'custom', category: 'عام', pre: 'إعلان داخلي', title: 'عنوان الإعلان المخصص', body: 'اكتب تفاصيل الإعلان هنا، وسيظهر النص في المعاينة مباشرة بدون استخدام قالب ثابت.', date: '', department: 'قسم الموارد البشرية' }
    ];

    let index = templates.findIndex(t => t.key === 'holiday');
    if (index < 0) index = 0;

    const form = document.getElementById('nxAnnCreateForm');
    const liveCard = document.getElementById('nxLiveAnnouncementCard');
    const artImage = document.getElementById('nxLiveArtImage');
    const liveCategory = document.getElementById('nxLiveCategory');
    const livePreTitle = document.getElementById('nxLivePreTitle');
    const liveTitle = document.getElementById('nxLiveTitle');
    const liveBody = document.getElementById('nxLiveBody');
    const liveDate = document.getElementById('nxLiveDate');
    const liveFooter = document.getElementById('nxLiveFooter');
    const select = root.querySelector('[data-ann-template-select]');
    const customHelp = root.querySelector('[data-ann-custom-help]');

    function field(name) {
        return form ? form.querySelector(`[data-ann-field="${name}"]`) : null;
    }

    function value(name) {
        const el = field(name);
        return el ? (el.value || '').trim() : '';
    }

    function setField(name, val, force) {
        const el = field(name);
        if (!el) return;
        if (force || !el.value.trim() || el.dataset.auto === 'true') {
            el.value = val || '';
            el.dataset.auto = 'true';
        }
    }

    function getTemplate(key) {
        return templates.find(t => t.key === key) || templates[templates.length - 1];
    }

    function selectedKey() {
        const checkedMode = root.querySelector('input[name="Announcement.UseTemplateMode"]:checked');
        if (checkedMode && checkedMode.value === 'Custom') return 'custom';

        const radio = root.querySelector('input[name="Announcement.TemplateKey"]:checked');
        return radio ? radio.value : (select ? select.value : 'holiday');
    }

    function setMode(mode) {
        const templateRadio = root.querySelector('input[name="Announcement.UseTemplateMode"][value="Template"]');
        const customRadio = root.querySelector('input[name="Announcement.UseTemplateMode"][value="Custom"]');

        if (mode === 'Custom') {
            if (customRadio) customRadio.checked = true;
            if (select) select.value = 'custom';
            root.querySelectorAll('input[name="Announcement.TemplateKey"]').forEach(r => r.checked = false);
            if (customHelp) customHelp.classList.add('is-visible');
            root.querySelectorAll('[data-ann-template-card]').forEach(card => {
                card.classList.remove('active');
                card.classList.add('is-muted');
            });
        } else {
            if (templateRadio) templateRadio.checked = true;
            if (customHelp) customHelp.classList.remove('is-visible');
            root.querySelectorAll('[data-ann-template-card]').forEach(card => card.classList.remove('is-muted'));
        }
    }

    function text(templateText) {
        const person = value('person') || 'زميلنا العزيز';
        const secondary = value('secondary') || 'المهمة الجديدة';
        return templateText
            .replaceAll('{person}', person)
            .replaceAll('{secondary}', secondary);
    }

    function setActive(key) {
        const isCustom = key === 'custom';

        root.querySelectorAll('[data-ann-template-card]').forEach(card => {
            card.classList.toggle('active', !isCustom && card.dataset.annTemplateCard === key);
            card.classList.toggle('is-muted', isCustom);
        });

        root.querySelectorAll('[data-ann-template-button]').forEach(btn => {
            btn.classList.toggle('active', !isCustom && btn.dataset.annTemplateButton === key);
        });

        root.querySelectorAll('.nx-dots i').forEach((dot, i) => {
            dot.classList.toggle('active', !isCustom && i === index);
        });
    }

    function syncInputs(key, force) {
        const template = getTemplate(key);

        if (key === 'custom') {
            setMode('Custom');
            if (select) select.value = 'custom';
            setField('category', value('category') || 'عام', false);
            setField('department', value('department') || template.department, false);
            return;
        }

        setMode('Template');

        const radio = root.querySelector(`input[name="Announcement.TemplateKey"][value="${key}"]`);
        if (radio) radio.checked = true;
        if (select && select.value !== key) select.value = key;

        setField('title', template.title, force);
        setField('body', text(template.body), force);
        setField('category', template.category, true);
        setField('date', template.date || '', force);
        setField('department', template.department, force);
    }

    function render(key, force) {
        const template = getTemplate(key);
        const isCustom = template.key === 'custom';

        if (!isCustom) {
            index = templates.filter(t => t.key !== 'custom').findIndex(t => t.key === template.key);
            if (index < 0) index = 0;
        }

        syncInputs(template.key, force);

        liveCard.className = `nx-live-card ${template.key}`;
        artImage.src = `/brand/announcement-studio/art/${template.key}.png`;
        artImage.alt = template.key;

        liveCategory.textContent = value('category') || template.category;
        livePreTitle.textContent = isCustom ? 'إعلان مخصص' : (template.pre || template.category);

        liveTitle.textContent = value('title') || template.title;
        liveBody.textContent = value('body') || template.body;
        liveDate.textContent = value('date') || '';
        liveDate.style.display = (value('date') || '').trim() ? '' : 'none';
        liveFooter.textContent = value('department') || template.department;

        setActive(template.key);
    }

    root.querySelectorAll('input[name="Announcement.TemplateKey"]').forEach(radio => {
        radio.addEventListener('change', () => {
            setMode('Template');
            render(radio.value, true);
        });
    });

    root.querySelectorAll('[data-ann-template-button]').forEach(btn => {
        btn.addEventListener('click', () => {
            setMode('Template');
            render(btn.dataset.annTemplateButton, true);
        });
    });

    root.querySelectorAll('input[name="Announcement.UseTemplateMode"]').forEach(mode => {
        mode.addEventListener('change', () => {
            if (mode.value === 'Custom') {
                render('custom', false);
            } else {
                const nextKey = select && select.value && select.value !== 'custom' ? select.value : 'holiday';
                render(nextKey, true);
            }
        });
    });

    if (select) {
        select.addEventListener('change', () => {
            if (select.value === 'custom') {
                render('custom', false);
            } else {
                setMode('Template');
                render(select.value, true);
            }
        });
    }

    root.querySelectorAll('[data-ann-step]').forEach(btn => {
        btn.addEventListener('click', () => {
            const visible = templates.filter(t => t.key !== 'custom');
            index = btn.dataset.annStep === 'next'
                ? (index + 1) % visible.length
                : (index - 1 + visible.length) % visible.length;

            render(visible[index].key, true);
        });
    });

    root.querySelectorAll('[data-ann-field]').forEach(input => {
        input.addEventListener('input', () => {
            if (input.dataset.annField === 'title' || input.dataset.annField === 'body' || input.dataset.annField === 'date' || input.dataset.annField === 'department' || input.dataset.annField === 'category') {
                input.dataset.auto = 'false';
            }
            render(selectedKey(), false);
        });
    });

    const previewButton = root.querySelector('[data-ann-preview-button]');
    if (previewButton) {
        previewButton.addEventListener('click', () => render(selectedKey(), false));
    }

    render('holiday', true);
})();


(function(){
    const root = document.getElementById('nxEmployeePulseStudio');
    if(!root) return;

    const templates = {
        satisfaction: {
            title: 'استطلاع رضا الموظفين - مايو 2026',
            question: 'ما مدى رضاك عن بيئة العمل في شركتنا؟',
            options: ['غير راض إطلاقاً','غير راض','محايد','راض','راض جداً'],
            category: 'استطلاع',
            label: 'رضا الموظفين',
            privacy: 'anonymous'
        },
        workplace: {
            title: 'تقييم بيئة العمل',
            question: 'كيف تقيم بيئة العمل من حيث الراحة والتنظيم والتعاون؟',
            options: ['ضعيفة','مقبولة','جيدة','جيدة جداً','ممتازة'],
            category: 'نبض الموظفين',
            label: 'بيئة العمل',
            privacy: 'anonymous'
        },
        supervisor: {
            title: 'تقييم المشرف المباشر',
            question: 'كيف تقيم تواصل ودعم المشرف المباشر؟',
            options: ['ضعيف','مقبول','جيد','جيد جداً','ممتاز'],
            category: 'استطلاع',
            label: 'تقييم المشرف',
            privacy: 'anonymous'
        },
        transport: {
            title: 'تقييم النقل والسكن',
            question: 'ما مدى رضاك عن خدمات النقل والسكن؟',
            options: ['غير راض','أحتاج متابعة','محايد','راض','راض جداً'],
            category: 'استطلاع',
            label: 'النقل والسكن',
            privacy: 'anonymous'
        },
        meals: {
            title: 'تقييم الوجبات',
            question: 'كيف تقيم جودة الوجبات والخدمات الغذائية؟',
            options: ['ضعيفة','مقبولة','جيدة','جيدة جداً','ممتازة'],
            category: 'استطلاع',
            label: 'الوجبات',
            privacy: 'anonymous'
        },
        employee_month: {
            title: 'انتخاب موظف الشهر',
            question: 'من هو الموظف الذي ترشحه كموظف الشهر؟',
            options: ['الموظف الأول','الموظف الثاني','الموظف الثالث','الموظف الرابع'],
            category: 'انتخاب',
            label: 'موظف الشهر',
            privacy: 'visible'
        },
        ideas: {
            title: 'اقتراحات تحسين',
            question: 'ما أكثر مجال يحتاج إلى تحسين من وجهة نظرك؟',
            options: ['جدول الدوام','التواصل الداخلي','النقل','الوجبات','فرص التطوير'],
            category: 'تصويت داخلي',
            label: 'اقتراحات تحسين',
            privacy: 'anonymous'
        },
        custom: {
            title: '',
            question: '',
            options: ['الخيار الأول','الخيار الثاني'],
            category: 'استطلاع',
            label: 'تصميم مخصص',
            privacy: 'anonymous'
        }
    };

    const form = document.getElementById('nxPulseForm');
    const q = document.getElementById('nxPulseQuestion');
    const desc = document.getElementById('nxPulseDescription');
    const faces = document.getElementById('nxPulseFaces');
    const step = document.getElementById('nxPulseStep');

    function field(name){
        return form ? form.querySelector(`[data-pulse-field="${name}"]`) : null;
    }

    function setValue(name, value){
        const el = field(name);
        if(el) el.value = value || '';
    }

    function value(name){
        const el = field(name);
        return el ? (el.value || '').trim() : '';
    }

    function currentOptions(){
        return (value('options') || '').split(/\r?\n/).map(x => x.trim()).filter(Boolean);
    }

    function iconFor(i, total){
        if(total === 5){
            return ['😡','☹️','😐','🙂','😀'][i] || '•';
        }
        return ['①','②','③','④','⑤','⑥','⑦','⑧'][i] || '•';
    }

    function renderPreview(){
        const question = value('question') || 'اكتب سؤال الاستطلاع';
        const options = currentOptions();
        const privacy = value('privacy');

        q.textContent = question;
        desc.textContent = privacy === 'visible'
            ? 'هذا الاستطلاع ظاهر للإدارة بالأسماء'
            : 'هذا الاستطلاع سري ولا تظهر أسماء في النتائج';

        faces.innerHTML = '';
        options.slice(0, 5).forEach((opt, i) => {
            const btn = document.createElement('button');
            btn.type = 'button';
            btn.innerHTML = `<strong>${iconFor(i, options.length)}</strong><small>${opt}</small>`;
            faces.appendChild(btn);
        });

        step.textContent = 'سؤال 1/1';
    }

    function applyTemplate(key){
        const t = templates[key] || templates.satisfaction;
        root.querySelectorAll('.nx-pulse-template').forEach(card => {
            card.classList.toggle('active', card.dataset.pulseTemplate === key);
            const input = card.querySelector('input');
            if(input) input.checked = card.dataset.pulseTemplate === key;
        });

        setValue('title', t.title);
        setValue('question', t.question);
        setValue('options', t.options.join('\n'));
        setValue('category', t.category);
        setValue('label', t.label);
        setValue('privacy', t.privacy);
        renderPreview();
    }

    root.querySelectorAll('.nx-pulse-template').forEach(card => {
        card.addEventListener('click', () => applyTemplate(card.dataset.pulseTemplate));
    });

    root.querySelectorAll('[data-pulse-field]').forEach(el => {
        el.addEventListener('input', renderPreview);
        el.addEventListener('change', renderPreview);
    });

    const customMode = form ? form.querySelector('input[name="PulseMode"][value="custom"]') : null;
    const templateMode = form ? form.querySelector('input[name="PulseMode"][value="template"]') : null;

    if(customMode){
        customMode.addEventListener('change', () => {
            if(customMode.checked) applyTemplate('custom');
        });
    }
    if(templateMode){
        templateMode.addEventListener('change', () => {
            if(templateMode.checked) applyTemplate('satisfaction');
        });
    }

    applyTemplate('satisfaction');
})();
