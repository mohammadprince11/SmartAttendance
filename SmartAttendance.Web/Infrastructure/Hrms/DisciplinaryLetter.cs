using System.Text.RegularExpressions;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// تركيب «كتاب العقوبة» من قالبه — **منطق نقيّ بلا قاعدة بيانات**.
///
/// كان استبدال الرموز مكتوباً داخل صفحة الطباعة وحدها، فأي قناة أخرى (الكتاب
/// المحفوظ · البريد · بوابة الموظف) كانت ستنسخه — ثم يُضاف رمزٌ لواحدة دون
/// الأخرى فيخرج كتابان بنصّين مختلفين لنفس المخالفة. المصدر هنا واحد.
///
/// ⚠️ **الرموز غير المعروفة تبقى كما هي ولا تُحذف.** حذفها بصمت يعني كتاباً
/// رسمياً تنقصه جملةٌ بلا أن يلاحظ أحد؛ وبقاؤها ظاهرةً يجعل الخلل مرئياً قبل
/// التوقيع. و<see cref="UnresolvedTokens"/> يتيح للمستدعي أن ينبّه إليها.
/// </summary>
public static class DisciplinaryLetter
{
    /// <summary>كل ما يحتاجه الكتاب من وقائع المخالفة — لا استعلام داخل التركيب.</summary>
    public sealed record Context(
        string ReferenceNo,
        string EmployeeName,
        string EmployeeCode,
        string Department,
        string Position,
        DateOnly EventDate,
        string ViolationCategory,
        string ViolationName,
        string Description,
        string PenaltyAction,
        string FinancialImpact,
        decimal DeductionAmount,
        string ArticleNo,
        string ApprovedBy,
        int OccurrenceNumber = 1);

    /// <summary>الرموز المدعومة — تُعرض بشاشة القوالب كي لا يُخترع رمزٌ لا يُستبدل.</summary>
    public static readonly IReadOnlyList<string> Tokens = new[]
    {
        "{ReferenceNo}", "{EmployeeName}", "{EmployeeCode}", "{Department}", "{Position}",
        "{ViolationDate}", "{ViolationCategory}", "{ViolationName}", "{ViolationDescription}",
        "{ArticleNo}", "{OccurrenceNumber}", "{PenaltyAction}", "{FinancialImpact}",
        "{DeductionAmount}", "{ApprovedBy}", "{IssueDate}"
    };

    private static readonly Regex TokenPattern = new(@"\{[A-Za-z][A-Za-z0-9_]*\}", RegexOptions.Compiled);

    /// <summary>يستبدل رموز القالب بوقائع المخالفة. <paramref name="issuedOn"/> تاريخ إصدار الكتاب.</summary>
    public static string Render(string? template, Context context, DateOnly issuedOn)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return string.Empty;
        }

        // فقرةٌ بلا رقم لا تُترك شرطةً بالكتاب الرسمي: تصير «—» صراحةً كي يُقرأ
        // النقص نقصاً لا سهواً مطبعياً.
        var article = string.IsNullOrWhiteSpace(context.ArticleNo) ? "—" : context.ArticleNo.Trim();

        return template
            .Replace("{ReferenceNo}", context.ReferenceNo)
            .Replace("{EmployeeName}", context.EmployeeName)
            .Replace("{EmployeeCode}", context.EmployeeCode)
            .Replace("{Department}", context.Department)
            .Replace("{Position}", context.Position)
            .Replace("{ViolationDate}", context.EventDate.ToString("yyyy-MM-dd"))
            .Replace("{ViolationCategory}", context.ViolationCategory)
            .Replace("{ViolationName}", context.ViolationName)
            .Replace("{ViolationDescription}", context.Description)
            .Replace("{ArticleNo}", article)
            .Replace("{OccurrenceNumber}", context.OccurrenceNumber.ToString())
            .Replace("{PenaltyAction}", context.PenaltyAction)
            .Replace("{FinancialImpact}", context.FinancialImpact)
            .Replace("{DeductionAmount}", context.DeductionAmount.ToString("N0"))
            .Replace("{ApprovedBy}", context.ApprovedBy)
            .Replace("{IssueDate}", issuedOn.ToString("yyyy-MM-dd"));
    }

    /// <summary>الرموز التي بقيت بعد التركيب — قالبٌ يذكر رمزاً لا نعرفه.</summary>
    public static IReadOnlyList<string> UnresolvedTokens(string? rendered) =>
        string.IsNullOrWhiteSpace(rendered)
            ? Array.Empty<string>()
            : TokenPattern.Matches(rendered)
                .Select(match => match.Value)
                .Distinct(StringComparer.Ordinal)
                .ToList();

    /// <summary>
    /// نصّ الأثر المالي بالعربية. القيمة صفرٌ ⟹ «لا أثر مالي» صراحةً: كتابٌ يقول
    /// «خصم 0» يُقرأ خطأً مطبعياً، والصمت يُقرأ خصماً مجهول المقدار.
    /// </summary>
    public static string FinancialImpactText(string? impactType, decimal value) => impactType switch
    {
        "Days" when value > 0 => $"خصم {value:0.##} يوم من الراتب",
        "Hours" when value > 0 => $"خصم {value:0.##} ساعة من الراتب",
        "Amount" when value > 0 => $"خصم مبلغ {value:N0}",
        "Percent" when value > 0 => $"خصم {value:0.##}% من الراتب",
        _ => "لا أثر مالي"
    };

    /// <summary>
    /// الكتاب الاحتياطي حين لا قالب نشطاً بالتهيئة.
    ///
    /// **لا يُترك الكتاب فارغاً**: قرارٌ يرتّب خصماً على راتب موظف يجب أن يخرج بنصّ
    /// يستطيع الموظف قراءته والاعتراض عليه، سواءٌ هيّأت الشركة قوالبها أم لا.
    /// </summary>
    public const string FallbackBody = """
السيد/ة: {EmployeeName}
الرقم الوظيفي: {EmployeeCode}
القسم: {Department}

نُعلمكم بأنه سُجّلت بحقكم مخالفة بالرقم المرجعي {ReferenceNo} بتاريخ {ViolationDate}:
{ViolationName} ({ViolationCategory})

سند المخالفة بلائحة الجزاءات: الفقرة {ArticleNo}

وبناءً عليه تقرّر: {PenaltyAction}
الأثر المالي: {FinancialImpact}

المعتمِد: {ApprovedBy}
تاريخ الإصدار: {IssueDate}
""";

    public const string FallbackSubject = "إشعار مخالفة وجزاء — {ReferenceNo}";
}
