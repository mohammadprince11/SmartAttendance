using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>تخزين إسنادات رئيس الوحدة المؤقت. المنطق كلّه بـ<see cref="TemporaryHeadPolicy"/>.</summary>
public static class TemporaryHeadStore
{
    public sealed record Row(
        int Id,
        int DepartmentId,
        string DepartmentName,
        int HeadEmployeeId,
        string HeadName,
        DateOnly FromDate,
        DateOnly ToDate,
        bool IsActive,
        string? Note)
    {
        public TemporaryHeadPolicy.Allocation ToAllocation() =>
            new(Id, DepartmentId, HeadEmployeeId, FromDate, ToDate, IsActive);
    }

    public static async Task<List<Row>> LoadAsync(ApplicationDbContext db) =>
        await HrmsDatabase.QueryAsync(
            db,
            """
SELECT t.Id, t.DepartmentId, ISNULL(d.Name, N'') AS DepartmentName, t.HeadEmployeeId,
       ISNULL(e.FullName, N'') AS HeadName, t.FromDate, t.ToDate, t.IsActive, t.Note
FROM TemporaryUnitHeads t
LEFT JOIN Departments d ON d.Id = t.DepartmentId
LEFT JOIN Employees e ON e.Id = t.HeadEmployeeId
ORDER BY t.FromDate DESC, t.Id DESC;
""",
            command => { },
            reader => new Row(
                HrmsDatabase.GetInt(reader, "Id"),
                HrmsDatabase.GetInt(reader, "DepartmentId"),
                HrmsDatabase.GetString(reader, "DepartmentName"),
                HrmsDatabase.GetInt(reader, "HeadEmployeeId"),
                HrmsDatabase.GetString(reader, "HeadName"),
                HrmsDatabase.GetDateOnly(reader, "FromDate") ?? default,
                HrmsDatabase.GetDateOnly(reader, "ToDate") ?? default,
                HrmsDatabase.GetBool(reader, "IsActive"),
                HrmsDatabase.GetString(reader, "Note")));

    /// <summary>الإسنادات وحدها — للاستهلاك بحلقات لا تحتاج أسماء (مولّد الإشعارات).</summary>
    public static async Task<List<TemporaryHeadPolicy.Allocation>> LoadAllocationsAsync(ApplicationDbContext db) =>
        await HrmsDatabase.QueryAsync(
            db,
            "SELECT Id, DepartmentId, HeadEmployeeId, FromDate, ToDate, IsActive FROM TemporaryUnitHeads WHERE IsActive = 1;",
            command => { },
            reader => new TemporaryHeadPolicy.Allocation(
                HrmsDatabase.GetInt(reader, "Id"),
                HrmsDatabase.GetInt(reader, "DepartmentId"),
                HrmsDatabase.GetInt(reader, "HeadEmployeeId"),
                HrmsDatabase.GetDateOnly(reader, "FromDate") ?? default,
                HrmsDatabase.GetDateOnly(reader, "ToDate") ?? default,
                HrmsDatabase.GetBool(reader, "IsActive")));

    public static async Task<int> SaveAsync(
        ApplicationDbContext db,
        int id,
        int departmentId,
        int headEmployeeId,
        DateOnly fromDate,
        DateOnly toDate,
        bool isActive,
        string? note,
        string? user)
    {
        if (id > 0)
        {
            await HrmsDatabase.ExecuteAsync(
                db,
                """
UPDATE TemporaryUnitHeads
SET DepartmentId = @DepartmentId, HeadEmployeeId = @HeadEmployeeId, FromDate = @FromDate,
    ToDate = @ToDate, IsActive = @IsActive, Note = @Note
WHERE Id = @Id;
""",
                command => Bind(command, id, departmentId, headEmployeeId, fromDate, toDate, isActive, note, user));

            return id;
        }

        return await HrmsDatabase.ScalarAsync<int>(
            db,
            """
INSERT INTO TemporaryUnitHeads (DepartmentId, HeadEmployeeId, FromDate, ToDate, IsActive, Note, CreatedBy)
OUTPUT INSERTED.Id
VALUES (@DepartmentId, @HeadEmployeeId, @FromDate, @ToDate, @IsActive, @Note, @CreatedBy);
""",
            command => Bind(command, id, departmentId, headEmployeeId, fromDate, toDate, isActive, note, user));
    }

    private static void Bind(
        System.Data.Common.DbCommand command,
        int id, int departmentId, int headEmployeeId,
        DateOnly fromDate, DateOnly toDate, bool isActive, string? note, string? user)
    {
        if (id > 0) HrmsDatabase.AddParameter(command, "@Id", id);
        HrmsDatabase.AddParameter(command, "@DepartmentId", departmentId);
        HrmsDatabase.AddParameter(command, "@HeadEmployeeId", headEmployeeId);
        HrmsDatabase.AddParameter(command, "@FromDate", fromDate.ToDateTime(TimeOnly.MinValue));
        HrmsDatabase.AddParameter(command, "@ToDate", toDate.ToDateTime(TimeOnly.MinValue));
        HrmsDatabase.AddParameter(command, "@IsActive", isActive);
        HrmsDatabase.AddParameter(command, "@Note", note);
        if (id <= 0) HrmsDatabase.AddParameter(command, "@CreatedBy", user);
    }

    public static async Task DeleteAsync(ApplicationDbContext db, int id) =>
        await HrmsDatabase.ExecuteAsync(
            db,
            "DELETE FROM TemporaryUnitHeads WHERE Id = @Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", id));
}
