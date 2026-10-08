namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>Capabilities backed by the current resolver; not a list of hypothetical modules.</summary>
public static class ApprovalTemplateCapabilities
{
    public sealed record Definition(string Module, string Description, string? NumericLabel = null, bool ChangedField = false);
    private static readonly IReadOnlyDictionary<string, Definition> Definitions =
        new Dictionary<string, Definition>(StringComparer.OrdinalIgnoreCase)
        {
            ["InfoChange"] = new("الأشخاص", "اعتماد تعديل بيانات الموظف قبل تطبيقها على ملفه.", ChangedField: true),
            ["CustomRequest"] = new("الأشخاص", "اعتماد طلبات الموظفين والنماذج المخصصة عبر مسار الموافقات."),
            ["Violation"] = new("الأشخاص", "تحديد لجنة طلبات إجراءات المخالفات."),
            ["Resignation"] = new("الأشخاص", "تحديد مراحل مراجعة طلب استقالة الموظف."),
            ["Transfer"] = new("الأشخاص", "تحديد لجنة طلبات نقل الموظف."),
            ["Onboarding"] = new("الأشخاص", "اعتماد تهيئة وتعيين الموظف ضمن إجراءات دورة حياته."),
            ["Offboarding"] = new("الأشخاص", "اعتماد إنهاء وخروج الموظف ضمن إجراءات دورة حياته."),
            ["Loan"] = new("الرواتب", "اعتماد القرض أو السلفة قبل تفعيل الأثر المالي.", "مبلغ الطلب"),
            ["SalaryIncrease"] = new("الرواتب", "اعتماد طلب زيادة الراتب قبل تطبيق الزيادة.", "قيمة الزيادة المقدّمة"),
            ["FinancialClaim"] = new("الرواتب", "اعتماد البدل المالي أو استرداد النفقات.", "مبلغ المطالبة"),
            ["Overtime"] = new("الرواتب", "اعتماد طلب العمل الإضافي قبل احتساب أثره وفق إعدادات النظام."),
            ["MissingPunch"] = new("الحضور والانصراف", "تحديد مراحل اعتماد طلب البصمة المفقودة."),
            ["ExitPermission"] = new("الحضور والانصراف", "تحديد لجنة طلب المغادرة أثناء الدوام."),
            ["ShiftRequest"] = new("الحضور والانصراف", "اعتماد طلب إسناد مناوبة للموظف."),
            ["ShiftChange"] = new("الحضور والانصراف", "اعتماد طلب تغيير مناوبة الموظف."),
            ["ShiftSwap"] = new("الحضور والانصراف", "اعتماد تبادل المناوبة مع زميل."),
            ["WorkFromHome"] = new("الحضور والانصراف", "اعتماد طلب العمل من المنزل."),
            ["LeaveRequest"] = new("الإجازات", "اعتماد طلب الإجازة حسب المدة واللجنة المحددة.", "عدد أيام الإجازة"),
            ["LeaveCancel"] = new("الإجازات", "تحديد مراحل مراجعة طلب إلغاء الإجازة."),
            ["ReturnToWork"] = new("الإجازات", "تحديد مراحل مراجعة طلب العودة إلى العمل."),
            ["DocumentRequest"] = new("الوثائق", "تحديد لجنة طلبات وثائق الموظف المقدّمة لمسار الموافقات.")
        };

    public static readonly IReadOnlyList<string> Modules = ["الأشخاص", "الرواتب", "الحضور والانصراف", "الإجازات", "الوثائق"];
    public static Definition? For(string? type) => type != null && Definitions.TryGetValue(type, out var definition) ? definition : null;

    public static string? ValidateConditions(ApprovalTemplateStore.TemplateRow template)
    {
        if (!template.HasConditions) return null;
        var definition = For(template.RequestType);
        // ExitPermission's legacy numeric resolver uses DaysCount; do not relabel it as hours.
        var supportsLegacyNumeric = string.Equals(template.RequestType, "ExitPermission", StringComparison.OrdinalIgnoreCase);
        if (definition?.NumericLabel == null && !supportsLegacyNumeric &&
            (template.CondMinAmount.HasValue || template.CondMaxAmount.HasValue))
            return "هذا النوع لا يدعم شرط قيمة الطلب. أزل حدود القيمة أو اختر نوعاً مالياً أو طلب إجازة.";
        if (definition?.ChangedField != true && !string.IsNullOrWhiteSpace(template.CondChangedFieldKey))
            return "شرط تغيير حقل متاح لقوالب تعديل معلومات الموظف فقط.";
        return null;
    }
}
