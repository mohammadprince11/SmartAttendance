using SmartAttendance.Domain.Enums;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// يطبّق الأثر الحقيقي للطلب بعد الاعتماد النهائي. هذا هو الجسر بين
/// SelfServiceRequests وبين الإجازات/المغادرات/المناوبات/الرواتب واليومية.
/// </summary>
public static class ApprovedAttendanceRequestEffectStore
{
    private sealed record RequestRow(
        int Id, int EmployeeId, string RequestType, DateOnly FromDate, DateOnly ToDate,
        TimeSpan? StartTime, TimeSpan? EndTime, int? ShiftTypeId, string Reason);

    public sealed record Outcome(bool Applied, string Message);

    public static async Task<Outcome> ApplyAsync(
        ApplicationDbContext db, CompanyScope scope, int requestId, string actor)
    {
        if (!await AttendanceRequestPolicy.GetApplyEffectsAsync(db))
            return new Outcome(false, "تطبيق أثر الطلبات المعتمدة معطّل من إعدادات الحضور.");

        var request = await LoadApprovedAsync(db, requestId);
        if (request is null) return new Outcome(false, string.Empty);

        var resolution = await ResolveAsync(db, request.RequestType);
        var applied = resolution.Key switch
        {
            "LeaveRequest" => await ApplyLeaveAsync(db, request, resolution.LeaveType, actor),
            "ExitPermission" => await ApplyPermissionAsync(db, request),
            "ShiftRequest" or "ShiftChange" => await ApplyShiftAsync(db, request),
            "WorkFromHome" or "BusinessTrip" => await CanonicalizeRequestTypeAsync(db, request.Id, resolution.Key),
            "Overtime" => await ApplyOvertimeAsync(db, scope, request, actor),
            _ => false
        };

        if (resolution.AffectsAttendance)
        {
            await AttendanceReanalysisPolicy.AfterApprovalRangeAsync(
                db, request.EmployeeId, request.FromDate, request.ToDate);
        }

        return new Outcome(applied, applied ? "تم تفعيل أثر الطلب المعتمد." : string.Empty);
    }

    private sealed record Resolution(string Key, LeaveType LeaveType, bool AffectsAttendance);

    private static async Task<Resolution> ResolveAsync(ApplicationDbContext db, string requestType)
    {
        await RequestTypeStore.EnsureAsync(db);
        var dynamicType = (await RequestTypeStore.ListTypesAsync(db, onlyActive: false))
            .FirstOrDefault(type => string.Equals(type.Name, requestType, StringComparison.OrdinalIgnoreCase));

        if (dynamicType is not null)
        {
            var effect = BulkRequestStore.ResolveEffect(dynamicType);
            if (effect.Kind == BulkRequestStore.EffectKind.Leave)
                return new Resolution("LeaveRequest", effect.Leave ?? LeaveType.Emergency, true);
            if (effect.Kind == BulkRequestStore.EffectKind.ExitPermission)
                return new Resolution("ExitPermission", LeaveType.Emergency, true);
            if (effect.Kind == BulkRequestStore.EffectKind.OutOfOffice)
                return new Resolution(effect.RequestTypeCode, LeaveType.Emergency, true);
        }

        var key = ApprovalWorkflowEngine.ResolveRequestTypeKey(requestType);
        var leaveType = InferLeaveType(requestType);
        var attendance = key is "LeaveRequest" or "ExitPermission" or "ShiftRequest" or "ShiftChange"
            or "WorkFromHome" or "BusinessTrip";
        return new Resolution(key, leaveType, attendance);
    }

