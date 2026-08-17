using SmartAttendance.Application.Common.Security;

namespace SmartAttendance.Application.Employees.ViewModels;

public class EmployeeListQueryViewModel
{
    public PeopleDataScope? DataScope { get; set; }

    /// <summary>
    /// نطاق «موظفي أدوار الوصول» — يُقاطَع (AND) مع <see cref="DataScope"/> بمستوى
    /// SQL: صفٌّ يظهر بالقائمة فقط إن سمح به الاثنان. أدمن/«All»/بلا دور بيانات
    /// يُحسَم غير مقيَّد فلا يوسّع النطاق، إنما قد يضيّقه (P0-1).
    /// </summary>
    public PeopleDataScope? AccessRoleScope { get; set; }

    public int? CompanyId { get; set; }

    public string? SearchTerm { get; set; }

    /// <summary>
    /// حصر القائمة بأكواد موظفين محدّدة (فلترة «من جدول بيانات» بنمط كيان) —
    /// تُقاطَع مع بقية المرشّحات والنطاقات ولا توسّعها.
    /// </summary>
    public IReadOnlyList<string>? EmployeeNos { get; set; }

    public int? BranchId { get; set; }

    public int? DepartmentId { get; set; }

    public string StatusFilter { get; set; } = "active";

    public string SortBy { get; set; } = "name";

    public int PageNumber { get; set; } = 1;

    public int PageSize { get; set; } = 25;
}
