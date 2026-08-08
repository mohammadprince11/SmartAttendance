using Microsoft.EntityFrameworkCore;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Domain.Enums;
using SmartAttendance.Domain.Leave;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// تقديم طلبات **نيابةً عن الموظفين** من شاشة الحضور اليومي على يوميات محدَّدة
/// (تحديد جماعي). لا نوع جديد ولا هجرة: الكتالوج كله موجود بـ
/// <see cref="RequestTypeStore"/> وتغيير المناوبة بـ<see cref="ShiftRequestStore"/> —
/// الفجوة كانت أن الكتالوج مستهلَك ببوابة الموظف وحدها، فـHR لا تقدر تقدّم عن أحد.
///
/// <para><b>قرارا محمد (2026-08-07) — ملزمان:</b>
/// <list type="number">
/// <item><b>الأهلية</b>: النوع يُطبَّق على المستحقّين ويُستثنى غيرهم <b>مع تقرير</b>
/// بمن استُثني ولماذا — لا يُخفى النوع لأن بعض المحدَّدين لا يستحقّونه.</item>
/// <item><b>الاعتماد</b>: <b>فوريّ بلا لجنة</b> — المُقدِّم HR لا الموظف، فلا يمرّ
/// بـ<see cref="ApprovalWorkflowEngine"/> بخلاف طلب البوابة.</item>
/// </list></para>
///
/// <para>⚠️ القرار (2) يعني كتابةً تخصم أرصدةً وتدخل المسير بلا مراجعة بشرية، ولهذا
/// المسار كلّه <b>بمعاملة واحدة</b> و<b>idempotent</b>: أي تقاطع مع طلبٍ قائم من
/// النوع نفسه يُستثني الموظف بدل أن يخصم مرّتين بإرسالٍ مكرّر.</para>
///
/// <para><b>الأثر ليس صفّاً بـ<c>SelfServiceRequests</c> وحده.</b> ذلك الصفّ سجلٌّ
/// إداريّ؛ ما تقرؤه المحرّكات فعلاً هو: <c>LeaveRequests</c> للإجازات (الرصيد
/// والتحليل والمسير)، و<c>RequestType='ExitPermission'</c> بوقتٍ للمغادرات، و
/// <c>WorkFromHome</c>/<c>BusinessTrip</c> لسياق اليوم، و<c>ShiftOverrides</c>
/// لتغيير المناوبة. ولهذا <see cref="ResolveEffect"/>: نوعٌ بلا أثر منفَّذ يُرفض
/// صراحةً بدل أن يُكتب صفّاً ميتاً يظنّه المستخدم مطبَّقاً.</para>
/// </summary>
public static class BulkRequestStore
{
    /// <summary>مصدر تجاوز المناوبة المولَّد من هذا المسار — يميّزه عن طلب الموظف نفسه.</summary>
    public const string ShiftOverrideSource = "تقديم HR";

    /// <summary>ما الذي يُحدثه النوع فعلاً بالمحرّكات؟</summary>
    public enum EffectKind
    {
        /// <summary>بلا أثر منفَّذ بهذا المسار — يُرفض التقديم.</summary>
        None,
        Leave,
        ExitPermission,
        OutOfOffice,
        ShiftChange
    }

    /// <param name="RequestTypeCode">قيمة <c>SelfServiceRequests.RequestType</c> التي تقرؤها المحرّكات.</param>
    /// <param name="Leave">نوع الإجازة بالنطاق (للأثر <see cref="EffectKind.Leave"/> وحده).</param>
    public sealed record Effect(EffectKind Kind, string RequestTypeCode, LeaveType? Leave = null);

    public static readonly Effect NoEffect = new(EffectKind.None, string.Empty);

