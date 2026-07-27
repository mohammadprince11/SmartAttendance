using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// طلب مناوبة من الخدمة الذاتية (إعداد <c>RequestableFromEss</c> بأنواع المناوبات):
/// الموظف يطلب مناوبةً معلَّمة «قابلة للطلب» لمدى أيام ⟵ صف <c>SelfServiceRequests</c>
/// (النوع ShiftRequest بعمود ShiftTypeId) يمرّ بمحرك الموافقات كسائر الطلبات، وعند
/// الاعتماد النهائي يُطبَّق الأثر تجاوزاً مؤقتاً (<c>ShiftOverrides</c> بمصدر «طلب موظف»)
/// فيقرؤه محرك التحليل بأولويته القائمة. التطبيق idempotent — لا تجاوز مكرر لنفس الطلب.
/// </summary>
public static class ShiftRequestStore
{
    public const string RequestType = "ShiftRequest";
    public const string OverrideSource = "طلب موظف";

    public sealed record MyRow(int Id, string ShiftName, DateOnly? FromDate, DateOnly? ToDate,
        string Status, string? CurrentStep, DateTime CreatedAt);

    /// <summary>المناوبات المتاحة للطلب الذاتي: نشطة ومعلَّمة RequestableFromEss.</summary>
    public static async Task<List<(int Id, string Name)>> RequestableShiftsAsync(ApplicationDbContext db)
    {
        await ShiftTypeStore.EnsureAsync(db);
        return (await ShiftTypeStore.ListAsync(db))
            .Where(s => s.IsActive && s.RequestableFromEss)
            .Select(s => (s.Id, s.Name))
            .ToList();
    }

    /// <summary>
    /// تقديم الطلب وبدء سريان الموافقات. يتحقق أن المناوبة فعلاً قابلة للطلب
    /// (لا ثقة بقيمة النموذج وحدها). يعيد معرّف الطلب أو 0.
    /// </summary>
    public static async Task<int> SubmitAsync(ApplicationDbContext db, int employeeId,
        int shiftTypeId, DateOnly from, DateOnly to, string? reason, string actor)
    {
        if (employeeId <= 0) return 0;
        if (to < from) (from, to) = (to, from);

        var allowed = (await RequestableShiftsAsync(db)).Any(s => s.Id == shiftTypeId);
        if (!allowed) return 0;

        var requestId = await HrmsDatabase.ScalarAsync<int>(
            db,
            """
INSERT INTO SelfServiceRequests
    (EmployeeId, RequestType, RequestDate, FromDate, ToDate, ShiftTypeId, Reason, Status, CurrentStep, CreatedBy)
VALUES
    (@Employee, @Type, CAST(GETDATE() AS date), @From, @To, @Shift, @Reason, N'Pending', N'Direct Manager', @Actor);

DECLARE @RequestId int = SCOPE_IDENTITY();

INSERT INTO ApprovalHistories (RequestId, StepName, Action, ActionBy, Notes)
VALUES (@RequestId, N'Submission', N'Submitted', @Actor, @Reason);

INSERT INTO SystemNotifications (Title, Message, TargetRole, Url)
VALUES (N'طلب مناوبة جديد', N'يوجد طلب مناوبة بانتظار الموافقة', N'HR', N'/Approvals');

SELECT @RequestId;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Employee", employeeId);
                HrmsDatabase.AddParameter(command, "@Type", RequestType);
                HrmsDatabase.AddParameter(command, "@From", from.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@To", to.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@Shift", shiftTypeId);
                HrmsDatabase.AddParameter(command, "@Reason", (object?)reason ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@Actor", actor);
            });

        if (requestId > 0)
            await ApprovalWorkflowEngine.StartAsync(db, requestId, RequestType, employeeId);

        return requestId;
    }

    /// <summary>طلبات مناوبات الموظف نفسه (الأحدث أولاً) لعرضها ببوابته.</summary>
    public static Task<List<MyRow>> ListMineAsync(ApplicationDbContext db, int employeeId) =>
        HrmsDatabase.QueryAsync(
            db,
            """
SELECT r.Id, ISNULL(st.Name, N'—') AS ShiftName, r.FromDate, r.ToDate,
       r.Status, r.CurrentStep, r.CreatedAt
FROM SelfServiceRequests r
LEFT JOIN ShiftTypes st ON st.Id = r.ShiftTypeId
WHERE r.EmployeeId = @Employee AND r.RequestType = @Type
ORDER BY r.Id DESC;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Employee", employeeId);
                HrmsDatabase.AddParameter(command, "@Type", RequestType);
            },
            reader => new MyRow(
                HrmsDatabase.GetInt(reader, "Id"),
                HrmsDatabase.GetString(reader, "ShiftName"),
                HrmsDatabase.GetDateOnly(reader, "FromDate"),
                HrmsDatabase.GetDateOnly(reader, "ToDate"),
                HrmsDatabase.GetString(reader, "Status"),
                HrmsDatabase.GetString(reader, "CurrentStep") is { Length: > 0 } step ? step : null,
                HrmsDatabase.GetDateTime(reader, "CreatedAt") ?? default));

    /// <summary>
    /// تطبيق أثر الطلب المعتمد: تجاوز مؤقت بمدى الطلب. يُستدعى من مسار الاعتماد
    /// (كما <c>ApplyIfFinancialAsync</c>) — يتجاهل غير طلبات المناوبة، وidempotent:
    /// وجود تجاوز سابق بنفس (الموظف، المناوبة، المدى، المصدر) يمنع التكرار.
    /// </summary>
    public static async Task<bool> ApplyIfShiftRequestAsync(ApplicationDbContext db, int requestId)
    {
        await ShiftOverrideStore.EnsureAsync(db);

        var applied = await HrmsDatabase.ScalarAsync<int>(
            db,
            """
DECLARE @Employee int, @Shift int, @From date, @To date;

SELECT @Employee = EmployeeId, @Shift = ShiftTypeId, @From = FromDate, @To = ISNULL(ToDate, FromDate)
FROM SelfServiceRequests
WHERE Id = @Request AND RequestType = @Type AND Status = N'Approved';

IF @Employee IS NULL OR @Shift IS NULL OR @From IS NULL
    SELECT 0;
ELSE IF EXISTS (SELECT 1 FROM ShiftOverrides
                WHERE EmployeeId = @Employee AND ShiftTypeId = @Shift
                  AND FromDate = @From AND ToDate = @To AND Source = @Source)
    SELECT 0;
ELSE
BEGIN
    INSERT INTO ShiftOverrides (EmployeeId, ShiftTypeId, FromDate, ToDate, Source)
    VALUES (@Employee, @Shift, @From, @To, @Source);
    SELECT 1;
END;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Request", requestId);
                HrmsDatabase.AddParameter(command, "@Type", RequestType);
                HrmsDatabase.AddParameter(command, "@Source", OverrideSource);
            });

        return applied > 0;
    }
}
