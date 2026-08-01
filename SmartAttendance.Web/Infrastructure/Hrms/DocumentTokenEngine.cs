using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// محرّك رموز الدمج (نظير رموز `{Employee-Code}` بمنشئ وثائق كيان) — منطق نقيّ.
///
/// الآلية: يكتب المستخدم `{رمز}` داخل نصّ القالب، فيُستبدل بقيمة الموظف عند التوليد.
///
/// ⚠️ <b>كل قيمة تُدرَج تُهرَّب HTML إجبارياً.</b> القالب HTML، والقيم تأتي من
/// بيانات يحرّرها بشر (اسم موظف فيه <c>&lt;</c> أو ملاحظة فيها وسم) — إدراجها خاماً
/// يكسر الوثيقة بأحسن الأحوال ويحقن سكربتاً بأسوئها. التهريب هنا لا خيار.
///
/// ⚠️ <b>الرمز المجهول يبقى كما هو ولا يُمحى.</b> محوُه بصمت يُخرج وثيقةً ناقصة
/// تبدو سليمة — والكاتب لا يكتشف خطأه الإملائي أبداً. وبقاؤه ظاهراً يفضح نفسه،
/// و<see cref="Render"/> يُرجع قائمة غير المحلولة ليعرضها المولّد قبل الاعتماد.
/// </summary>
public static class DocumentTokenEngine
{
    public sealed record Token(string Key, string Label, string Category);

    public const string CategoryIdentity = "هوية الموظف";
    public const string CategoryJob = "التوظيف";
    public const string CategoryContract = "العقد";
    public const string CategoryFinancial = "المالية";
    public const string CategoryCompany = "الشركة";
    public const string CategoryDocument = "الوثيقة";

    /// <summary>بادئة رموز الحقول الإضافية — تصير رموزاً تلقائياً كما بكيان.</summary>
    public const string CustomPrefix = "Custom-";

    public static readonly IReadOnlyList<Token> Catalog = new List<Token>
    {
        new("Employee-Code",        "رقم الموظف",              CategoryIdentity),
        new("Employee-FullName",    "الاسم الكامل",            CategoryIdentity),
        new("Employee-FullName-En", "الاسم الكامل — إنجليزي",  CategoryIdentity),
        new("Employee-FirstName",   "الاسم الأول",             CategoryIdentity),
        new("Employee-LastName",    "الاسم الأخير",            CategoryIdentity),
        new("National-Id",          "الرقم الوطني",            CategoryIdentity),
        new("Passport-No",          "رقم الجواز",              CategoryIdentity),
        new("Nationality",          "الجنسية",                 CategoryIdentity),
        new("Gender",               "الجنس",                   CategoryIdentity),
        new("Religion",             "الديانة",                 CategoryIdentity),
        new("Marital-Status",       "الحالة الاجتماعية",       CategoryIdentity),
        new("Birth-Date",           "تاريخ الميلاد",           CategoryIdentity),
        new("Age",                  "العمر",                   CategoryIdentity),
        new("Is-Citizen",           "مواطن",                   CategoryIdentity),
        // ⚠️ **رابط** لا وسم صورة: قيم الرموز تُهرَّب HTML عمداً (Render)، وإرجاع
        // <img> هنا يعني فتح باب حقن بقالبٍ يحرّره المستخدم. كاتب القالب يضعه
        // بنفسه: <img src="{Employee-Signature-Url}"> — فيُهرَّب الرابط داخل السمة بأمان.
        new("Employee-Signature-Url", "رابط توقيع الموظف",     CategoryIdentity),
        new("Phone",                "الهاتف",                  CategoryIdentity),
        new("Email",                "البريد الإلكتروني",       CategoryIdentity),

        new("Position",             "المسمى الوظيفي",          CategoryJob),
        new("Department",           "القسم",                   CategoryJob),
        new("Branch",               "الفرع / وحدة العمل",      CategoryJob),
        new("Direct-Manager",       "المدير المباشر",          CategoryJob),
        new("Job-Grade",            "طبقة العمل",              CategoryJob),
        new("Work-Type",            "نوع الدوام",              CategoryJob),
        new("Employment-Status",    "حالة التوظيف",            CategoryJob),
        new("Hire-Date",            "تاريخ التعيين",           CategoryJob),
        new("Joining-Date",         "تاريخ الانضمام",          CategoryJob),
        new("Service-Period",       "مدة الخدمة",              CategoryJob),
        new("Service-Months",       "مدة الخدمة (شهر)",        CategoryJob),

        new("Contract-Type",        "نوع العقد",               CategoryContract),
        new("Contract-No",          "رقم العقد",               CategoryContract),
        new("Contract-Start",       "تاريخ بدء العقد",         CategoryContract),
        new("Contract-End",         "تاريخ انتهاء العقد",      CategoryContract),
        new("Sponsor",              "الكفيل",                  CategoryContract),

        new("Basic-Salary",         "الراتب الأساسي",          CategoryFinancial),
        new("Allowances",           "مجموع العلاوات",          CategoryFinancial),
        new("Gross-Salary",         "الراتب الكلي",            CategoryFinancial),
        new("Salary-Scale",         "سلم الرواتب",             CategoryFinancial),
        new("Bank",                 "البنك",                   CategoryFinancial),
        new("Currency",             "العملة",                  CategoryFinancial),

        new("Company-Name",         "اسم الشركة",              CategoryCompany),
        new("Branch-Name",          "اسم الفرع",               CategoryCompany),

        new("Document-Ref",         "الرقم المرجعي للوثيقة",   CategoryDocument),
        new("Document-Date",        "تاريخ إصدار الوثيقة",     CategoryDocument),
        new("Issued-By",            "أصدرها",                  CategoryDocument)
    };