    /// <summary>
    /// من نوع الكتالوج (اسمٌ عربيّ يحرّره الأدمن) إلى الأثر المنفَّذ. المطابقة بالاسم
    /// لأن الكتالوج داينمك بلا عمود «رمز» — وإضافة عمود تعني هجرة، والقاعدة الحمراء
    /// تمنع توليد الأعمدة بالشفاء الذاتي. نقيّة ومُختبَرة.
    /// </summary>
    public static Effect ResolveEffect(RequestTypeStore.ReqType type)
    {
        var name = (type.Name ?? string.Empty).Trim();
        if (name.Length == 0) return NoEffect;

        // المغادرات أولاً: «مغادرة غير مدفوعة» تحمل كلمة الإجازة بالضوابط لا بالاسم،
        // والترتيب هنا يمنع ابتلاع الإجازة لها لو تغيّرت التسمية.
        if (name.Contains("مغادرة") || name.Contains("خروج"))
            return new Effect(EffectKind.ExitPermission, "ExitPermission");

        if (name.Contains("مهمة عمل") || name.Contains("رحلة عمل"))
            return new Effect(EffectKind.OutOfOffice, "BusinessTrip");

        if (name.Contains("المنزل") || name.Contains("عن بعد") || name.Contains("عن بُعد"))
            return new Effect(EffectKind.OutOfOffice, "WorkFromHome");

        if (name.Contains("إجازة") || name.Contains("اجازة"))
        {
            var leaveType =
                name.Contains("سنوي") ? LeaveType.Annual
                : name.Contains("مرض") ? LeaveType.Sick
                : string.Equals(type.PaidMode, "unpaid", StringComparison.OrdinalIgnoreCase) ? LeaveType.Unpaid
                : LeaveType.Emergency;

            return new Effect(EffectKind.Leave, "Leave", leaveType);
        }

        // الأوفرتايم وما شابهه: لا مستهلِك له بمحرّك الحضور — يُنتَج من البصمات لا من
        // طلبٍ يُكتب على الموظف. تقديمه هنا كان سيكتب صفّاً لا يقرؤه أحد.
        return NoEffect;
    }

    /// <summary>
    /// يدمج تواريخ التحديد المتجاورة بمدياتٍ متّصلة. التحديد بالشاشة صفوف موظف×يوم
    /// مبعثرة، والطلب كيانٌ بمدى — فثلاثة أيام متتالية طلبٌ واحد من 3 أيام لا ثلاثة
    /// طلبات، ويومان متباعدان طلبان. نقيّة ومُختبَرة.
    /// </summary>
    public static List<(DateOnly From, DateOnly To)> MergeRuns(IEnumerable<DateOnly> dates)
    {
        var ordered = dates.Distinct().OrderBy(d => d.DayNumber).ToList();
        var runs = new List<(DateOnly From, DateOnly To)>();

        foreach (var date in ordered)
        {
            if (runs.Count > 0 && runs[^1].To.DayNumber + 1 == date.DayNumber)
                runs[^1] = (runs[^1].From, date);
            else
                runs.Add((date, date));
        }

        return runs;
    }

    public static int TotalDays(IReadOnlyList<(DateOnly From, DateOnly To)> runs) =>
        runs.Sum(run => run.To.DayNumber - run.From.DayNumber + 1);

    public static int LongestRun(IReadOnlyList<(DateOnly From, DateOnly To)> runs) =>
        runs.Count == 0 ? 0 : runs.Max(run => run.To.DayNumber - run.From.DayNumber + 1);

    /// <summary>حقائق موظفٍ مرشَّح، مقروءةً مسبقاً — ليبقى قرار الأهلية نقيّاً.</summary>
    public sealed record Candidate(
        int EmployeeId,
        string EmployeeName,
        IReadOnlyList<(DateOnly From, DateOnly To)> Runs,
        bool MatchesConditions,
        bool HasOverlap,
        decimal RemainingBalance);

