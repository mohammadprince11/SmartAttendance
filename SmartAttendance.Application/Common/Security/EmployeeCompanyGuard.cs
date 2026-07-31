namespace SmartAttendance.Application.Common.Security;

/// <summary>
/// يمنع انحراف شركة الموظف بعد الترحيل: الفرع هو **مصدر الحقيقة الوحيد**، والقسم
/// يجب أن يوافقه. تعبئة العمود مرة واحدة بلا هذا القيد تعني عودة الانحراف خلال
/// أسابيع مع كل نقل موظف بين الفروع.
///
/// نقية بالكامل: تأخذ الشركتين المشتقّتين وتقرّر — بلا قاعدة بيانات ولا سياق طلب.
/// </summary>
public static class EmployeeCompanyGuard
{
    /// <summary>
    /// شركة الموظف الصحيحة، أو null إن كان الإسناد غير متسق/غير صالح ⟹ يُرفض
    /// الحفظ بدل كتابة صفّ بشركة مخمَّنة.
    /// </summary>
    public static int? Resolve(int branchCompanyId, int departmentCompanyId)
    {
        if (branchCompanyId <= 0 || departmentCompanyId <= 0)
        {
            return null;
        }

        return branchCompanyId == departmentCompanyId ? branchCompanyId : null;
    }

    /// <summary>هل قيمة مخزَّنة على الموظف ما زالت مطابقة لشركة فرعه؟</summary>
    public static bool IsConsistent(int? storedCompanyId, int branchCompanyId) =>
        branchCompanyId > 0 &&
        storedCompanyId.HasValue &&
        storedCompanyId.Value == branchCompanyId;
}