    /// <summary>الكتالوج + الحقول الإضافية المعرَّفة فعلاً (نفس سلوك محرّك الشروط).</summary>
    public static IReadOnlyList<Token> WithCustomFields(IEnumerable<(string Key, string Label)> customFields)
    {
        var all = Catalog.ToList();

        foreach (var (key, label) in customFields)
        {
            if (string.IsNullOrWhiteSpace(key)) continue;
            all.Add(new Token(CustomPrefix + key, string.IsNullOrWhiteSpace(label) ? key : label, "حقول إضافية"));
        }

        return all;
    }

    // ── بناء القيم ─────────────────────────────────────────────────────────────

    /// <summary>سياق التوليد: ما لا يأتي من صفّ الموظف (رقم الوثيقة · تاريخها · مُصدرها).</summary>
    public sealed record DocumentContext(
        string ReferenceNo,
        DateOnly IssuedOn,
        string? IssuedBy,
        string? CompanyName,
        string? BranchName,
        string? Currency);

    /// <summary>بيانات إضافية عن الموظف لا يحملها <see cref="HrConditionFacts.EmployeeRow"/>.</summary>
    public sealed record EmployeeExtras(
        string FullName,
        string? FullNameEn,
        string? FirstName,
        string? LastName,
        string? NationalId,
        string? PassportNo,
        string? Phone,
        string? Email,
        string? PositionName,
        string? DepartmentName,
        string? BranchName,
        string? ManagerName,
        string? ContractNo,
        DateOnly? ContractStart,
        string? BankName,
        /// <summary>رابط توقيع الموظف بنقطة مصادَقة؛ فارغ إن لم يُرفع توقيع.</summary>
        string? SignatureUrl = null);

    /// <summary>
    /// يبني قاموس الرموز. القيم **نصوص جاهزة للعرض** لا خام: التواريخ منسَّقة
    /// والأرقام بفواصل الآلاف — لأن الوثيقة مخرَج بشريّ لا سجلّ بيانات.
    ///
    /// الحقيقة المفقودة تعطي **نصّاً فارغاً** لا كلمة «null»: شهادة راتب تكتب
    /// «الراتب: null» أسوأ من واحدة تترك الفراغ للكاتب ليلاحظه.
    /// </summary>
    public static Dictionary<string, string> Build(
        HrConditionFacts.EmployeeRow row,
        EmployeeExtras extras,
        DocumentContext context,
        DateOnly asOf)
    {
        var serviceMonths = HrConditionFacts.ServiceMonths(row.HireDate, row.JoiningDate, asOf);
        var gross = row.BasicSalary is null ? null : row.BasicSalary + (row.Allowances ?? 0);

        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Employee-Code"] = row.EmployeeNo,
            ["Employee-FullName"] = extras.FullName,
            ["Employee-FullName-En"] = Text(extras.FullNameEn),
            ["Employee-FirstName"] = Text(extras.FirstName),
            ["Employee-LastName"] = Text(extras.LastName),
            ["National-Id"] = Text(extras.NationalId),
            ["Passport-No"] = Text(extras.PassportNo),
            ["Nationality"] = Text(row.Nationality),
            ["Gender"] = Text(row.Gender),
            ["Religion"] = Text(row.Religion),
            ["Marital-Status"] = Text(row.MaritalStatus),
            ["Birth-Date"] = Date(row.BirthDate),
            ["Age"] = Number(HrConditionFacts.AgeYears(row.BirthDate, asOf)),
            ["Is-Citizen"] = row.IsCitizen ? "مواطن" : "غير مواطن",
            ["Employee-Signature-Url"] = Text(extras.SignatureUrl),
            ["Phone"] = Text(extras.Phone),
            ["Email"] = Text(extras.Email),

            ["Position"] = Text(extras.PositionName),
            ["Department"] = Text(extras.DepartmentName),
            ["Branch"] = Text(extras.BranchName),
            ["Direct-Manager"] = Text(extras.ManagerName),
            ["Job-Grade"] = Text(row.JobGrade),
            ["Work-Type"] = Text(row.WorkType),
            ["Employment-Status"] = Text(row.EmploymentStatus),
            ["Hire-Date"] = Date(row.HireDate),
            ["Joining-Date"] = Date(row.JoiningDate),
            ["Service-Period"] = ServicePeriodLabel(serviceMonths),
            ["Service-Months"] = Number(serviceMonths),

            ["Contract-Type"] = Text(row.ContractType),
            ["Contract-No"] = Text(extras.ContractNo),
            ["Contract-Start"] = Date(extras.ContractStart),
            ["Contract-End"] = Date(row.ContractEndDate),
            ["Sponsor"] = Text(row.SponsorName),

            ["Basic-Salary"] = Money(row.BasicSalary),
            ["Allowances"] = Money(row.Allowances),
            ["Gross-Salary"] = Money(gross),
            ["Salary-Scale"] = Text(row.SalaryScale),
            ["Bank"] = Text(extras.BankName),
            ["Currency"] = Text(context.Currency),

            ["Company-Name"] = Text(context.CompanyName),
            ["Branch-Name"] = Text(context.BranchName ?? extras.BranchName),

            ["Document-Ref"] = context.ReferenceNo,
            ["Document-Date"] = Date(context.IssuedOn),
            ["Issued-By"] = Text(context.IssuedBy)
        };