    /// <summary>
    /// سبب استثناء المرشَّح، أو <c>null</c> إن كان مستحقّاً. الترتيب مقصود: الشرط
    /// البنيوي (الأهلية) قبل التكرار قبل الحدود قبل الرصيد — فيقرأ المستخدم أوّل
    /// سببٍ حقيقي لا آخره.
    /// </summary>
    public static string? RejectionReason(
        RequestTypeStore.ReqType? type, Effect effect, Candidate candidate)
    {
        if (!candidate.MatchesConditions)
            return "لا تنطبق عليه شروط أهلية النوع";

        if (candidate.HasOverlap)
            return "لديه طلبٌ من النوع نفسه على أيامٍ محدَّدة (لا يُخصم مرّتين)";

        var days = TotalDays(candidate.Runs);

        if (type?.MaxPerRequest is int maxPer && LongestRun(candidate.Runs) > maxPer)
            return $"مدى متّصل يتجاوز حدّ الطلب الواحد ({maxPer} يوم)";

        if (type?.AllowedDays is int allowed && days > allowed)
            return $"عدد الأيام ({days}) يتجاوز المسموح للنوع ({allowed} يوم)";

        if (effect.Kind == EffectKind.Leave && effect.Leave is { } leaveType
            && IraqiLeavePolicy.TrackedTypes.Contains(leaveType)
            && days > candidate.RemainingBalance)
        {
            return $"الرصيد غير كافٍ (المتبقّي {candidate.RemainingBalance:0.#} · المطلوب {days})";
        }

        return null;
    }

    /// <param name="TypeId">نوع من كتالوج الطلبات، أو 0 مع <paramref name="ShiftTypeId"/> لتغيير المناوبة.</param>
    public sealed record Options(
        int TypeId,
        int ShiftTypeId,
        string? FromTime,
        string? ToTime,
        string? Reason);

    /// <summary>
    /// يومية محدَّدة، ومعها نافذتها الزمنية إن كان النوع زمنياً. النافذة **لكل يوم
    /// على حدة**: تأخيرُ الأحد ليس تأخير الاثنين، ونافذةٌ واحدة تُفرَض على أيامٍ
    /// مختلفة تعني طلباً لا يطابق ما جرى فعلاً بأكثرها.
    /// </summary>
    public sealed record Target(int EmployeeId, DateOnly Date, TimeSpan? From = null, TimeSpan? To = null);

    public sealed record Skipped(string EmployeeName, string Reason);

    public sealed record Outcome(
        bool Ok,
        int Employees,
        int Requests,
        IReadOnlyList<Skipped> Skips,
        string Message);

    private static Outcome Fail(string message) =>
        new(false, 0, 0, Array.Empty<Skipped>(), message);

    /// <summary>
    /// ينفّذ التقديم الجماعي: يحسم الأهلية لكل موظف، ثم يكتب المستحقّين <b>بمعاملة
    /// واحدة</b> باعتمادٍ فوريّ، ويعيد تقرير المستثنَين.
    /// </summary>
    public static async Task<Outcome> SubmitAsync(
        ApplicationDbContext db,
        Options options,
        IReadOnlyList<Target> targets,
        string actor)
    {
        if (targets.Count == 0)
            return Fail("لم تُحدَّد أي يومية — أشّر على الصفوف أولاً.");

        RequestTypeStore.ReqType? type = null;
        Effect effect;

        if (options.TypeId > 0)
        {
            type = await RequestTypeStore.GetTypeAsync(db, options.TypeId);
            if (type is null || !type.IsActive)
                return Fail("نوع الطلب غير موجود أو غير فعّال.");

            effect = ResolveEffect(type);
            if (effect.Kind == EffectKind.None)
            {
                return Fail(
                    $"النوع «{type.Name}» بلا أثرٍ منفَّذ بمحرّك الحضور، فلا يُقدَّم من هنا " +
                    "(الأوفرتايم مثلاً يُشتقّ من البصمات لا من طلبٍ يُكتب على الموظف).");
            }
        }
        else if (options.ShiftTypeId > 0)
        {
            var shifts = await ShiftTypeStore.ListAsync(db);
            if (!shifts.Any(s => s.Id == options.ShiftTypeId && s.IsActive))
                return Fail("المناوبة المطلوبة غير موجودة أو غير فعّالة.");

            effect = new Effect(EffectKind.ShiftChange, ShiftRequestStore.RequestType);
        }
        else
        {
            return Fail("اختر نوع الطلب أو المناوبة البديلة.");
        }

        var timed = effect.Kind == EffectKind.ExitPermission || type is { NeedsTime: true };

        // نافذة كل يوم: ما جاء مع اليومية نفسها، وإلا النافذة العامة بالنموذج (حالة
        // اليوم الواحد). القاموس هو مصدر الوقت وقت الكتابة.
        var windows = new Dictionary<(int, DateOnly), (TimeSpan From, TimeSpan To)>();

        if (timed)
        {
            TimeSpan.TryParse(options.FromTime, out var globalFrom);
            TimeSpan.TryParse(options.ToTime, out var globalTo);

            foreach (var target in targets)
            {
                var from = target.From ?? (globalFrom == default ? (TimeSpan?)null : globalFrom);
                var to = target.To ?? (globalTo == default ? (TimeSpan?)null : globalTo);

                if (from is null || to is null)
                    return Fail($"النوع زمنيّ — يومية {target.Date:yyyy-MM-dd} بلا وقت بداية أو نهاية.");

                if (to <= from)
                    return Fail($"يومية {target.Date:yyyy-MM-dd}: وقت النهاية يجب أن يكون بعد وقت البداية.");

                windows[(target.EmployeeId, target.Date)] = (from.Value, to.Value);
            }
        }

        // النوع الزمنيّ **لا تُدمج أيامه**: لكل يومٍ نافذتُه، ودمجُها بمدىً واحد يُلغي
        // الأوقات المختلفة. غير الزمنيّ يُدمج (ثلاثة أيام متتالية = إجازة من 3 أيام).
        var runsByEmployee = targets
            .GroupBy(t => t.EmployeeId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<(DateOnly From, DateOnly To)>)(
                timed
                    ? g.Select(t => t.Date).Distinct().OrderBy(d => d.DayNumber)
                       .Select(d => (d, d)).ToList()
                    : MergeRuns(g.Select(t => t.Date))));

