namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// كتالوج معايير الشروط (نظير <c>ConditionCriteriaID</c> بكيان — 36 معياراً مصنَّفاً).
///
/// كل معيار هنا **مسنود بعمود حقيقي** بقاعدتنا يقرؤه <see cref="HrConditionFacts"/>؛
/// لا معيار «تخطيطي» بلا مصدر، لأن معياراً بلا حقيقة يعني شرطاً لا يتحقّق أبداً
/// ويظهر للمستخدم كعطل صامت (وهو أسوأ من غياب المعيار).
///
/// المعايير المؤجَّلة عمداً وأسبابها:
/// <list type="bullet">
/// <item><b>«في فترة التجربة»</b> — دائريّ: أول استعمال للمحرّك هو حسم سياسة فترة
/// التجربة نفسها، فمعيارٌ يعتمد على نتيجتها يفتح حلقة. يُضاف بعد استقرار الحاسبة.</item>
/// <item><b>«مجموعات الموظفين»</b> — <c>EmployeeGroups</c> عندنا استعلامٌ محفوظ
/// بثلاثة شروط ثابتة؛ الصحيح ترحيلها لتصير هي نفسها مجموعةَ شروط، لا جعلها معياراً
/// داخل المحرّك فتتداخل الطبقتان.</item>
/// <item><b>«الموقع» و«مراكز التكلفة»</b> — لا جدول مؤكَّد عندنا بعد.</item>
/// </list>
/// </summary>
public static class HrConditionCatalog
{
    public sealed record Criterion(
        string Key,
        string Label,
        HrConditions.ValueKind Kind,
        string Category,
        /// <summary>مصدر القائمة للعرض: فئة <see cref="HrLookups"/> أو اسم جدول مرجعي.</summary>
        string? Source = null);

    public const string CategoryOrganization = "تنظيمية";
    public const string CategoryPersonal = "شخصية";
    public const string CategoryContract = "تعاقدية";
    public const string CategoryFinancial = "مالية";
    public const string CategoryIdentity = "تحديد أفراد";

    /// <summary>بادئة معيار مبنيّ على حقل إضافي معرَّف بـ<c>EmployeeCustomFields</c>.</summary>
    public const string CustomFieldPrefix = "custom:";