    private static LeaveType InferLeaveType(string text)
    {
        if (text.Contains("سنوي", StringComparison.OrdinalIgnoreCase)) return LeaveType.Annual;
        if (text.Contains("مرض", StringComparison.OrdinalIgnoreCase)) return LeaveType.Sick;
        if (text.Contains("غير مدفوع", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("unpaid", StringComparison.OrdinalIgnoreCase)) return LeaveType.Unpaid;
        if (text.Contains("رسمي", StringComparison.OrdinalIgnoreCase)) return LeaveType.Official;
        return LeaveType.Emergency;
    }

    private static async Task<RequestRow?> LoadApprovedAsync(ApplicationDbContext db, int requestId)
    {
        var rows = await HrmsDatabase.QueryAsync(db, """
SELECT TOP 1 Id, EmployeeId, ISNULL(RequestType,N'') AS RequestType,
       COALESCE(FromDate, RequestDate, CAST(CreatedAt AS date)) AS FromDate,
       COALESCE(ToDate, FromDate, RequestDate, CAST(CreatedAt AS date)) AS ToDate,
       StartTime, EndTime, ShiftTypeId, ISNULL(Reason,N'') AS Reason
FROM SelfServiceRequests
WHERE Id=@Id AND Status=N'Approved' AND EmployeeId IS NOT NULL;
""", command => HrmsDatabase.AddParameter(command, "@Id", requestId), reader => new RequestRow(
            HrmsDatabase.GetInt(reader, "Id"), HrmsDatabase.GetInt(reader, "EmployeeId"),
            HrmsDatabase.GetString(reader, "RequestType"),
            HrmsDatabase.GetDateOnly(reader, "FromDate") ?? default,
            HrmsDatabase.GetDateOnly(reader, "ToDate") ?? default,
            HrmsDatabase.GetTimeSpan(reader, "StartTime"), HrmsDatabase.GetTimeSpan(reader, "EndTime"),
            HrmsDatabase.GetNullableInt(reader, "ShiftTypeId"), HrmsDatabase.GetString(reader, "Reason")));
        return rows.FirstOrDefault();
    }

    private static async Task<bool> ApplyLeaveAsync(
        ApplicationDbContext db, RequestRow request, LeaveType leaveType, string actor)
    {
        var exists = await HrmsDatabase.ScalarAsync<int>(db, """
SELECT COUNT(1) FROM LeaveRequests
WHERE EmployeeId=@Employee AND LeaveType=@LeaveType
  AND FromDate=@FromDate AND ToDate=@ToDate
  AND Status IN (1,2) AND ISNULL(IsDeleted,0)=0;
""", command =>
        {
            HrmsDatabase.AddParameter(command, "@Employee", request.EmployeeId);
            HrmsDatabase.AddParameter(command, "@LeaveType", (int)leaveType);
            HrmsDatabase.AddParameter(command, "@FromDate", request.FromDate);
            HrmsDatabase.AddParameter(command, "@ToDate", request.ToDate);
        });
        if (exists > 0) return false;

        await HrmsDatabase.ExecuteAsync(db, """
INSERT INTO LeaveRequests
(EmployeeId,LeaveType,Status,FromDate,ToDate,Reason,CreatedAt,IsDeleted,CreatedBy)
VALUES(@Employee,@LeaveType,2,@FromDate,@ToDate,@Reason,SYSUTCDATETIME(),0,@Actor);
""", command =>
        {
            HrmsDatabase.AddParameter(command, "@Employee", request.EmployeeId);
            HrmsDatabase.AddParameter(command, "@LeaveType", (int)leaveType);
            HrmsDatabase.AddParameter(command, "@FromDate", request.FromDate);
            HrmsDatabase.AddParameter(command, "@ToDate", request.ToDate);
            HrmsDatabase.AddParameter(command, "@Reason", request.Reason);
            HrmsDatabase.AddParameter(command, "@Actor", actor);
        });
        return true;
    }

    private static async Task<bool> ApplyPermissionAsync(ApplicationDbContext db, RequestRow request)
    {
        await CanonicalizeRequestTypeAsync(db, request.Id, "ExitPermission");
        return true;
    }

    private static async Task<bool> CanonicalizeRequestTypeAsync(
        ApplicationDbContext db, int requestId, string requestType)
    {
        await HrmsDatabase.ExecuteAsync(db, """
UPDATE SelfServiceRequests
SET RequestType=@Type, UpdatedAt=SYSUTCDATETIME()
WHERE Id=@Id AND Status=N'Approved' AND RequestType<>@Type;
""", command =>
        {
            HrmsDatabase.AddParameter(command, "@Id", requestId);
            HrmsDatabase.AddParameter(command, "@Type", requestType);
        });
        return true;
    }

    private static async Task<bool> ApplyShiftAsync(ApplicationDbContext db, RequestRow request)
    {
        if (request.ShiftTypeId is not > 0) return false;
        await ShiftOverrideStore.EnsureAsync(db);
        await HrmsDatabase.ExecuteAsync(db, """
IF NOT EXISTS (
    SELECT 1 FROM ShiftOverrides
    WHERE EmployeeId=@Employee AND ShiftTypeId=@Shift
      AND FromDate=@FromDate AND ToDate=@ToDate AND Source=N'طلب معتمد')
INSERT INTO ShiftOverrides(EmployeeId,ShiftTypeId,FromDate,ToDate,Source)
VALUES(@Employee,@Shift,@FromDate,@ToDate,N'طلب معتمد');
""", command =>
        {
            HrmsDatabase.AddParameter(command, "@Employee", request.EmployeeId);
            HrmsDatabase.AddParameter(command, "@Shift", request.ShiftTypeId.Value);
            HrmsDatabase.AddParameter(command, "@FromDate", request.FromDate);
            HrmsDatabase.AddParameter(command, "@ToDate", request.ToDate);
        });
        return true;
    }

    private static async Task<bool> ApplyOvertimeAsync(
        ApplicationDbContext db, CompanyScope scope, RequestRow request, string actor)
    {
        if (request.StartTime is not { } start || request.EndTime is not { } end) return false;

        var allowCrossMidnight = await AttendanceRequestPolicy.GetCrossMidnightAsync(db);
        var duration = AttendanceRequestPolicy.Duration(start, end, allowCrossMidnight);
        if (duration <= TimeSpan.Zero) return false;

        await PayrollTransactionStore.EnsureAsync(db);
        var source = $"Approval:{request.Id}";
        var exists = await HrmsDatabase.ScalarAsync<int>(db, """
SELECT COUNT(1) FROM PayrollTransactions
WHERE EmployeeId=@Employee AND TxType=N'Overtime' AND Source=@Source;
""", command =>
        {
            HrmsDatabase.AddParameter(command, "@Employee", request.EmployeeId);
            HrmsDatabase.AddParameter(command, "@Source", source);
        });
        if (exists > 0) return false;

        var tx = new PayrollTransactionStore.Transaction
        {
            EmployeeId = request.EmployeeId,
            Year = request.FromDate.Year,
            Month = request.FromDate.Month,
            ItemName = "عمل إضافي معتمد",
            TxType = PayrollTransactionStore.Overtime,
            Hours = Math.Round((decimal)duration.TotalHours, 2),
            Amount = 0,
            Taxable = true,
            PaymentType = "InSalary",
            TransactionDate = request.FromDate,
            EffectiveDate = request.FromDate,
            Source = source,
            Status = "Approved",
            Note = string.IsNullOrWhiteSpace(request.Reason)
                ? $"طلب عمل إضافي معتمد #{request.Id}"
                : request.Reason
        };
        return await PayrollTransactionStore.SaveAsync(db, scope, tx, actor) > 0;
    }
}
