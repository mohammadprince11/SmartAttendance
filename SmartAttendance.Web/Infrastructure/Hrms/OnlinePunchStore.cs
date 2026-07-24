using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// البصمات عبر الإنترنت (نمط كيان — قسم 36.ج بدراسة الحضور): بصم ذاتي مباشر من
/// المتصفح/الجوال. تُخزَّن كسجلات AttendanceRecords بمصدر «موبايل» (Source=2) فتدخل
/// اشتقاق اليومية كأي بصمة عند «تحديث الحضور». دخول ⟶ CheckIn (CheckOut فارغ)،
/// خروج ⟶ CheckOut (مع CheckIn=نفس الوقت لأن العمود غير قابل للتفريغ)، فنوع البصمة
/// يُستنتج من امتلاء CheckOut. صفحة الإدارة تعرضها وتحذف المختار منها.
/// </summary>
public static class OnlinePunchStore
{
    public const int MobileSource = 2;

    public sealed class OnlinePunch
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public string EmployeeNo { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string Branch { get; set; } = string.Empty;
        public DateTime PunchAt { get; set; }
        public string PunchType { get; set; } = "In";           // In | Out (مستنتج)
        public int? PunchSemanticId { get; set; }
        public string SemanticName { get; set; } = string.Empty;

        public string PunchTypeText => PunchType == "Out" ? "ختم خروج" : "ختم دخول";
    }

    /// <summary>نافذة منع التكرار (ثوانٍ): بصمتان أونلاين خلالها = ضغطة مكرّرة تُرفَض.</summary>
    public const int DebounceSeconds = 60;

