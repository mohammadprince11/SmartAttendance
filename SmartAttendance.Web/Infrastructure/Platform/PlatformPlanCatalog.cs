namespace SmartAttendance.Web.Infrastructure.Platform;

public static class PlatformPlanCatalog
{
    public sealed record Plan(
        string Code,
        string Name,
        string Description,
        int MaxCompanies,
        int MaxEmployees,
        int MaxDevices,
        IReadOnlyCollection<string> EnabledModules);

    public static readonly IReadOnlyList<Plan> Plans =
    [
        new(
            "Basic",
            "أساسية",
            "للشركات الصغيرة وإدارة الموارد البشرية والحضور والخدمة الذاتية.",
            1,
            100,
            2,
            ["CoreHR", "Attendance", "SelfService", "Mobile"]),
        new(
            "Business",
            "أعمال",
            "تشغيل متكامل للمجموعات المتوسطة مع الرواتب وإدارة الأداء.",
            5,
            1000,
            20,
            ["CoreHR", "Attendance", "Payroll", "SelfService", "Performance", "Mobile"]),
        new(
            "Enterprise",
            "مؤسسات",
            "سعة موسعة وجميع المودلات للشركات والمجموعات الكبيرة.",
            100,
            100000,
            10000,
            PlatformPortalStore.ModuleCatalog.Keys.ToArray())
    ];

    public static Plan? Find(string? code) => Plans.FirstOrDefault(
        plan => string.Equals(plan.Code, code, StringComparison.OrdinalIgnoreCase));
}
