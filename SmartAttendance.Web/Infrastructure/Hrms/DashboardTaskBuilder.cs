namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// «مهامك اليوم» — قلب لوحة البيانات الجديدة (Design System v1، نموذج
/// <c>docs/prototypes/dashboard-redesign-v1.html</c>): بدل أن يبحث المدير في 18
/// مؤشراً عمّا يحتاج فعله، **النظام يخبره بما ينقص** ويعطيه زر ذهاب مباشراً.
///
/// نقي بالكامل: يأخذ أعداداً محسوبة ويرتّبها — بلا قاعدة بيانات ولا HttpContext،
/// فكل قواعد الظهور والترتيب تُختبر بلا بيئة.
/// </summary>
public static class DashboardTaskBuilder
{
    /// <summary>لهجة العرض — تُترجم لألوان الهوية بالواجهة لا هنا.</summary>
    public enum TaskTone
    {
        Accent = 0,
        Warning = 1,
        Danger = 2
    }

    public sealed record DashboardTask(
        string Key,
        string Title,
        string Subtitle,
        string ActionLabel,
        string Url,
        TaskTone Tone,
        int Count);

    /// <summary>الأعداد الخام كما تُقرأ من المخازن.</summary>
    public readonly record struct Counts(
        int PendingRequests,
        int PendingBiometricKeys,
        int ExpiringContracts,
        int UnexcusedAbsenceToday);

    /// <summary>
    /// يبني قائمة المهام مرتّبة بالإلحاح. مهمة بعدّاد صفر **لا تظهر إطلاقاً** —
    /// لوحة تعرض «0 طلبات معلّقة» تُدرّب المستخدم على تجاهلها.
    /// </summary>
    public static IReadOnlyList<DashboardTask> Build(Counts counts)
    {
        var tasks = new List<DashboardTask>();

        // الأمن أولاً: مفتاح بصمة غير معتمد يعني موظفاً لا يستطيع البصم، أو
        // تسجيلاً مشبوهاً ينتظر من يراه.
        if (counts.PendingBiometricKeys > 0)
        {
            tasks.Add(new DashboardTask(
                "biometric",
                $"{counts.PendingBiometricKeys} مفتاح بصمة/وجه بانتظار الاعتماد",
                "لن تعمل بصمة أصحابها قبل موافقتك",
                "راجعها",
                "/BiometricKeys",
                TaskTone.Danger,
                counts.PendingBiometricKeys));
        }

        // الموافقات: تعطّل الموظف وتتراكم.
        if (counts.PendingRequests > 0)
        {
            tasks.Add(new DashboardTask(
                "approvals",
                $"{counts.PendingRequests} طلباً بانتظار اعتمادك",
                "إجازات ومغادرات وطلبات مالية بانتظار قرارك",
                "اعتمد الآن",
                "/Approvals",
                TaskTone.Warning,
                counts.PendingRequests));
        }

        // الغياب بلا مبرر اليوم: يحتاج معالجة بنفس اليوم قبل ترحيله للمسير.
        if (counts.UnexcusedAbsenceToday > 0)
        {
            tasks.Add(new DashboardTask(
                "absence",
                $"{counts.UnexcusedAbsenceToday} غياباً اليوم بلا مبرر",
                "عالجها قبل الاعتماد الشهري وإلا رُحّلت للمسير كما هي",
                "افتح الحضور",
                "/DayAttendance",
                TaskTone.Warning,
                counts.UnexcusedAbsenceToday));
        }

        // العقود: مهلتها أطول، فتأتي أخيراً.
        if (counts.ExpiringContracts > 0)
        {
            tasks.Add(new DashboardTask(
                "contracts",
                $"{counts.ExpiringContracts} عقداً ينتهي قريباً",
                "قرار التجديد قبل انتهاء المدة",
                "راجع العقود",
                "/Employees",
                TaskTone.Accent,
                counts.ExpiringContracts));
        }

        return tasks;
    }

    /// <summary>نص الحالة أعلى اللوحة — يعكس الواقع بلا تهويل ولا طمأنة كاذبة.</summary>
    public static string BuildHeadline(IReadOnlyList<DashboardTask> tasks) =>
        tasks.Count == 0
            ? "لا شيء ينتظر قرارك الآن"
            : $"{tasks.Count} {(tasks.Count == 1 ? "أمر يحتاج" : "أمور تحتاج")} نظرتك";
}