    /// <summary>
    /// تسجيل بصمة عبر الإنترنت (بصم ذاتي). يرجع معرّف السجل، أو 0 إن رُفضت لأنها
    /// مكرّرة خلال <see cref="DebounceSeconds"/> ثانية من بصمة سابقة (قاعدة صارمة تمنع
    /// إغراق يوم واحد ببصمات متطابقة متلاحقة).
    /// </summary>
    public static async Task<int> RecordAsync(
        ApplicationDbContext db, int employeeId, string punchType, DateTime punchAt, int? semanticId)
    {
        await HrmsDatabase.EnsureCreatedAsync(db);

        // قاعدة صارمة: ارفض بصمة أونلاين مكرّرة خلال نافذة قصيرة (نقرة مزدوجة/شبكة بطيئة).
        var recent = await HrmsDatabase.ScalarAsync<int>(
            db,
            $"""
SELECT COUNT(1) FROM AttendanceRecords
WHERE EmployeeId = @Emp AND Source = @Src AND ISNULL(IsDeleted, 0) = 0
      AND ABS(DATEDIFF(second, CheckIn, @At)) < {DebounceSeconds};
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Emp", employeeId);
                HrmsDatabase.AddParameter(command, "@Src", MobileSource);
                HrmsDatabase.AddParameter(command, "@At", punchAt);
            });
        if (recent > 0) return 0;

        var attendanceSemanticId = await PunchSemanticStore.AttendanceSemanticIdAsync(db);
        // دلالة الحضور تُخزَّن NULL (يقرأها المحلل)؛ غيرها كبصمة أخرى بمعرّف الدلالة.
        int? storedSemantic = (semanticId == null || semanticId == attendanceSemanticId) ? null : semanticId;
        var isOut = punchType == "Out";

        return await HrmsDatabase.ScalarAsync<int>(
            db,
            """
INSERT INTO AttendanceRecords
    (EmployeeId, AttendanceDate, CheckIn, CheckOut, Source, Status, DeviceId, Notes, CreatedAt, IsDeleted, PunchSemanticId)
VALUES
    (@Emp, @Date, @CheckIn, @CheckOut, @Src, 1, NULL, N'بصمة عبر الإنترنت', SYSUTCDATETIME(), 0, @Semantic);
SELECT CAST(SCOPE_IDENTITY() AS int);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Emp", employeeId);
                HrmsDatabase.AddParameter(command, "@Date", DateOnly.FromDateTime(punchAt));
                HrmsDatabase.AddParameter(command, "@CheckIn", punchAt);
                HrmsDatabase.AddParameter(command, "@CheckOut", isOut ? punchAt : (object)DBNull.Value);
                HrmsDatabase.AddParameter(command, "@Src", MobileSource);
                HrmsDatabase.AddParameter(command, "@Semantic", (object?)storedSemantic ?? DBNull.Value);
            });
    }

    public sealed class Filter
    {
        public int? EmployeeId { get; set; }
        public string? Search { get; set; }
        public string? PunchType { get; set; }
        public string? Department { get; set; }
        public string? Branch { get; set; }
        public DateOnly? From { get; set; }
        public DateOnly? To { get; set; }
        public int Top { get; set; } = 500;
    }

    public static async Task<List<OnlinePunch>> ListAsync(ApplicationDbContext db, Filter filter)
    {
        await HrmsDatabase.EnsureCreatedAsync(db);

        var rows = await HrmsDatabase.QueryAsync(
            db,
            $"""
SELECT TOP {Math.Clamp(filter.Top, 1, 2000)}
    ar.Id, ar.EmployeeId, ISNULL(e.EmployeeNo, N'') AS EmployeeNo, ISNULL(e.FullName, N'') AS FullName,
    ISNULL(d.Name, N'') AS DepartmentName, ISNULL(b.Name, N'') AS BranchName,
    ar.CheckIn, ar.CheckOut, ar.PunchSemanticId, ISNULL(ps.Name, N'حضور') AS SemanticName
FROM AttendanceRecords ar
INNER JOIN Employees e ON e.Id = ar.EmployeeId
LEFT JOIN Departments d ON d.Id = e.DepartmentId
LEFT JOIN Branches b ON b.Id = e.BranchId
LEFT JOIN PunchSemantics ps ON ps.Id = ar.PunchSemanticId
WHERE ISNULL(ar.IsDeleted, 0) = 0 AND ar.Source = @Src
ORDER BY ar.CheckIn DESC;
""",
            command => HrmsDatabase.AddParameter(command, "@Src", MobileSource),
            reader =>
            {
                var checkOut = HrmsDatabase.GetDateTime(reader, "CheckOut");
                return new OnlinePunch
                {
                    Id = HrmsDatabase.GetInt(reader, "Id"),
                    EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                    EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                    EmployeeName = HrmsDatabase.GetString(reader, "FullName"),
                    Department = HrmsDatabase.GetString(reader, "DepartmentName"),
                    Branch = HrmsDatabase.GetString(reader, "BranchName"),
                    PunchAt = checkOut ?? HrmsDatabase.GetDateTime(reader, "CheckIn") ?? default,
                    PunchType = checkOut.HasValue ? "Out" : "In",
                    PunchSemanticId = HrmsDatabase.GetNullableInt(reader, "PunchSemanticId"),
                    SemanticName = HrmsDatabase.GetString(reader, "SemanticName")
                };
            });

        if (filter.EmployeeId is > 0) rows = rows.Where(r => r.EmployeeId == filter.EmployeeId).ToList();
        if (!string.IsNullOrWhiteSpace(filter.PunchType)) rows = rows.Where(r => r.PunchType == filter.PunchType).ToList();
        if (!string.IsNullOrWhiteSpace(filter.Department)) rows = rows.Where(r => r.Department == filter.Department).ToList();
        if (!string.IsNullOrWhiteSpace(filter.Branch)) rows = rows.Where(r => r.Branch == filter.Branch).ToList();
        if (filter.From is { } f) rows = rows.Where(r => DateOnly.FromDateTime(r.PunchAt) >= f).ToList();
        if (filter.To is { } t) rows = rows.Where(r => DateOnly.FromDateTime(r.PunchAt) <= t).ToList();
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var v = filter.Search.Trim();
            rows = rows.Where(r =>
                r.EmployeeNo.Contains(v, StringComparison.OrdinalIgnoreCase) ||
                r.EmployeeName.Contains(v, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        return rows;
    }

    /// <summary>حذف بصمات عبر الإنترنت مختارة (نمط كيان «حذف العناصر المختارة»).</summary>
    public static async Task<int> DeleteManyAsync(ApplicationDbContext db, IReadOnlyCollection<int> ids)
    {
        await HrmsDatabase.EnsureCreatedAsync(db);
        if (ids.Count == 0) return 0;
        var total = 0;
        foreach (var chunk in ids.Chunk(200))
        {
            var inList = string.Join(",", chunk.Select((_, i) => $"@P{i}"));
            total += await HrmsDatabase.ScalarAsync<int>(
                db,
                $"UPDATE AttendanceRecords SET IsDeleted=1, UpdatedAt=SYSUTCDATETIME() WHERE Source=@Src AND Id IN ({inList}); SELECT @@ROWCOUNT;",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Src", MobileSource);
                    for (var i = 0; i < chunk.Length; i++) HrmsDatabase.AddParameter(command, $"@P{i}", chunk[i]);
                });
        }
        return total;
    }
}
