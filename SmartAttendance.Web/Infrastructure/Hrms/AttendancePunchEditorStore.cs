using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// المسار المركزي لتحرير أزواج بصمات يوم واحد. كل شاشة تعدّل الحضور اليدوي
/// تمر من هنا حتى تبقى AttendanceRecords وDayAttendances والتدقيق متزامنة.
/// </summary>
public static class AttendancePunchEditorStore
{
    public sealed class PairInput
    {
        public int Id { get; set; }
        public string? CheckIn { get; set; }
        public string? CheckOut { get; set; }
        public bool IsEmpty => string.IsNullOrWhiteSpace(CheckIn) && string.IsNullOrWhiteSpace(CheckOut);
    }

    public sealed record PairRow(int Id, string CheckIn, string CheckOut);

    public sealed record SaveResult(bool Ok, string Message, int PairCount, bool DayRecalculated);

    public static async Task<List<PairRow>> LoadAsync(
        ApplicationDbContext db, CompanyScope scope, int employeeId, DateOnly date)
    {
        if (!await EmployeeCompanyGuard.CanAccessEmployeeAsync(db, employeeId, scope))
            return new();

        var attendanceSemanticId = await PunchSemanticStore.AttendanceSemanticIdAsync(db);
        return await HrmsDatabase.QueryAsync(
            db,
            """
SELECT Id,
       CONVERT(varchar(5), CheckIn, 108) AS CheckInText,
       CONVERT(varchar(5), CheckOut, 108) AS CheckOutText
FROM AttendanceRecords
WHERE EmployeeId = @EmployeeId
  AND AttendanceDate = @Date
  AND ISNULL(IsDeleted, 0) = 0
  AND ISNULL(PunchSemanticId, @AttendanceSemanticId) = @AttendanceSemanticId
ORDER BY CheckIn, Id;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                HrmsDatabase.AddParameter(command, "@Date", date);
                HrmsDatabase.AddParameter(command, "@AttendanceSemanticId", attendanceSemanticId);
            },
            reader => new PairRow(
                HrmsDatabase.GetInt(reader, "Id"),
                HrmsDatabase.GetString(reader, "CheckInText"),
                HrmsDatabase.GetString(reader, "CheckOutText")));
    }

    public static async Task<SaveResult> SaveAsync(
        ApplicationDbContext db,
        CompanyScope scope,
        int employeeId,
        DateOnly date,
        IReadOnlyCollection<PairInput> pairs,
        string? notes,
        string actor,
        string? ipAddress)
    {
        if (!await EmployeeCompanyGuard.CanAccessEmployeeAsync(db, employeeId, scope))
            return new(false, "الموظف غير موجود أو خارج نطاقك.", 0, false);

        var effective = pairs.Where(pair => !pair.IsEmpty || pair.Id > 0).ToList();
        if (effective.Count == 0 || effective.All(pair => pair.IsEmpty))
            return new(false, "أبقِ زوج بصمة واحداً على الأقل لهذا اليوم.", 0, false);

        var attendanceSemanticId = await PunchSemanticStore.AttendanceSemanticIdAsync(db);
        var existingIds = (await LoadAsync(db, scope, employeeId, date))
            .Select(pair => pair.Id)
            .ToHashSet();

        foreach (var pair in effective.Where(pair => pair.Id > 0))
        {
            if (!existingIds.Contains(pair.Id))
                return new(false, "أحد سجلات البصمة لا يخص الموظف أو اليوم المحدد.", 0, false);
        }

        var noteText = string.IsNullOrWhiteSpace(notes)
            ? "تعديل يدوي لأزواج البصمات من ملف الموظف"
            : notes.Trim();

        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            var keptIds = new List<int>();
            foreach (var pair in effective)
            {
                if (pair.IsEmpty)
                {
                    await HrmsDatabase.ExecuteAsync(
                        db,
                        "DELETE FROM AttendanceRecords WHERE Id=@Id AND EmployeeId=@EmployeeId AND AttendanceDate=@Date;",
                        command =>
                        {
                            HrmsDatabase.AddParameter(command, "@Id", pair.Id);
                            HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                            HrmsDatabase.AddParameter(command, "@Date", date);
                        });
                    continue;
                }

                if (!TryBuild(date, pair.CheckIn, out var checkIn))
                    return await RollbackResultAsync(tx, "وقت الدخول غير صالح.");

                object checkOut = DBNull.Value;
                if (!string.IsNullOrWhiteSpace(pair.CheckOut))
                {
                    if (!TryBuild(date, pair.CheckOut, out var parsedOut))
                        return await RollbackResultAsync(tx, "وقت الخروج غير صالح.");
                    if (parsedOut < checkIn)
                        parsedOut = parsedOut.AddDays(1);
                    checkOut = parsedOut;
                }

                if (pair.Id > 0)
                {
                    keptIds.Add(pair.Id);
                    await HrmsDatabase.ExecuteAsync(
                        db,
                        """
UPDATE AttendanceRecords
SET CheckIn=@CheckIn, CheckOut=@CheckOut, Source=3, Notes=@Notes
WHERE Id=@Id AND EmployeeId=@EmployeeId AND AttendanceDate=@Date;
""",
                        command =>
                        {
                            HrmsDatabase.AddParameter(command, "@Id", pair.Id);
                            HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                            HrmsDatabase.AddParameter(command, "@Date", date);
                            HrmsDatabase.AddParameter(command, "@CheckIn", checkIn);
                            HrmsDatabase.AddParameter(command, "@CheckOut", checkOut);
                            HrmsDatabase.AddParameter(command, "@Notes", noteText);
                        });
                }
                else
                {
                    var newId = await HrmsDatabase.ScalarAsync<int>(
                        db,
                        """
INSERT INTO AttendanceRecords
(EmployeeId,AttendanceDate,CheckIn,CheckOut,Source,Status,DeviceId,Notes,CreatedAt,IsDeleted,PunchSemanticId)
VALUES(@EmployeeId,@Date,@CheckIn,@CheckOut,3,1,NULL,@Notes,SYSUTCDATETIME(),0,NULL);
SELECT CAST(SCOPE_IDENTITY() AS int);
""",
                        command =>
                        {
                            HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                            HrmsDatabase.AddParameter(command, "@Date", date);
                            HrmsDatabase.AddParameter(command, "@CheckIn", checkIn);
                            HrmsDatabase.AddParameter(command, "@CheckOut", checkOut);
                            HrmsDatabase.AddParameter(command, "@Notes", noteText);
                        });
                    keptIds.Add(newId);
                }
            }

            if (keptIds.Count == 0)
                return await RollbackResultAsync(tx, "أبقِ زوج بصمة واحداً على الأقل لهذا اليوم.");

            var keepList = string.Join(",", keptIds);
            await HrmsDatabase.ExecuteAsync(
                db,
                $"""
DELETE FROM AttendanceRecords
WHERE EmployeeId=@EmployeeId AND AttendanceDate=@Date
  AND ISNULL(PunchSemanticId,@AttendanceSemanticId)=@AttendanceSemanticId
  AND Id NOT IN ({keepList});
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                    HrmsDatabase.AddParameter(command, "@Date", date);
                    HrmsDatabase.AddParameter(command, "@AttendanceSemanticId", attendanceSemanticId);
                });

