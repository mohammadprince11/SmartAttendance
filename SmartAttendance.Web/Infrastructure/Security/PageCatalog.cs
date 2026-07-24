namespace SmartAttendance.Web.Infrastructure.Security;

/// <summary>
/// Data-driven catalog of the application's modules and pages, used to build the
/// Pages access-role tree (module → page → CRUD actions). Mirrors the live
/// navigation tree; extend here when new pages are added. Page codes are stable
/// identifiers stored as grant keys, independent of route or label changes.
/// </summary>
public static class PageCatalog
{
    public sealed record CatalogPage(string Code, string Label);

    public sealed record CatalogModule(string Key, string Label, IReadOnlyList<CatalogPage> Pages);

    public static readonly IReadOnlyList<string> Actions = new[] { "View", "Create", "Edit", "Delete" };

    public static readonly IReadOnlyDictionary<string, string> ActionLabels =
        new Dictionary<string, string>
        {
            ["View"] = "عرض",
            ["Create"] = "إضافة",
            ["Edit"] = "تعديل",
            ["Delete"] = "حذف",
        };

    public static readonly IReadOnlyList<CatalogModule> Modules = new List<CatalogModule>
    {
        new("Setup", "إعداد الشركة", new[]
        {
            new CatalogPage("Setup.Company", "بيانات الشركة"),
            new CatalogPage("Setup.Branches", "الفروع"),
            new CatalogPage("Setup.Departments", "الأقسام"),
            new CatalogPage("Setup.Positions", "المناصب"),
        }),
        new("Identity", "إدارة المستخدمين", new[]
        {
            new CatalogPage("Identity.Users", "المستخدمون"),
            new CatalogPage("Identity.Permissions", "الصلاحيات"),
            new CatalogPage("Identity.AccessRoles", "أدوار الوصول"),
        }),
        new("People", "أشخاص", new[]
        {
            new CatalogPage("People.Directory", "قائمة الموظفين"),
            new CatalogPage("People.Profile", "ملف الموظف"),
            new CatalogPage("People.Import", "استيراد الموظفين"),
            new CatalogPage("People.Engagement", "تفاعل الموظفين"),
            new CatalogPage("People.Updates", "تحديثات الموظف"),
            new CatalogPage("People.Violations", "حالات المخالفات"),
            new CatalogPage("People.Assets", "إدارة العهد"),
            new CatalogPage("People.Tasks", "مهام التعيين والإنهاء"),
            new CatalogPage("People.LeaveBalances", "أرصدة الإجازات"),
            new CatalogPage("People.Alerts", "التنبيهات والانتهاءات"),
            new CatalogPage("People.Documents", "مركز الوثائق"),
            new CatalogPage("People.Organization", "الهياكل التنظيمية"),
            new CatalogPage("People.OrgStructures", "الهياكل الثلاث المتوازية"),
            new CatalogPage("People.Reports", "تقارير الأشخاص"),
            new CatalogPage("People.Cards", "بطاقات الموظفين"),
        }),
        new("HrSettings", "إعدادات الموارد البشرية", new[]
        {
            new CatalogPage("HrSettings.Disciplinary", "إعدادات المخالفات"),
            new CatalogPage("HrSettings.ProfileFields", "حقول ملف الموظف"),
            new CatalogPage("HrSettings.ApprovalTemplates", "قوالب الموافقات"),
            new CatalogPage("HrSettings.EntityFields", "حقول الكيانات"),
            new CatalogPage("HrSettings.EmployeeGroups", "مجموعات الموظفين"),
            new CatalogPage("HrSettings.Lookups", "القوائم المرجعية"),
        }),
        new("Attendance", "الحضور والانصراف", new[]
        {
            new CatalogPage("Attendance.Operations", "مراقبة الحضور"),
            new CatalogPage("Attendance.Records", "سجلات الحضور"),
            new CatalogPage("Attendance.Imports", "استيراد البصمات"),
            new CatalogPage("Attendance.Processing", "معالجة الحضور"),
            new CatalogPage("Attendance.Corrections", "تصحيحات الحضور"),
            new CatalogPage("Attendance.ShiftTypes", "أنواع المناوبات"),
            new CatalogPage("Attendance.Settings", "إعدادات الحضور"),
            new CatalogPage("Attendance.DayAttendance", "الحضور اليومي"),
            new CatalogPage("Attendance.ShiftRules", "قواعد المناوبات"),
            new CatalogPage("Attendance.PeriodRules", "القواعد الفترية"),
            new CatalogPage("Attendance.Recommendations", "الإجراءات المقترحة"),
            new CatalogPage("Attendance.MissingPunch", "طلبات البصمة المفقودة"),
            new CatalogPage("Attendance.OnlinePunches", "البصمات عبر الإنترنت"),
            new CatalogPage("Attendance.Assignments", "مناوبات الموظفين"),
            new CatalogPage("Attendance.Viewer", "مستعرض الحضور"),
            new CatalogPage("Attendance.MonthAttendance", "الحضور الشهري"),
            new CatalogPage("Attendance.WeekAttendance", "الحضور الأسبوعي"),
            new CatalogPage("Attendance.Reports", "تقارير الحضور"),
            new CatalogPage("Attendance.Devices", "الأجهزة"),
        }),
        new("Payroll", "الرواتب", new[]
        {
            new CatalogPage("Payroll.Runs", "المسير"),
            new CatalogPage("Payroll.Transactions", "الحركات (دخل/اقتطاع)"),
            new CatalogPage("Payroll.Overtime", "العمل الإضافي"),
            new CatalogPage("Payroll.SalaryDaysAdjustment", "تعديل أيام الراتب"),
            new CatalogPage("Payroll.LeaveEncashment", "بدل إجازة"),
            new CatalogPage("Payroll.Raises", "زيادات الراتب"),
            new CatalogPage("Payroll.EndOfService", "نهاية الخدمة"),
            new CatalogPage("Payroll.Provisions", "حساب الاحتياطي"),
            new CatalogPage("Payroll.SalaryItems", "عناصر الراتب"),
            new CatalogPage("Payroll.Settings", "تهيئة الضريبة والضمان"),
            new CatalogPage("Payroll.BankTemplates", "قوالب ملفات البنوك"),
            new CatalogPage("Payroll.TaxSocial", "الضرائب والضمان"),
            new CatalogPage("Payroll.FinancialInfo", "تحديث المعلومات المالية"),
            new CatalogPage("Payroll.Payment", "معلومات الدفع"),
        }),
    };

    public static bool IsValidPage(string code) =>
        Modules.Any(m => m.Pages.Any(p => p.Code.Equals(code, StringComparison.OrdinalIgnoreCase)));
}
