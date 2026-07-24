using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// محرك تصنيف البصمات بالأسبقية الزمنية (نمط كيان — اشتقاق دخول/خروج من ترتيب البصم):
/// بصمات اليوم تُرتَّب تصاعدياً بالوقت ثم تُصنَّف بالتناوب — الأولى «دخول»، الثانية «خروج»،
/// الثالثة «دخول»… فإدخال بصمة مفقودة يعيد تصنيف اليوم كاملاً حسب موقعها الزمني.
/// مثال: بصمة 17:00 وحدها = دخول؛ بإضافة 08:00 تصبح 08:00 دخولاً و17:00 خروجاً.
/// نقي بلا حالة — يُستخدَم للمعاينة الحيّة وللتخزين الرسمي عند الإرسال.
/// </summary>
public static class PunchTypingEngine
{
    public sealed record TypedPunch(DateTime At, string Type, int Order)
    {
        public string TypeLabel => Type == "Out" ? "خروج" : "دخول";
    }

    /// <summary>يصنّف مجموعة أوقات بصم ليوم: ترتيب تصاعدي ثم تناوب دخول/خروج.</summary>
    public static List<TypedPunch> Derive(IEnumerable<DateTime> times)
    {
        var ordered = times.OrderBy(t => t).ToList();
        var result = new List<TypedPunch>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
            result.Add(new TypedPunch(ordered[i], i % 2 == 0 ? "In" : "Out", i + 1));
        return result;
    }

    /// <summary>نوع بصمة جديدة لو أُدخلت بين البصمات الموجودة (In/Out حسب موقعها الزمني).</summary>
    public static string DeriveTypeFor(IEnumerable<DateTime> existing, DateTime newPunch)
    {
        var all = existing.ToList();
        all.Add(newPunch);
        var typed = Derive(all);
        // أول بصمة بنفس اللحظة هي المُدخَلة (قد تتساوى مع أخرى نظرياً).
        var match = typed.First(p => p.At == newPunch);
        return match.Type;
    }

    /// <summary>
    /// أوقات بصم يوم موظف من AttendanceRecords: كل صف يُسهم بـ CheckIn، ويُسهم بـ CheckOut
    /// أيضاً إن كان مختلفاً (زوج فعلي من جهاز). المكرّرات تُزال. لا يشمل المحذوف.
    /// </summary>
    public static async Task<List<DateTime>> DayPunchTimesAsync(
        ApplicationDbContext db, int employeeId, DateOnly date)
    {
        var rows = await HrmsDatabase.QueryAsync(
            db,
            """
SELECT CheckIn, CheckOut
FROM AttendanceRecords
WHERE EmployeeId = @Emp AND AttendanceDate = @Date AND ISNULL(IsDeleted, 0) = 0;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Emp", employeeId);
                HrmsDatabase.AddParameter(command, "@Date", date);
            },
            reader => new
            {
                CheckIn = HrmsDatabase.GetDateTime(reader, "CheckIn"),
                CheckOut = HrmsDatabase.GetDateTime(reader, "CheckOut")
            });

        var times = new List<DateTime>();
        foreach (var r in rows)
        {
            if (r.CheckIn is { } ci) times.Add(ci);
            if (r.CheckOut is { } co && (r.CheckIn is not { } c2 || co != c2)) times.Add(co);
        }
        return times.Distinct().OrderBy(t => t).ToList();
    }
}
