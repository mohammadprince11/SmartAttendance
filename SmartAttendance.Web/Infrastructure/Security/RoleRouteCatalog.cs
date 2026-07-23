namespace SmartAttendance.Web.Infrastructure.Security;

/// <summary>
/// مصدر الحقيقة الوحيد لخريطة «الدور ← المسارات المسموحة» بالقوائم التوافقية الثابتة.
///
/// سبب وجوده: كانت الخريطة محبوسة داخل <see cref="RoleSecurityMiddleware"/> بينما
/// القائمة الجانبية تقرّر الإظهار بمعيار آخر (رمز صفحة واحد لكل مجموعة)، فكان
/// المستخدم غير الأدمن يرى روابط تُمنع عند النقر — 13 من 15 رابط حضور لموظف الموارد
/// البشرية مثلاً. الآن يستهلك الطرفان هذا الصنف نفسه فيختفي الرابط الذي سيُمنع.
///
/// ملاحظة: الحالات التي تحتاج سياق الطلب (ملفي الشخصي المرتبط بموظف، ودور «موظف»
/// الذي يفحص ملكية الطلب) تبقى في الحارس — هنا القوائم الثابتة فقط.
/// </summary>
public static class RoleRouteCatalog
{
    public const string Admin = "Admin";
    public const string HrManager = "HR Manager";
    public const string HrOfficer = "HR Officer";
    public const string BranchManager = "Branch Manager";
    public const string FinanceViewer = "Finance Viewer";
    public const string Employee = "Employee";

    /// <summary>مسارات متاحة لكل دور مسجّل الدخول.</summary>
    public static readonly string[] Common =
    {
        "/",
        "/index",
        "/account",
        "/settings"
    };

    public static readonly string[] HrManagerRoutes =
    {
        "/organization",
        "/organizationsettings",
        "/alerts",
        "/leavebalances",
        "/assetsmanagement",
        "/peoplereports",
        "/employeetasks",
        "/employees",
        "/myprofile",
        "/useraccess",
        "/devices",
        "/shifts",
        "/shifttypes",
        "/attendancesettings",
        "/dayattendance",
        "/shiftrules",
        "/attendancerecommendations",
        "/shiftassignments",
        // الشاشة التشغيلية الأم: المسارات البديلة (المعالجة/التصحيحات/الاستيراد)
        // كلها تُعيد التوجيه إليها، فبدونها تنتهي كلها بـ«لا صلاحية».
        "/attendanceoperations",
        // صفحات أُضيفت للمودل لاحقاً ولم تُحدَّث قوائم الأدوار معها
        "/shiftoverrides",
        "/roster",
        "/employeegeolocations",
        "/attendanceviewer",
        "/monthattendance",
        "/employeeshifts",
        "/attendancerecords",
        "/attendanceprocessing",
        "/attendancecorrections",
        "/attendanceimports",
        "/holidays",
        "/leaverequests",
        "/selfservices",
        "/approvals",
        "/auditlogs",
        "/systemmaintenance",
        "/employeepermissions"
    };

    public static readonly string[] HrOfficerRoutes =
    {
        "/organization",
        "/alerts",
        "/leavebalances",
        "/assetsmanagement",
        "/peoplereports",
        "/employeetasks",
        "/employees",
        "/myprofile",
        "/attendancerecords",
        "/attendanceoperations",
        "/attendanceprocessing",
        "/attendancecorrections",
        "/attendanceimports",
        "/holidays",
        "/leaverequests",
        "/selfservices",
        "/approvals"
    };

    public static readonly string[] BranchManagerRoutes =
    {
        "/organization",
        "/employees",
        "/myprofile",
        "/attendancerecords",
        "/attendanceoperations",
        "/attendanceprocessing",
        "/attendancecorrections",
        "/leaverequests",
        "/selfservices"
    };

    public static readonly string[] FinanceViewerRoutes =
    {
        "/organization"
    };

    /// <summary>قائمة مسارات الدور، أو null إن لم يكن دوراً بقائمة ثابتة.</summary>
    public static string[]? RoutesFor(string? role)
    {
        if (string.IsNullOrWhiteSpace(role)) return null;

        role = role.Trim();

        if (role.Equals(HrManager, StringComparison.OrdinalIgnoreCase)) return HrManagerRoutes;
        if (role.Equals(HrOfficer, StringComparison.OrdinalIgnoreCase)) return HrOfficerRoutes;
        if (role.Equals(BranchManager, StringComparison.OrdinalIgnoreCase)) return BranchManagerRoutes;
        if (role.Equals(FinanceViewer, StringComparison.OrdinalIgnoreCase)) return FinanceViewerRoutes;

        return null;
    }

    public static bool IsAdmin(string? role) =>
        !string.IsNullOrWhiteSpace(role) &&
        role.Trim().Equals(Admin, StringComparison.OrdinalIgnoreCase);

    /// <summary>هل يطابق المسار أحد البادئات المسموحة؟</summary>
    public static bool Matches(string path, params string[] allowedPrefixes)
    {
        foreach (var allowed in allowedPrefixes)
        {
            if (path == allowed || path.StartsWith(allowed + "/", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// هل يصل هذا الدور لهذا المسار بالقوائم الثابتة؟ يستخدمه الحارس للإنفاذ
    /// والقائمة الجانبية لإخفاء ما سيُمنع. الأدمن دائماً مسموح.
    /// المسار يجب أن يكون بحروف صغيرة ويبدأ بـ «/».
    /// </summary>
    public static bool IsAllowed(string? role, string path)
    {
        if (IsAdmin(role)) return true;
        if (string.IsNullOrWhiteSpace(path)) return false;

        path = path.ToLowerInvariant();

        if (Matches(path, Common)) return true;

        var routes = RoutesFor(role);

        return routes != null && Matches(path, routes);
    }
}