        var names = await EmployeeNamesAsync(db, runsByEmployee.Keys.ToList());

        var conditions = HrConditions.Deserialize(type?.ConditionsJson ?? string.Empty);
        var factRows = (await HrConditionFacts.LoadAsync(db))
            .Where(row => runsByEmployee.ContainsKey(row.Id))
            .ToDictionary(row => row.Id);
        var today = DateOnly.FromDateTime(DateTime.Today);

        var eligible = new List<Candidate>();
        var skips = new List<Skipped>();

        foreach (var (employeeId, runs) in runsByEmployee.OrderBy(pair => pair.Key))
        {
            var name = names.GetValueOrDefault(employeeId, $"#{employeeId}");

            // نوعٌ بلا شروط متاحٌ للجميع (matchWhenEmpty) — نفس افتراض بوابة الموظف،
            // وإلا اختفى كل نوعٍ عن كل موظف لحظة النشر.
            var matches = factRows.TryGetValue(employeeId, out var row)
                ? HrConditions.Matches(conditions, HrConditionFacts.Build(row, today), matchWhenEmpty: true)
                : true;

            // الفحص **بكل مدىً على حدة** لا بالامتداد الكلّي: مغادرةٌ قائمة يوم 11
            // كانت تُلغي طلباً على 9 و13 لأن الامتداد يبتلع ما بينهما.
            var overlap = false;
            foreach (var run in runs)
            {
                if (await HasOverlappingRequestAsync(db, employeeId, effect, run.From, run.To))
                {
                    overlap = true;
                    break;
                }
            }

            var remaining = 0m;
            if (effect.Kind == EffectKind.Leave && effect.Leave is { } leaveType
                && IraqiLeavePolicy.TrackedTypes.Contains(leaveType))
            {
                var balances = await LeaveBalanceCalculator.ForEmployeeAsync(
                    db, employeeId, runs[0].From.Year);
                remaining = balances.FirstOrDefault(b => b.Type == leaveType)?.Remaining ?? 0m;
            }

            var candidate = new Candidate(employeeId, name, runs, matches, overlap, remaining);
            var reason = RejectionReason(type, effect, candidate);

            if (reason is null) eligible.Add(candidate);
            else skips.Add(new Skipped(name, reason));
        }

