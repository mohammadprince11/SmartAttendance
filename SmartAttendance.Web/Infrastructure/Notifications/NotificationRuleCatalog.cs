namespace SmartAttendance.Web.Infrastructure.Notifications;

/// <summary>Observed per-rule UI contract, independent of whether its business source is connected.</summary>
public static class NotificationRuleCatalog
{
    public const string SameWorkUnit = "الموظفون في نفس وحدة العمل";
    public const string SameHierarchy = "الموظفون في نفس الوحدة الهرمية";
    public const string SameLocation = "الموظفون في نفس الموقع الوظيفي";
    private static readonly string[] Operation = NotificationRoutingPolicy.Audiences.Where(a => a != NotificationRoutingPolicy.Group).ToArray();
    private static readonly string[] Announcement = [NotificationRoutingPolicy.AllEmployees, SameWorkUnit, SameHierarchy, SameLocation,
        NotificationRoutingPolicy.Self, NotificationRoutingPolicy.Manager, NotificationRoutingPolicy.SelfAndManager,
        NotificationRoutingPolicy.Specific, NotificationRoutingPolicy.Group, NotificationRoutingPolicy.Supervisors,
        NotificationRoutingPolicy.Everyone, NotificationRoutingPolicy.ManagerAndSupervisors, NotificationRoutingPolicy.SelfAndSupervisors];

    public static IReadOnlyList<string> Audiences(string name) => NotificationRuleSettings.ReferenceName(name) switch
    {
        "عيد ميلاد موظف" or "ذكرى عمل موظف" or "الترحيب بموظف جديد" or "وداع موظف" => Announcement,
        "العطل" => [NotificationRoutingPolicy.AllEmployees, NotificationRoutingPolicy.Group, NotificationRoutingPolicy.Supervisors],
        "فترة التجربة" or "سن التقاعد" or "عقود الموظفين" or "انتهاء صلاحية الوثيقة" or "تمديد العقد" => NotificationRoutingPolicy.Audiences,
        "مقابلات نهاية الخدمة" or "رضا الموظفين" => [NotificationRoutingPolicy.Supervisors],
        "اقتراحات وشكاوي" => [NotificationRoutingPolicy.Specific, NotificationRoutingPolicy.Group, NotificationRoutingPolicy.Supervisors],
        "مشاركة التقارير" => ["المستلم", "المستلم والمسؤولون"],
        "مشاركة الوثائق" => [NotificationRoutingPolicy.Self, NotificationRoutingPolicy.SelfAndSupervisors],
        "الإنتخابات و إستطلاعات الرأي" => [NotificationRoutingPolicy.Voters, NotificationRoutingPolicy.VotersAndSupervisors],
        _ => Operation
    };

    public static bool AllowsAudience(string name, string audience) =>
        NotificationRoutingPolicy.ValidAudience(audience) && Audiences(name).Contains(audience == NotificationRoutingPolicy.Employee ? NotificationRoutingPolicy.Self : audience);

    public static bool HasDays(string name) => NotificationRuleSettings.ReferenceName(name) is
        "العطل" or "انتهاء صلاحية الوثيقة" or "فترة التجربة" or "سن التقاعد" or "عقود الموظفين";

    public static bool AllowsSettings(string name, NotificationRuleSettings settings) =>
        AllowsAudience(name, settings.Audience) && (HasDays(name) || settings.DaysBefore == 0);

    public static IReadOnlyList<(string Value, string Label)> WelcomeBases { get; } =
        [("CreatedAt", "تاريخ الإضافة"), ("JoiningDate", "تاريخ الانضمام"), ("HireDate", "تاريخ التعيين"), ("ContractStart", "تاريخ بدء العقد")];
}
