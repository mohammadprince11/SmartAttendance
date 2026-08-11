namespace SmartAttendance.Application.LeaveRequests.Services;

/// <summary>
/// نطاق الوصول لطلبات الإجازة — يُمرَّر إلزاميّاً لكل قراءة وكتابة بالخدمة.
///
/// <para><b>الثغرة المحروسة (AUTHZ-003):</b> كانت الخدمة تعمل بالمعرّف وحده
/// (<c>GetByIdAsync(id)</c> · <c>DeleteAsync(id)</c>) بلا أي فحص ملكية أو شركة،
/// والحارس المركزي لا يعرف المورد بل المسار والدور فقط. فكان أدنى دور بالنظام
/// (<c>Employee</c>) يقرأ ويحذف <b>أي طلب إجازة بأي شركة</b> بمعرفة رقمه.</para>
///
/// <para><b>لماذا هنا لا بالصفحة:</b> الحماية بالصفحة تعتمد على تذكُّر كاتبها —
/// وقد ثبت أنها تصمد صدفةً (<c>Edit</c> يربط حقلاً اسمه <c>EmployeeId</c>) وتنهار
/// حيث لا يُرسل (<c>Delete</c> يرسل <c>id</c> فقط). بجعل النطاق <b>معطىً إلزاميّاً
/// بلا قيمة افتراضية</b>، يرفض المُصرِّف أي استدعاء جديد ينسى النطاق.</para>
///
/// <para>بُعدان مستقلّان: <b>الشركة</b> (أي شركات يراها المستخدم) و<b>الموظّف</b>
/// (دور <c>Employee</c> لا يرى إلا طلباته). كلاهما يُفرض معاً.</para>
/// </summary>
public sealed class LeaveRequestAccessScope
{
    private readonly HashSet<int> _allowedCompanyIds;

    private LeaveRequestAccessScope(
        bool isUnrestricted,
        IEnumerable<int> allowedCompanyIds,
        int? onlyEmployeeId)
    {
        IsUnrestricted = isUnrestricted;
        _allowedCompanyIds = new HashSet<int>(allowedCompanyIds);
        OnlyEmployeeId = onlyEmployeeId;
    }

    /// <summary>كل الشركات وكل الموظفين — للأدمن غير المقيَّد وحده.</summary>
    public static LeaveRequestAccessScope Unrestricted() =>
        new(true, Array.Empty<int>(), null);

    /// <summary>لا شيء إطلاقاً — الحالة المغلقة حين يتعذّر تحديد الهوية.</summary>
    public static LeaveRequestAccessScope DeniedAll() =>
        new(false, Array.Empty<int>(), null);

    /// <summary>شركات محدّدة (قائمة فارغة ⟹ لا شيء).</summary>
    public static LeaveRequestAccessScope ForCompanies(IEnumerable<int> companyIds) =>
        new(false, companyIds ?? Array.Empty<int>(), null);

    /// <summary>يضيف قيد «طلباتي فقط» فوق قيد الشركة — لا يُلغيه.</summary>
    public LeaveRequestAccessScope RestrictedToEmployee(int employeeId) =>
        employeeId > 0
            ? new LeaveRequestAccessScope(IsUnrestricted, _allowedCompanyIds, employeeId)
            : DeniedAll();

    public bool IsUnrestricted { get; }

    public IReadOnlyCollection<int> AllowedCompanyIds => _allowedCompanyIds;

    /// <summary>حين يُضبَط: لا يُرى إلا طلبات هذا الموظّف.</summary>
    public int? OnlyEmployeeId { get; }

    public bool IsDeniedAll => !IsUnrestricted && _allowedCompanyIds.Count == 0;

    /// <summary>
    /// هل يُسمح بطلبٍ يخصّ هذا الموظّف؟ <b>مغلق الفشل</b>: موظّفٌ بلا شركة
    /// (<c>CompanyId = null</c>) لا يمرّ إلا للأدمن غير المقيَّد — فبيانات لا تُعرف
    /// شركتها لا تُسلَّم لمستخدمٍ مقيَّد بشركة.
    /// </summary>
    public bool Allows(int? employeeCompanyId, int employeeId)
    {
        if (OnlyEmployeeId.HasValue && OnlyEmployeeId.Value != employeeId)
        {
            return false;
        }

        if (IsUnrestricted)
        {
            return true;
        }

        return employeeCompanyId.HasValue &&
               _allowedCompanyIds.Contains(employeeCompanyId.Value);
    }
}