        if (eligible.Count == 0)
        {
            return new Outcome(
                false, 0, 0, skips,
                $"لم يُقدَّم أي طلب — كل المحدَّدين ({skips.Count}) مستثنَون. التفاصيل بالتقرير.");
        }

        var label = type?.Name ?? "تغيير مناوبة";
        var requestReason = string.IsNullOrWhiteSpace(options.Reason)
            ? $"{label} — مقدَّم من الموارد البشرية نيابةً عن الموظف"
            : options.Reason.Trim();

        var requests = 0;

        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            foreach (var candidate in eligible)
            {
                foreach (var run in candidate.Runs)
                {
                    var days = run.To.DayNumber - run.From.DayNumber + 1;

                    var window = windows.TryGetValue((candidate.EmployeeId, run.From), out var w)
                        ? w
                        : default;

                    await InsertApprovedRequestAsync(
                        db, candidate.EmployeeId, effect, options.ShiftTypeId,
                        run.From, run.To,
                        timed ? window.From : null, timed ? window.To : null,
                        days, requestReason, actor, label);

                    if (effect.Kind == EffectKind.Leave && effect.Leave is { } leaveType)
                    {
                        // الأثر الحقيقي للإجازة: صفّ `LeaveRequests` — هو ما يقرؤه
                        // الرصيد ومحرّك التحليل والمسير. الصفّ الإداري وحده لا يخصم شيئاً.
                        db.LeaveRequests.Add(new LeaveRequest
                        {
                            EmployeeId = candidate.EmployeeId,
                            LeaveType = leaveType,
                            Status = LeaveStatus.Approved,
                            FromDate = run.From,
                            ToDate = run.To,
                            Reason = requestReason,
                            CreatedBy = actor
                        });
                    }

                    if (effect.Kind == EffectKind.ShiftChange)
                    {
                        await InsertShiftOverrideAsync(
                            db, candidate.EmployeeId, options.ShiftTypeId, run.From, run.To);
                    }

                    requests++;
                }
            }

            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }

        // لا إعادة تحليل تلقائية هنا عمداً: إعادة بناء الشهر لكل موظفٍ محدَّد عمليةٌ
        // بحجم الشهر × عدد الموظفين. اليوميات المتأثرة تصير «لم تُحلَّل ✗» بفضل حساب
        // البائتة، فيراها المستخدم ويشغّل «تحديث الحضور» ضربةً واحدة.
        var message =
            $"تم تقديم واعتماد {requests} طلب «{label}» لـ{eligible.Count} موظفاً"
            + (skips.Count > 0 ? $" — واستُثني {skips.Count} (التفاصيل أدناه)" : "")
            + ". شغّل «تحديث الحضور» ليُحتسب الأثر باليوميات.";

        return new Outcome(true, eligible.Count, requests, skips, message);
    }

    private static async Task<Dictionary<int, string>> EmployeeNamesAsync(
        ApplicationDbContext db, IReadOnlyList<int> employeeIds)
    {
        if (employeeIds.Count == 0) return new Dictionary<int, string>();

        return await db.Employees.AsNoTracking()
            .Where(e => employeeIds.Contains(e.Id))
            .Select(e => new { e.Id, e.FullName, e.EmployeeNo })
            .ToDictionaryAsync(e => e.Id, e => $"{e.FullName} ({e.EmployeeNo})");
    }

    /// <summary>
    /// idempotency: هل للموظف طلبٌ من النوع نفسه (غير مرفوض) يتقاطع مع المدى؟
    /// للإجازات يُفحص <c>LeaveRequests</c> أيضاً لأنه موضع الخصم الفعلي.
    /// </summary>
    private static async Task<bool> HasOverlappingRequestAsync(
        ApplicationDbContext db, int employeeId, Effect effect, DateOnly from, DateOnly to)
    {
        var existing = await HrmsDatabase.ScalarAsync<int>(
            db,
            """
SELECT COUNT(1) FROM SelfServiceRequests
WHERE EmployeeId = @Employee
  AND RequestType = @Type
  AND Status <> N'Rejected'
  AND COALESCE(FromDate, RequestDate) <= @To
  AND COALESCE(ToDate, FromDate, RequestDate) >= @From;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Employee", employeeId);
                HrmsDatabase.AddParameter(command, "@Type", effect.RequestTypeCode);
                HrmsDatabase.AddParameter(command, "@From", from.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@To", to.ToDateTime(TimeOnly.MinValue));
            });

        if (existing > 0) return true;

        if (effect.Kind != EffectKind.Leave || effect.Leave is not { } leaveType) return false;

        return await db.LeaveRequests.AsNoTracking()
            .AnyAsync(l => l.EmployeeId == employeeId
                        && l.LeaveType == leaveType
                        && l.Status != LeaveStatus.Rejected
                        && l.Status != LeaveStatus.Cancelled
                        && l.FromDate <= to
                        && l.ToDate >= from);
    }

    private static Task InsertApprovedRequestAsync(
        ApplicationDbContext db, int employeeId, Effect effect, int shiftTypeId,
        DateOnly from, DateOnly to, TimeSpan? startTime, TimeSpan? endTime,
        int days, string reason, string actor, string label) =>
        HrmsDatabase.ExecuteAsync(
            db,
            """
INSERT INTO SelfServiceRequests
    (EmployeeId, RequestType, RequestDate, FromDate, ToDate, StartTime, EndTime, Reason,
     Status, CurrentStep, CreatedBy, ReviewedBy, ReviewNote, HrStatus, HrReviewedBy,
     HrReviewedAt, UpdatedAt, DaysCount, ShiftTypeId)
VALUES
    (@Employee, @Type, CAST(GETDATE() AS date), @From, @To, @StartTime, @EndTime, @Reason,
     N'Approved', N'Completed', @Actor, @Actor, @Note, N'Approved', @Actor,
     SYSUTCDATETIME(), SYSUTCDATETIME(), @Days, @Shift);

DECLARE @RequestId int = SCOPE_IDENTITY();

INSERT INTO ApprovalHistories (RequestId, StepName, Action, ActionBy, Notes)
VALUES (@RequestId, N'Submission', N'Submitted', @Actor, @Reason),
       (@RequestId, N'HR', N'Approved', @Actor, @Note);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Employee", employeeId);
                HrmsDatabase.AddParameter(command, "@Type", effect.RequestTypeCode);
                HrmsDatabase.AddParameter(command, "@From", from.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@To", to.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@StartTime", (object?)startTime ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@EndTime", (object?)endTime ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@Reason", reason);
                HrmsDatabase.AddParameter(command, "@Actor", actor);
                HrmsDatabase.AddParameter(
                    command, "@Note",
                    $"{label} — اعتماد فوريّ: مقدَّم من الموارد البشرية نيابةً عن الموظف (بلا لجنة).");
                HrmsDatabase.AddParameter(command, "@Days", days);
                HrmsDatabase.AddParameter(
                    command, "@Shift",
                    effect.Kind == EffectKind.ShiftChange ? shiftTypeId : (object)DBNull.Value);
            });

    /// <summary>تجاوز المناوبة للمدى — idempotent بنفس شرط <see cref="ShiftRequestStore"/>.</summary>
    private static async Task InsertShiftOverrideAsync(
        ApplicationDbContext db, int employeeId, int shiftTypeId, DateOnly from, DateOnly to)
    {
        await ShiftOverrideStore.EnsureAsync(db);

        await HrmsDatabase.ExecuteAsync(
            db,
            """
IF NOT EXISTS (SELECT 1 FROM ShiftOverrides
               WHERE EmployeeId = @Employee AND ShiftTypeId = @Shift
                 AND FromDate = @From AND ToDate = @To AND Source = @Source)
    INSERT INTO ShiftOverrides (EmployeeId, ShiftTypeId, FromDate, ToDate, Source)
    VALUES (@Employee, @Shift, @From, @To, @Source);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Employee", employeeId);
                HrmsDatabase.AddParameter(command, "@Shift", shiftTypeId);
                HrmsDatabase.AddParameter(command, "@From", from.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@To", to.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@Source", ShiftOverrideSource);
            });
    }
}
