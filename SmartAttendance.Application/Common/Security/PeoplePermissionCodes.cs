namespace SmartAttendance.Application.Common.Security;

public sealed record PermissionDefinition(
    string Module,
    string Code,
    string Name,
    string Description,
    int DisplayOrder);

public static class PeoplePermissionCodes
{
    public const string ViewDirectory = "People.ViewDirectory";
    public const string ViewProfile = "People.ViewProfile";
    public const string Create = "People.Create";
    public const string Edit = "People.Edit";
    public const string Import = "People.Import";
    public const string Export = "People.Export";
    public const string UploadDocument = "People.UploadDocument";
    public const string DeleteDocument = "People.DeleteDocument";
    public const string EndService = "People.EndService";
    public const string Rehire = "People.Rehire";
    public const string ChangeAssignment = "People.ChangeAssignment";
    public const string ViewLifecycle = "People.ViewLifecycle";
    public const string ViewHistory = "People.ViewHistory";
    public const string Delete = "People.Delete";
    public const string ManagePermissions = "People.ManagePermissions";

    public static IReadOnlyList<PermissionDefinition> Definitions { get; } =
        new List<PermissionDefinition>
        {
            new("People", ViewDirectory, "عرض دليل الأشخاص", "عرض قائمة الأشخاص والبحث والتصفية ضمن نطاق البيانات المسموح.", 100),
            new("People", ViewProfile, "عرض ملف الشخص", "فتح ملف الشخص وقراءة الأقسام والحقول المسموح بها.", 110),
            new("People", Create, "إضافة شخص", "إنشاء سجل شخص أو موظف جديد.", 120),
            new("People", Edit, "تعديل بيانات الشخص", "تعديل بيانات الشخص ضمن الحقول والنطاقات المسموح بها.", 130),
            new("People", Import, "استيراد الأشخاص", "رفع ومعالجة ملفات استيراد الأشخاص.", 140),
            new("People", Export, "تصدير الأشخاص", "تصدير نتائج الأشخاص المسموح للمستخدم برؤيتها.", 150),
            new("People", UploadDocument, "رفع مستند", "رفع مستند إلى ملف الشخص.", 160),
            new("People", DeleteDocument, "حذف مستند", "حذف مستند من ملف الشخص.", 170),
            new("People", ChangeAssignment, "تغيير الارتباط الوظيفي", "تغيير الشركة أو الموقع أو القسم أو المنصب أو المدير المباشر.", 180),
            new("People", EndService, "إنهاء الخدمة", "تنفيذ إجراء إنهاء الخدمة.", 190),
            new("People", Rehire, "إعادة التعيين", "إعادة تعيين شخص منتهي الخدمة.", 200),
            new("People", ViewLifecycle, "عرض دورة حياة الموظف", "عرض حركات التعيين والنقل وإنهاء الخدمة وإعادة التعيين.", 210),
            new("People", ViewHistory, "عرض سجل التغييرات", "عرض سجل تغييرات بيانات الشخص.", 220),
            new("People", Delete, "حذف الشخص", "حذف سجل الشخص وفق سياسة النظام.", 230),
            new("People", ManagePermissions, "إدارة صلاحيات الأشخاص", "إدارة صلاحيات مستخدمي منظومة الأشخاص.", 240)
        };

    public static IReadOnlySet<string> All { get; } =
        Definitions
            .Select(x => x.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