        foreach (var (key, value) in row.CustomFields)
        {
            tokens[CustomPrefix + key] = Text(value);
        }

        return tokens;
    }

    /// <summary>«سنتان و3 أشهر» — صيغة بشرية، فالوثيقة تُقرأ لا تُحلَّل.</summary>
    public static string ServicePeriodLabel(int? months)
    {
        if (months is not { } total)
        {
            return string.Empty;
        }

        var years = total / 12;
        var rest = total % 12;

        var parts = new List<string>();
        if (years > 0) parts.Add(years == 1 ? "سنة" : years == 2 ? "سنتان" : $"{years} سنوات");
        if (rest > 0) parts.Add(rest == 1 ? "شهر" : rest == 2 ? "شهران" : $"{rest} أشهر");

        return parts.Count == 0 ? "أقل من شهر" : string.Join(" و", parts);
    }

    private static string Text(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    private static string Date(DateOnly? value) => value is { } date ? date.ToString("yyyy/MM/dd") : string.Empty;
    private static string Number(int? value) => value is { } number ? number.ToString(CultureInfo.InvariantCulture) : string.Empty;
    private static string Money(decimal? value) => value is { } amount ? amount.ToString("N2", CultureInfo.InvariantCulture) : string.Empty;

    // ── الاستبدال ──────────────────────────────────────────────────────────────

    /// <summary>الرمز: حروف/أرقام/شرطة داخل قوسين معقوفين. المسافات ممنوعة عمداً فلا تُلتقط أقواس CSS.</summary>
    private static readonly Regex TokenPattern = new(@"\{([A-Za-z][A-Za-z0-9\-_]{0,60})\}", RegexOptions.Compiled);

    public sealed record RenderResult(string Html, IReadOnlyList<string> UnresolvedTokens);

    /// <summary>
    /// يستبدل الرموز بقيمها **مهرَّبةً HTML**، ويُرجع قائمة ما لم يُحَلّ.
    /// غير المحلول يبقى بنصّه ليراه الكاتب — لا يُمحى بصمت.
    /// </summary>
    public static RenderResult Render(string? templateHtml, IReadOnlyDictionary<string, string> tokens)
    {
        if (string.IsNullOrWhiteSpace(templateHtml))
        {
            return new RenderResult(string.Empty, Array.Empty<string>());
        }

        var unresolved = new List<string>();

        var html = TokenPattern.Replace(templateHtml, match =>
        {
            var key = match.Groups[1].Value;

            if (!tokens.TryGetValue(key, out var value))
            {
                if (!unresolved.Contains(key, StringComparer.OrdinalIgnoreCase))
                {
                    unresolved.Add(key);
                }

                return match.Value;
            }

            return HtmlEscape(value);
        });

        return new RenderResult(html, unresolved);
    }

    /// <summary>الرموز المستعملة بقالب — تُعرض للكاتب وتكشف الأخطاء الإملائية.</summary>
    public static List<string> ExtractTokens(string? templateHtml) =>
        string.IsNullOrWhiteSpace(templateHtml)
            ? new List<string>()
            : TokenPattern.Matches(templateHtml)
                .Select(match => match.Groups[1].Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

    /// <summary>رموز بالقالب لا مقابل لها بالكتالوج — تُعرض عند حفظ القالب لا عند التوليد.</summary>
    public static List<string> UnknownTokens(string? templateHtml, IEnumerable<Token> catalog)
    {
        var known = new HashSet<string>(catalog.Select(token => token.Key), StringComparer.OrdinalIgnoreCase);
        return ExtractTokens(templateHtml).Where(token => !known.Contains(token)).ToList();
    }

    internal static string HtmlEscape(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var c in value)
        {
            switch (c)
            {
                case '&': builder.Append("&amp;"); break;
                case '<': builder.Append("&lt;"); break;
                case '>': builder.Append("&gt;"); break;
                case '"': builder.Append("&quot;"); break;
                case '\'': builder.Append("&#39;"); break;
                default: builder.Append(c); break;
            }
        }

        return builder.ToString();
    }
}
