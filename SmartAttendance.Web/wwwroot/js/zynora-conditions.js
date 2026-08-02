/*
 * باني الشروط — مكوّن واجهة عام لمحرّك HrConditions.
 *
 * نظير lstGroupConditions بكيان، بسلوكه المرصود حياً:
 *  - «مجموعة قواعد جديدة» ⟵ داخلها «قاعدة جديدة»؛ AND داخل المجموعة، OR بينها.
 *  - **محرّر القيمة يُشتقّ من نوع المعيار** (اختيار «الراتب الأساسي» يحوّل الحقل
 *    إلى رقم فوراً) — هذا ما رصدناه بالفحص الحيّ وهو جوهر بساطة الشاشة عندهم.
 *
 * وتحسينان عن كيان:
 *  - العوامل تُرشَّح بنوع المعيار (لا «أكبر من» على نصّ) بدل عرض الستّة دائماً.
 *  - سطر ملخّص عربي مقروء تحت الباني، فيُفهم الشرط بلا قراءة الحقول.
 *
 * العقد: حقل مخفيّ يحمل JSON مطابقاً لـHrConditions.ConditionSet، يُحدَّث بكل تغيير.
 * بلا اعتماديات خارجية — نفس نهج بقية سكربتات المشروع.
 */
(function () {
    'use strict';

    var OPERATORS = [
        { value: 'Equal', label: 'يساوي' },
        { value: 'NotEqual', label: 'لا يساوي' },
        { value: 'LessThan', label: 'أقل من' },
        { value: 'GreaterThan', label: 'أكبر من' },
        { value: 'LessOrEqual', label: 'أقل من أو يساوي' },
        { value: 'GreaterOrEqual', label: 'أكبر من أو يساوي' },
        { value: 'In', label: 'ضمن' },
        { value: 'NotIn', label: 'ليس ضمن' }
    ];

    // مطابق لـHrConditions.SupportsOperator — أي اختلاف يعني قاعدةً تُبنى بالواجهة
    // ولا يقبلها المحرّك، فتُحفظ ولا تنطبق أبداً.
    function allowedOperators(kind) {
        switch (kind) {
            case 'Number':
            case 'Date':
                return OPERATORS;
            case 'Bool':
                return OPERATORS.filter(function (o) { return o.value === 'Equal' || o.value === 'NotEqual'; });
            default:
                return OPERATORS.filter(function (o) {
                    return ['Equal', 'NotEqual', 'In', 'NotIn'].indexOf(o.value) >= 0;
                });
        }
    }

    var sharedCache = null;

    /// الكتالوج المبثوث مرّةً واحدة بالصفحة — يُقرأ ويُخزَّن فلا يُحلَّل مرّتين.
    function sharedCriteria() {
        if (sharedCache !== null) return sharedCache;
        var node = document.getElementById('zy-criteria-catalog');
        sharedCache = node ? node.textContent : '';
        return sharedCache;
    }

    function el(tag, className, text) {
        var node = document.createElement(tag);
        if (className) node.className = className;
        if (text != null) node.textContent = text;
        return node;
    }

    function ZyConditions(root) {
        this.root = root;
        this.input = document.querySelector(root.getAttribute('data-input'));
        // الكتالوج مشتركٌ بالصفحة لا منسوخٌ بكل باني.
        //
        // كان كل عنصر يحمل `data-criteria` بنفسه: بشاشةٍ فيها 79 صفاً صار الكتالوج
        // (4KB) مكرّراً 79 مرّة = **336KB من نفس النصّ**، وقِيست الصفحة 1.3MB.
        // الآن يُبثّ مرّةً واحدة بوسم `<script>` ويقرؤه الجميع، ويبقى
        // `data-criteria` مدعوماً للشاشات التي تحمل باني واحد.
        this.criteria = JSON.parse(
            root.getAttribute('data-criteria') || sharedCriteria() || '[]');

        // معنى «بلا شروط» يختلف بحسب المستدعي — نسخة سياسة لا تنطبق على أحد،
        // بينما مخالفة بلا شروط تنطبق على الجميع. النصّ المثبَّت كان يكذب على
        // نصف الشاشات، فصار من الوسم وافتراضه سلوك النسخة كما كان.
        this.emptyText = root.getAttribute('data-empty-text')
            || 'بلا شروط — لن تنطبق هذه النسخة على أحد.';
        this.byKey = {};
        for (var i = 0; i < this.criteria.length; i++) this.byKey[this.criteria[i].key] = this.criteria[i];

        this.state = this.read();
        this.build();
        this.render();
    }

    ZyConditions.prototype.read = function () {
        if (!this.input || !this.input.value) return { groups: [] };
        try {
            var parsed = JSON.parse(this.input.value);
            return parsed && Array.isArray(parsed.groups) ? parsed : { groups: [] };
        } catch (e) {
            return { groups: [] };
        }
    };

    ZyConditions.prototype.build = function () {
        this.root.innerHTML = '';
        this.list = el('div', 'zyc-groups');
        this.root.appendChild(this.list);

        var addGroup = el('button', 'zyc-add-group', '+ مجموعة قواعد جديدة');
        addGroup.type = 'button';
        var self = this;
        addGroup.addEventListener('click', function () {
            self.state.groups.push({ rules: [self.blankRule()] });
            self.render();
        });
        this.root.appendChild(addGroup);

        this.summary = el('p', 'zyc-summary');
        this.root.appendChild(this.summary);
    };

    ZyConditions.prototype.blankRule = function () {
        var first = this.criteria.length ? this.criteria[0].key : '';
        return { criterion: first, op: 'Equal', value: '' };
    };

    ZyConditions.prototype.render = function () {
        var self = this;
        this.list.innerHTML = '';

        if (!this.state.groups.length) {
            this.list.appendChild(el('p', 'zyc-empty', 'بلا شروط — أضف مجموعة قواعد لتحديد من تنطبق عليه.'));
        }

        this.state.groups.forEach(function (group, groupIndex) {
            if (groupIndex > 0) self.list.appendChild(el('div', 'zyc-or', 'أو'));
            self.list.appendChild(self.renderGroup(group, groupIndex));
        });

        this.sync();
    };

    ZyConditions.prototype.renderGroup = function (group, groupIndex) {
        var self = this;
        var box = el('div', 'zyc-group');

        var head = el('div', 'zyc-group-head');
        head.appendChild(el('span', 'zyc-group-title', 'مجموعة قواعد ' + (groupIndex + 1)));

        var drop = el('button', 'zyc-icon zyc-danger', '×');
        drop.type = 'button';
        drop.title = 'حذف المجموعة';
        drop.addEventListener('click', function () {
            self.state.groups.splice(groupIndex, 1);
            self.render();
        });
        head.appendChild(drop);
        box.appendChild(head);

        var rules = el('div', 'zyc-rules');
        group.rules.forEach(function (rule, ruleIndex) {
            if (ruleIndex > 0) rules.appendChild(el('div', 'zyc-and', 'و'));
            rules.appendChild(self.renderRule(group, rule, ruleIndex));
        });
        box.appendChild(rules);

        var addRule = el('button', 'zyc-add-rule', '+ قاعدة جديدة');
        addRule.type = 'button';
        addRule.addEventListener('click', function () {
            group.rules.push(self.blankRule());
            self.render();
        });
        box.appendChild(addRule);

        return box;
    };

    ZyConditions.prototype.renderRule = function (group, rule, ruleIndex) {
        var self = this;
        var row = el('div', 'zyc-rule');

        // المعيار — مصنَّف بمجموعات كما بكيان لا قائمةً مسطّحة من 26 بنداً.
        var criterion = el('select', 'zyc-criterion');
        var categories = [];
        this.criteria.forEach(function (c) {
            if (categories.indexOf(c.category) < 0) categories.push(c.category);
        });
        categories.forEach(function (category) {
            var group2 = document.createElement('optgroup');
            group2.label = category;
            self.criteria.filter(function (c) { return c.category === category; }).forEach(function (c) {
                var option = document.createElement('option');
                option.value = c.key;
                option.textContent = c.label;
                if (c.key === rule.criterion) option.selected = true;
                group2.appendChild(option);
            });
            criterion.appendChild(group2);
        });
        criterion.addEventListener('change', function () {
            rule.criterion = criterion.value;
            // نوع المحرّر تغيّر ⟹ القيمة السابقة قد تكون بلا معنى (نصّ بحقل رقم).
            rule.value = '';
            var allowed = allowedOperators(self.kindOf(rule.criterion)).map(function (o) { return o.value; });
            if (allowed.indexOf(rule.op) < 0) rule.op = allowed[0];
            self.render();
        });
        row.appendChild(criterion);

        // العامل — مرشَّح بنوع المعيار.
        var op = el('select', 'zyc-op');
        allowedOperators(this.kindOf(rule.criterion)).forEach(function (o) {
            var option = document.createElement('option');
            option.value = o.value;
            option.textContent = o.label;
            if (o.value === rule.op) option.selected = true;
            op.appendChild(option);
        });
        op.addEventListener('change', function () {
            rule.op = op.value;
            self.render();
        });
        row.appendChild(op);

        row.appendChild(this.renderValueEditor(rule));

        var remove = el('button', 'zyc-icon zyc-danger', '×');
        remove.type = 'button';
        remove.title = 'حذف القاعدة';
        remove.addEventListener('click', function () {
            group.rules.splice(ruleIndex, 1);
            self.render();
        });
        row.appendChild(remove);

        return row;
    };

    ZyConditions.prototype.kindOf = function (key) {
        var criterion = this.byKey[key];
        return criterion ? criterion.kind : 'Text';
    };

    /** محرّر القيمة يُشتقّ من نوع المعيار — السلوك المرصود حياً بكيان. */
    ZyConditions.prototype.renderValueEditor = function (rule) {
        var self = this;
        var criterion = this.byKey[rule.criterion];
        var kind = criterion ? criterion.kind : 'Text';
        var options = criterion && criterion.options ? criterion.options : null;
        var multi = rule.op === 'In' || rule.op === 'NotIn';
        var node;

        if (kind === 'Bool') {
            node = el('select', 'zyc-value');
            [['true', 'نعم'], ['false', 'لا']].forEach(function (pair) {
                var option = document.createElement('option');
                option.value = pair[0];
                option.textContent = pair[1];
                if (pair[0] === rule.value) option.selected = true;
                node.appendChild(option);
            });
            if (!rule.value) rule.value = 'true';
        } else if (options && options.length && !multi) {
            // قائمة معرَّفة بالنظام ⟹ منسدلة لا كتابة حرّة: قيمة مكتوبة بيدٍ لا
            // تطابق أي صفّ تعني شرطاً صامتاً لا يتحقّق أبداً.
            node = el('select', 'zyc-value');
            var blank = document.createElement('option');
            blank.value = '';
            blank.textContent = '— اختر —';
            node.appendChild(blank);
            options.forEach(function (item) {
                var option = document.createElement('option');
                option.value = item.value;
                option.textContent = item.label;
                if (String(item.value) === String(rule.value)) option.selected = true;
                node.appendChild(option);
            });
        } else if (multi) {
            node = el('input', 'zyc-value');
            node.type = 'text';
            node.value = rule.value || '';
            node.placeholder = 'قيم مفصولة بفاصلة';
        } else if (kind === 'Date') {
            node = el('input', 'zyc-value');
            node.type = 'date';
            node.value = rule.value || '';
        } else if (kind === 'Number' || kind === 'Reference') {
            node = el('input', 'zyc-value');
            node.type = 'number';
            node.step = 'any';
            node.value = rule.value || '';
        } else {
            node = el('input', 'zyc-value');
            node.type = 'text';
            node.value = rule.value || '';
        }

        node.addEventListener('change', function () {
            rule.value = node.value;
            self.sync();
        });
        node.addEventListener('input', function () {
            rule.value = node.value;
            self.sync();
        });

        return node;
    };

    ZyConditions.prototype.describe = function () {
        var self = this;
        var groups = this.state.groups.filter(function (g) { return g.rules.length; });
        if (!groups.length) return this.emptyText;

        return groups.map(function (group) {
            return '(' + group.rules.map(function (rule) {
                var criterion = self.byKey[rule.criterion];
                var name = criterion ? criterion.label : rule.criterion;
                var op = OPERATORS.filter(function (o) { return o.value === rule.op; })[0];
                var value = rule.value;
                if (criterion && criterion.options) {
                    var match = criterion.options.filter(function (i) { return String(i.value) === String(value); })[0];
                    if (match) value = match.label;
                }
                return name + ' ' + (op ? op.label : rule.op) + ' ' + (value || '…');
            }).join(' و ') + ')';
        }).join(' أو ');
    };

    ZyConditions.prototype.sync = function () {
        // مجموعة بلا قواعد تُطرح قبل الحفظ: المحرّك يتجاهلها، لكن حفظها يترك
        // للمستخدم صفوفاً فارغة تعود كلما فتح النموذج.
        var clean = { groups: this.state.groups.filter(function (g) { return g.rules.length; }) };
        if (this.input) this.input.value = clean.groups.length ? JSON.stringify(clean) : '';
        if (this.summary) this.summary.textContent = this.describe();
    };

    function mount(node) {
        if (!node || node.__zyConditions) return;
        node.__zyConditions = new ZyConditions(node);
    }

    /// <summary>
    /// ⚠️ **`data-zy-lazy` لا يُبنى عند التحميل.**
    ///
    /// بناء 79 بانياً بشاشة اللائحة كان يصنع مئات العُقد وعشرات المنسدلات قبل أن
    /// يطلب المستخدم شرطاً واحداً. الشرط استثناءٌ لا قاعدة — فالصفّ يعرض ملخّصه
    /// نصّاً، ويُبنى الباني عند أوّل نقرةٍ عليه.
    /// </summary>
    function init(scope) {
        (scope || document)
            .querySelectorAll('[data-zy-conditions]:not([data-zy-lazy])')
            .forEach(mount);
    }

    // فتح باني الشروط عند الطلب — النقرة الأولى تبنيه ثم تُظهره.
    document.addEventListener('click', function (event) {
        var toggle = event.target.closest('[data-zy-conditions-open]');
        if (!toggle) return;

        var host = document.querySelector(toggle.getAttribute('data-zy-conditions-open'));
        if (!host) return;

        mount(host);
        host.hidden = !host.hidden;
        toggle.setAttribute('aria-expanded', host.hidden ? 'false' : 'true');
    });

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () { init(document); });
    } else {
        init(document);
    }

    window.ZyConditions = { init: init, mount: mount };
})();