    /// <summary>
    /// المعايير الثابتة. الحقول الإضافية تُضاف ديناميكياً بـ<see cref="WithCustomFields"/>
    /// — نفس سلوك كيان: تعرّف حقلاً إضافياً فيصير فوراً معياراً متاحاً للشروط.
    /// </summary>
    public static readonly IReadOnlyList<Criterion> Fixed = new List<Criterion>
    {
        // تنظيمية
        new("branch",           "الفرع / وحدة الأعمال", HrConditions.ValueKind.Reference, CategoryOrganization, "Branches"),
        new("department",       "القسم",                HrConditions.ValueKind.Reference, CategoryOrganization, "Departments"),
        new("company",          "الشركة",               HrConditions.ValueKind.Reference, CategoryOrganization, "Companies"),
        new("position",         "المسمى الوظيفي",       HrConditions.ValueKind.Reference, CategoryOrganization, "Positions"),
        new("manager",          "المدير المباشر",       HrConditions.ValueKind.Reference, CategoryOrganization, "Employees"),

        // شخصية
        new("gender",           "الجنس",                HrConditions.ValueKind.Lookup, CategoryPersonal, "genders"),
        new("maritalstatus",    "الحالة الاجتماعية",    HrConditions.ValueKind.Lookup, CategoryPersonal, "maritalstatuses"),
        new("religion",         "الديانة",              HrConditions.ValueKind.Lookup, CategoryPersonal, "religions"),
        new("nationality",      "الجنسية",              HrConditions.ValueKind.Lookup, CategoryPersonal, "nationalities"),
        new("country",          "البلد",                HrConditions.ValueKind.Text,   CategoryPersonal),
        new("age",              "العمر (سنة)",          HrConditions.ValueKind.Number, CategoryPersonal),
        // ⭐ «مواطن» معيار مستقل بكيان، وهو أساس اختلاف الضمان والإقامة والكفيل.
        new("iscitizen",        "مواطن",                HrConditions.ValueKind.Bool,   CategoryPersonal),

        // تعاقدية
        new("contracttype",     "نوع العقد",            HrConditions.ValueKind.Lookup, CategoryContract, "contracttypes"),
        new("worktype",         "نوع الدوام",           HrConditions.ValueKind.Lookup, CategoryContract, "worktypes"),
        new("jobgrade",         "طبقة العمل / الدرجة",  HrConditions.ValueKind.Lookup, CategoryContract, "grades"),
        new("employmentstatus", "حالة التوظيف",         HrConditions.ValueKind.Text,   CategoryContract),
        new("isactive",         "على رأس العمل",        HrConditions.ValueKind.Bool,   CategoryContract),
        new("hiredate",         "تاريخ التعيين",        HrConditions.ValueKind.Date,   CategoryContract),
        new("joiningdate",      "تاريخ الانضمام",       HrConditions.ValueKind.Date,   CategoryContract),
        new("contractenddate",  "تاريخ انتهاء العقد",   HrConditions.ValueKind.Date,   CategoryContract),
        new("servicemonths",    "مدة الخدمة (شهر)",     HrConditions.ValueKind.Number, CategoryContract),

        new("sponsor",          "الكفيل",               HrConditions.ValueKind.Text,   CategoryContract),

        // مالية — ⭐ غيابها هو ما يجعل شروطنا اليوم عاجزة عن «الراتب أقل من كذا»
        new("basicsalary",      "الراتب الأساسي",       HrConditions.ValueKind.Number, CategoryFinancial),
        new("allowances",       "مجموع العلاوات",       HrConditions.ValueKind.Number, CategoryFinancial),
        new("grosssalary",      "الراتب الكلي",         HrConditions.ValueKind.Number, CategoryFinancial),
        new("salaryscale",      "سلم الرواتب",          HrConditions.ValueKind.Text,   CategoryFinancial),
        new("gosiType",         "نوع الضمان",           HrConditions.ValueKind.Text,   CategoryFinancial),
        new("bank",             "البنك",                HrConditions.ValueKind.Text,   CategoryFinancial),

        // تحديد أفراد
        new("employee",         "الموظف (بالمعرّف)",    HrConditions.ValueKind.Reference, CategoryIdentity, "Employees"),
        new("employeecode",     "رقم الموظف",           HrConditions.ValueKind.Text,     CategoryIdentity)
    };

    /// <summary>الترتيب المعروض بالمنسدلة: مصنَّف كما بكيان لا أبجدياً.</summary>
    public static readonly IReadOnlyList<string> Categories = new[]
    {
        CategoryOrganization, CategoryPersonal, CategoryContract, CategoryFinancial, CategoryIdentity
    };

    public static Criterion? Find(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        // حقل إضافي: نوعه دائماً نصّ لأن EmployeeCustomFields يخزّن nvarchar(max).
        if (key.StartsWith(CustomFieldPrefix, StringComparison.Ordinal))
        {
            var fieldKey = key[CustomFieldPrefix.Length..];
            return fieldKey.Length == 0
                ? null
                : new Criterion(key, fieldKey, HrConditions.ValueKind.Text, "حقول إضافية");
        }

        return Fixed.FirstOrDefault(criterion =>
            string.Equals(criterion.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>الكتالوج الكامل لهذه الشركة = الثابت + الحقول الإضافية المعرَّفة فعلاً.</summary>
    public static IReadOnlyList<Criterion> WithCustomFields(IEnumerable<(string Key, string Label)> customFields)
    {
        var all = Fixed.ToList();

        foreach (var (key, label) in customFields)
        {
            if (string.IsNullOrWhiteSpace(key)) continue;

            all.Add(new Criterion(
                CustomFieldPrefix + key,
                string.IsNullOrWhiteSpace(label) ? key : label,
                HrConditions.ValueKind.Text,
                "حقول إضافية"));
        }

        return all;
    }
}
