using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// طلبات البصمة المفقودة (نمط كيان — قسم 36.ب بدراسة الحضور): طلب يوثّق بصمة غائبة
/// (دخول/خروج) لموظف بوقتها ودلالتها وسببها، يمرّ بحالة (قيد الانتظار ← موافَق/مرفوض/
/// ملغى). عند الموافقة يُنشأ سجل بصمة فعلي في AttendanceRecords فيدخل اشتقاق اليومية
/// عند «تحديث الحضور». التحرير متاح ما دام الطلب قيد الانتظار (والصلاحية تُنفَّذ بالصفحة).
/// نمط self-healing (CREATE + ALTER idempotent) — لا هجرات.
/// </summary>
public static class MissingPunchRequestStore
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Cancelled = "Cancelled";

    public static string StatusLabel(string s) => s switch
    {
        "Pending" => "قيد الانتظار",
        "Approved" => "موافَق عليه",
        "Rejected" => "مرفوض",
        "Cancelled" => "ملغى",
        _ => s
    };

    public static string PunchTypeLabel(string t) => t switch
    {
        "In" => "ختم دخول",
        "Out" => "ختم خروج",
        _ => t
    };

    public sealed class Request
    {
        public int Id { get; set; }
        public string RefNo { get; set; } = string.Empty;
        public int EmployeeId { get; set; }
        public string EmployeeNo { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string Branch { get; set; } = string.Empty;
        public string Position { get; set; } = string.Empty;
        public DateTime PunchAt { get; set; }
        public string PunchType { get; set; } = "In";           // In | Out
        public int? PunchSemanticId { get; set; }               // null = حضور
        public string SemanticName { get; set; } = string.Empty;
        public string? Reason { get; set; }
        public string Status { get; set; } = "Pending";
        public string? DecisionNote { get; set; }
        public int? CreatedRecordId { get; set; }
        public string Source { get; set; } = "مباشر";
        public DateTime CreatedAt { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime? DecidedAt { get; set; }
        public string? DecidedBy { get; set; }

        public string StatusText => StatusLabel(Status);
        public string PunchTypeText => PunchTypeLabel(PunchType);
        public bool IsPending => Status == "Pending";
    }

    public static async Task EnsureAsync(ApplicationDbContext db)
    {
        await HrmsDatabase.ExecuteAsync(
            db,
            """
IF OBJECT_ID('MissingPunchRequests', 'U') IS NULL
BEGIN
    CREATE TABLE MissingPunchRequests
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        RefNo nvarchar(40) NOT NULL,
        EmployeeId int NOT NULL,
        PunchAt datetime2 NOT NULL,
        PunchType nvarchar(10) NOT NULL DEFAULT(N'In'),
        PunchSemanticId int NULL,
        Reason nvarchar(500) NULL,
        Status nvarchar(20) NOT NULL DEFAULT(N'Pending'),
        DecisionNote nvarchar(500) NULL,
        CreatedRecordId int NULL,
        Source nvarchar(50) NOT NULL DEFAULT(N'مباشر'),
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME()),
        CreatedBy nvarchar(150) NULL,
        DecidedAt datetime2 NULL,
        DecidedBy nvarchar(150) NULL,
        IsDeleted bit NOT NULL DEFAULT(0)
    );
    CREATE INDEX IX_MissingPunchRequests_Status ON MissingPunchRequests (Status);
    CREATE INDEX IX_MissingPunchRequests_Employee ON MissingPunchRequests (EmployeeId);
END;
""");
    }

    public sealed class Filter
    {
        public string? Search { get; set; }
        public string? Status { get; set; }
        public string? PunchType { get; set; }
        public string? Department { get; set; }
        public string? Branch { get; set; }
        public string? Position { get; set; }
        public DateOnly? From { get; set; }
        public DateOnly? To { get; set; }
    }

    public static async Task<List<Request>> ListAsync(ApplicationDbContext db, Filter filter)
    {
        await EnsureAsync(db);
        var rows = await HrmsDatabase.QueryAsync(
            db,
            """
SELECT r.*, ISNULL(e.EmployeeNo, N'') AS EmployeeNo, ISNULL(e.FullName, N'') AS FullName,
       ISNULL(d.Name, N'') AS DepartmentName, ISNULL(b.Name, N'') AS BranchName,
       ISNULL(e.Position, N'') AS Position, ISNULL(ps.Name, N'حضور') AS SemanticName
FROM MissingPunchRequests r
INNER JOIN Employees e ON e.Id = r.EmployeeId
LEFT JOIN Departments d ON d.Id = e.DepartmentId
LEFT JOIN Branches b ON b.Id = e.BranchId
LEFT JOIN PunchSemantics ps ON ps.Id = r.PunchSemanticId
WHERE ISNULL(r.IsDeleted, 0) = 0
ORDER BY r.CreatedAt DESC;
""",
            command => { },
            Read);

        if (!string.IsNullOrWhiteSpace(filter.Status)) rows = rows.Where(r => r.Status == filter.Status).ToList();
        if (!string.IsNullOrWhiteSpace(filter.PunchType)) rows = rows.Where(r => r.PunchType == filter.PunchType).ToList();
        if (!string.IsNullOrWhiteSpace(filter.Department)) rows = rows.Where(r => r.Department == filter.Department).ToList();
        if (!string.IsNullOrWhiteSpace(filter.Branch)) rows = rows.Where(r => r.Branch == filter.Branch).ToList();
        if (!string.IsNullOrWhiteSpace(filter.Position)) rows = rows.Where(r => r.Position == filter.Position).ToList();
        if (filter.From is { } f) rows = rows.Where(r => DateOnly.FromDateTime(r.PunchAt) >= f).ToList();
        if (filter.To is { } t) rows = rows.Where(r => DateOnly.FromDateTime(r.PunchAt) <= t).ToList();
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var v = filter.Search.Trim();
            rows = rows.Where(r =>
                r.EmployeeNo.Contains(v, StringComparison.OrdinalIgnoreCase) ||
                r.EmployeeName.Contains(v, StringComparison.OrdinalIgnoreCase) ||
                r.RefNo.Contains(v, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        return rows;
    }

    public static async Task<Request?> GetAsync(ApplicationDbContext db, int id)
    {
        await EnsureAsync(db);
        return (await HrmsDatabase.QueryAsync(
            db,
            """
SELECT r.*, ISNULL(e.EmployeeNo, N'') AS EmployeeNo, ISNULL(e.FullName, N'') AS FullName,
       ISNULL(d.Name, N'') AS DepartmentName, ISNULL(b.Name, N'') AS BranchName,
       ISNULL(e.Position, N'') AS Position, ISNULL(ps.Name, N'حضور') AS SemanticName
FROM MissingPunchRequests r
INNER JOIN Employees e ON e.Id = r.EmployeeId
LEFT JOIN Departments d ON d.Id = e.DepartmentId
LEFT JOIN Branches b ON b.Id = e.BranchId
LEFT JOIN PunchSemantics ps ON ps.Id = r.PunchSemanticId
WHERE r.Id = @Id AND ISNULL(r.IsDeleted, 0) = 0;
""",
            command => HrmsDatabase.AddParameter(command, "@Id", id),
            Read)).FirstOrDefault();
    }

    /// <summary>حفظ طلب (إنشاء أو تحرير). التحرير مسموح فقط ما دام قيد الانتظار.</summary>
    public static async Task<(bool Ok, string Message)> SaveAsync(ApplicationDbContext db, Request r, string userName)
    {
        await EnsureAsync(db);
        if (r.EmployeeId <= 0) return (false, "اختر الموظف.");
        if (r.PunchType is not ("In" or "Out")) return (false, "نوع البصمة غير صالح.");

        if (r.Id > 0)
        {
            var existing = await GetAsync(db, r.Id);
            if (existing == null) return (false, "الطلب غير موجود.");
            if (!existing.IsPending) return (false, "لا يمكن تحرير طلب بعد البتّ فيه.");

            await HrmsDatabase.ExecuteAsync(
                db,
                """
UPDATE MissingPunchRequests
SET EmployeeId=@Emp, PunchAt=@At, PunchType=@Type, PunchSemanticId=@Semantic, Reason=@Reason
WHERE Id=@Id AND Status=N'Pending';
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Id", r.Id);
                    AddCore(command, r);
                });
            return (true, "تم تحديث الطلب.");
        }

        var refNo = await GenerateRefNoAsync(db);
        await HrmsDatabase.ExecuteAsync(
            db,
            """
INSERT INTO MissingPunchRequests (RefNo, EmployeeId, PunchAt, PunchType, PunchSemanticId, Reason, Status, Source, CreatedBy)
VALUES (@Ref, @Emp, @At, @Type, @Semantic, @Reason, N'Pending', @Source, @By);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Ref", refNo);
                AddCore(command, r);
                HrmsDatabase.AddParameter(command, "@Source", string.IsNullOrWhiteSpace(r.Source) ? "مباشر" : r.Source);
                HrmsDatabase.AddParameter(command, "@By", userName);
            });
        return (true, $"أُنشئ الطلب {refNo}.");
    }

    /// <summary>
    /// الموافقة: تُنشئ سجل بصمة فعلي (Source=يدوي، Status=حاضر). دلالة «حضور» ⟶
    /// PunchSemanticId=NULL فتدخل اشتقاق اليومية؛ الدلالات الأخرى تُحفظ كبصمة أخرى.
    /// «دخول» ⟶ CheckIn، «خروج» ⟶ CheckOut (مع CheckIn=نفس الوقت لأن العمود غير قابل للتفريغ).
    /// </summary>
    public static async Task<(bool Ok, string Message)> ApproveAsync(
        ApplicationDbContext db, int id, string? note, string userName)
    {
        var r = await GetAsync(db, id);
        if (r == null) return (false, "الطلب غير موجود.");
        if (!r.IsPending) return (false, "الطلب ليس قيد الانتظار.");

        var attendanceSemanticId = await PunchSemanticStore.AttendanceSemanticIdAsync(db);
        // دلالة الحضور تُخزَّن NULL (يقرأها المحلل)؛ غيرها كبصمة أخرى بمعرّف الدلالة.
        int? storedSemantic = (r.PunchSemanticId == null || r.PunchSemanticId == attendanceSemanticId)
            ? null : r.PunchSemanticId;

        var date = DateOnly.FromDateTime(r.PunchAt);
        var recordId = await HrmsDatabase.ScalarAsync<int>(
            db,
            """
INSERT INTO AttendanceRecords
    (EmployeeId, AttendanceDate, CheckIn, CheckOut, Source, Status, DeviceId, Notes, CreatedAt, IsDeleted, PunchSemanticId)
VALUES
    (@Emp, @Date, @CheckIn, @CheckOut, 3, 1, NULL, @Notes, SYSUTCDATETIME(), 0, @Semantic);
SELECT CAST(SCOPE_IDENTITY() AS int);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Emp", r.EmployeeId);
                HrmsDatabase.AddParameter(command, "@Date", date);
                HrmsDatabase.AddParameter(command, "@CheckIn", r.PunchAt);
                HrmsDatabase.AddParameter(command, "@CheckOut", r.PunchType == "Out" ? r.PunchAt : (object)DBNull.Value);
                HrmsDatabase.AddParameter(command, "@Notes", $"طلب بصمة مفقودة {r.RefNo}");
                HrmsDatabase.AddParameter(command, "@Semantic", (object?)storedSemantic ?? DBNull.Value);
            });

        await HrmsDatabase.ExecuteAsync(
            db,
            """
UPDATE MissingPunchRequests
SET Status=N'Approved', DecisionNote=@Note, CreatedRecordId=@Rec, DecidedAt=SYSUTCDATETIME(), DecidedBy=@By
WHERE Id=@Id AND Status=N'Pending';
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", id);
                HrmsDatabase.AddParameter(command, "@Note", (object?)note ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@Rec", recordId);
                HrmsDatabase.AddParameter(command, "@By", userName);
            });
        return (true, $"وُوفق على {r.RefNo} وأُنشئت البصمة — شغّل «تحديث الحضور» لتظهر باليومية.");
    }

    public static async Task<(bool Ok, string Message)> RejectAsync(
        ApplicationDbContext db, int id, string? note, string userName)
    {
        var r = await GetAsync(db, id);
        if (r == null) return (false, "الطلب غير موجود.");
        if (!r.IsPending) return (false, "الطلب ليس قيد الانتظار.");

        await HrmsDatabase.ExecuteAsync(
            db,
            "UPDATE MissingPunchRequests SET Status=N'Rejected', DecisionNote=@Note, DecidedAt=SYSUTCDATETIME(), DecidedBy=@By WHERE Id=@Id AND Status=N'Pending';",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", id);
                HrmsDatabase.AddParameter(command, "@Note", (object?)note ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@By", userName);
            });
        return (true, $"رُفض الطلب {r.RefNo}.");
    }

    /// <summary>إلغاء طلب موافَق عليه: يحذف البصمة المُنشأة ويعيد الحالة إلى «ملغى».</summary>
    public static async Task<(bool Ok, string Message)> CancelAsync(
        ApplicationDbContext db, int id, string userName)
    {
        var r = await GetAsync(db, id);
        if (r == null) return (false, "الطلب غير موجود.");
        if (r.Status != Approved) return (false, "الإلغاء متاح فقط لطلب موافَق عليه.");

        if (r.CreatedRecordId is int recId)
        {
            await HrmsDatabase.ExecuteAsync(
                db,
                "UPDATE AttendanceRecords SET IsDeleted=1, UpdatedAt=SYSUTCDATETIME() WHERE Id=@Rec;",
                command => HrmsDatabase.AddParameter(command, "@Rec", recId));
        }

        await HrmsDatabase.ExecuteAsync(
            db,
            "UPDATE MissingPunchRequests SET Status=N'Cancelled', CreatedRecordId=NULL, DecidedAt=SYSUTCDATETIME(), DecidedBy=@By WHERE Id=@Id AND Status=N'Approved';",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", id);
                HrmsDatabase.AddParameter(command, "@By", userName);
            });
        return (true, $"أُلغيت الموافقة على {r.RefNo} وحُذفت البصمة — شغّل «تحديث الحضور».");
    }

    public static async Task DeleteAsync(ApplicationDbContext db, int id)
    {
        await EnsureAsync(db);
        await HrmsDatabase.ExecuteAsync(
            db,
            "UPDATE MissingPunchRequests SET IsDeleted=1 WHERE Id=@Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", id));
    }

    private static async Task<string> GenerateRefNoAsync(ApplicationDbContext db)
    {
        var prefix = $"MP{DateTime.Today:yy}-";
        var count = await HrmsDatabase.ScalarAsync<int>(
            db,
            "SELECT COUNT(1) FROM MissingPunchRequests WHERE RefNo LIKE @P;",
            command => HrmsDatabase.AddParameter(command, "@P", prefix + "%"));
        return $"{prefix}{count + 1:0000}";
    }

    private static void AddCore(System.Data.Common.DbCommand command, Request r)
    {
        HrmsDatabase.AddParameter(command, "@Emp", r.EmployeeId);
        HrmsDatabase.AddParameter(command, "@At", r.PunchAt);
        HrmsDatabase.AddParameter(command, "@Type", r.PunchType);
        HrmsDatabase.AddParameter(command, "@Semantic", (object?)r.PunchSemanticId ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@Reason", (object?)r.Reason ?? DBNull.Value);
    }

    private static Request Read(System.Data.Common.DbDataReader reader) => new()
    {
        Id = HrmsDatabase.GetInt(reader, "Id"),
        RefNo = HrmsDatabase.GetString(reader, "RefNo"),
        EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
        EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
        EmployeeName = HrmsDatabase.GetString(reader, "FullName"),
        Department = HrmsDatabase.GetString(reader, "DepartmentName"),
        Branch = HrmsDatabase.GetString(reader, "BranchName"),
        Position = HrmsDatabase.GetString(reader, "Position"),
        PunchAt = HrmsDatabase.GetDateTime(reader, "PunchAt") ?? default,
        PunchType = HrmsDatabase.GetString(reader, "PunchType") is { Length: > 0 } pt ? pt : "In",
        PunchSemanticId = HrmsDatabase.GetNullableInt(reader, "PunchSemanticId"),
        SemanticName = HrmsDatabase.GetString(reader, "SemanticName"),
        Reason = HrmsDatabase.GetString(reader, "Reason") is { Length: > 0 } rs ? rs : null,
        Status = HrmsDatabase.GetString(reader, "Status") is { Length: > 0 } st ? st : "Pending",
        DecisionNote = HrmsDatabase.GetString(reader, "DecisionNote") is { Length: > 0 } dn ? dn : null,
        CreatedRecordId = HrmsDatabase.GetNullableInt(reader, "CreatedRecordId"),
        Source = HrmsDatabase.GetString(reader, "Source") is { Length: > 0 } src ? src : "مباشر",
        CreatedAt = HrmsDatabase.GetDateTime(reader, "CreatedAt") ?? default,
        CreatedBy = HrmsDatabase.GetString(reader, "CreatedBy") is { Length: > 0 } cb ? cb : null,
        DecidedAt = HrmsDatabase.GetDateTime(reader, "DecidedAt"),
        DecidedBy = HrmsDatabase.GetString(reader, "DecidedBy") is { Length: > 0 } db2 ? db2 : null
    };
}
