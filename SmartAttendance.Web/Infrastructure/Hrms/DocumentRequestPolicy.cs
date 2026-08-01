namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// قواعد طلب الوثيقة من الخدمة الذاتية — منطق نقيّ.
///
/// الفصل مقصود: الأهلية والتحقق وانتقالات الحالة **تُختبر بلا قاعدة بيانات**،
/// لأنها الطبقة التي إن أخطأت أعطت موظفاً وثيقةً لا يستحقّها أو منعت آخر يستحقّها.
/// </summary>
public static class DocumentRequestPolicy
{
    public const string StatusPending = "Pending";
    public const string StatusApproved = "Approved";
    public const string StatusRejected = "Rejected";

    public static string StatusLabel(string? status) => status switch
    {
        StatusApproved => "معتمد",
        StatusRejected => "مرفوض",
        _ => "قيد المراجعة"
    };

    /// <summary>
    /// هل يحقّ لهذا الموظف طلب هذا القالب؟ ثلاثة شروط **كلها لازمة**:
    /// القالب نشط · مسموح طلبه من الخدمة الذاتية · وشروط جمهوره تنطبق عليه.
    ///
    /// <b>بلا شروط ⟶ متاح للجميع</b> (نمط أهلية لا نسخة سياسة): قالبٌ فُتح للطلب
    /// ولم تُحدَّد له شروط مقصودٌ به الكلّ، ومنعُه حينها يعطّل الميزة بصمت.
    /// </summary>
    public static bool CanRequest(
        bool isActive,
        bool allowsEmployeeRequest,
        HrConditions.ConditionSet? conditions,
        IReadOnlyDictionary<string, HrConditions.Fact> facts) =>
        isActive
        && allowsEmployeeRequest
        && HrConditions.Matches(conditions, facts, matchWhenEmpty: true);

    /// <summary>سبب رفض التحقق، أو <c>null</c> إن كان الطلب مكتملاً.</summary>
    public static string? Validate(bool reasonRequired, bool attachmentRequired, string? reason, bool hasAttachment)
    {
        if (reasonRequired && string.IsNullOrWhiteSpace(reason))
        {
            return "سبب الطلب إلزامي لهذه الوثيقة.";
        }

        if (attachmentRequired && !hasAttachment)
        {
            return "مرفق الطلب إلزامي لهذه الوثيقة.";
        }

        return null;
    }

    /// <summary>
    /// الطلب المحسوم **لا يُعاد حسمه**: اعتمادٌ يُصدر وثيقةً ورقمَ مرجع، وإعادة
    /// اعتماده تُصدر ثانيةً؛ ورفضٌ بعد اعتماد يترك وثيقةً صادرةً لطلبٍ مرفوض.
    /// </summary>
    public static bool CanReview(string? status) =>
        string.Equals(status ?? StatusPending, StatusPending, StringComparison.OrdinalIgnoreCase);

    /// <summary>الموظف يسحب طلبه ما دام لم يُحسم — وبعد الحسم يبقى سجلّاً.</summary>
    public static bool CanWithdraw(string? status) => CanReview(status);

    /// <summary>
    /// الرفض يستوجب سبباً. «رُفض» بلا تعليل يجعل الموظف يعيد الطلب بلا فهم،
    /// ويحرم الشركة من سجلٍّ يُحتجّ به.
    /// </summary>
    public static string? ValidateRejection(string? note) =>
        string.IsNullOrWhiteSpace(note) ? "سبب الرفض إلزامي." : null;
}