            var derived = await HrmsDatabase.QueryAsync(
                db,
                """
SELECT MIN(CheckIn) FirstIn, MAX(CheckOut) LastOut
FROM AttendanceRecords
WHERE EmployeeId=@EmployeeId AND AttendanceDate=@Date
  AND ISNULL(IsDeleted,0)=0
  AND ISNULL(PunchSemanticId,@AttendanceSemanticId)=@AttendanceSemanticId;
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                    HrmsDatabase.AddParameter(command, "@Date", date);
                    HrmsDatabase.AddParameter(command, "@AttendanceSemanticId", attendanceSemanticId);
                },
                reader => new
                {
                    FirstIn = HrmsDatabase.GetDateTime(reader, "FirstIn"),
                    LastOut = HrmsDatabase.GetDateTime(reader, "LastOut")
                });

            var day = derived.FirstOrDefault();
            var recalculated = day != null && await DayAttendanceStore.UpdateDayAsync(
                db, scope, employeeId, date, day.FirstIn, day.LastOut);

            await HrmsDatabase.ExecuteAsync(
                db,
                """
IF OBJECT_ID('AuditLogs','U') IS NOT NULL
INSERT INTO AuditLogs(EntityName,EntityId,Action,NewValues,UserName,IpAddress)
VALUES('AttendanceRecord',CAST(@EmployeeId AS nvarchar(80)),'Attendance Punch Pairs Edit',@Values,@User,@Ip);
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                    HrmsDatabase.AddParameter(command, "@Values", HrmsDatabase.JsonLine(
                        ("Date", date.ToString("yyyy-MM-dd")),
                        ("Pairs", keptIds.Count.ToString()),
                        ("FirstIn", day?.FirstIn?.ToString("HH:mm") ?? "-"),
                        ("LastOut", day?.LastOut?.ToString("HH:mm") ?? "-"),
                        ("Notes", noteText)));
                    HrmsDatabase.AddParameter(command, "@User", actor);
                    HrmsDatabase.AddParameter(command, "@Ip", ipAddress);
                });

            await tx.CommitAsync();
            var message = recalculated
                ? $"حُفظت {keptIds.Count} من أزواج البصمات وأُعيد احتساب اليوم مباشرةً."
                : $"حُفظت {keptIds.Count} من أزواج البصمات، لكن اليومية لم تكن قابلة لإعادة الاشتقاق.";
            return new(true, message, keptIds.Count, recalculated);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    private static bool TryBuild(DateOnly date, string? time, out DateTime value)
    {
        value = default;
        return TimeOnly.TryParse(time, out var parsed) &&
               (value = date.ToDateTime(parsed)) != default;
    }

    private static async Task<SaveResult> RollbackResultAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx, string message)
    {
        await tx.RollbackAsync();
        return new(false, message, 0, false);
    }
}
