using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// سجلّ العقود وحركاتها (نظير شاشتَي كيان: «عقود الموظفين» و«تحديثات العقود»).
///
/// المبدأ: **العقود لا تُدهَس، تُضاف**. تجديدٌ يُنشئ صفّاً جديداً ويُبقي السابق
/// بالسجلّ، فيبقى تاريخ الموظف التعاقدي قابلاً للإجابة عنه بعد سنوات.
/// </summary>
public static class ContractRegisterStore
{
    public sealed record ContractRow(
        int Id,
        int EmployeeId,
        string EmployeeName,
        string EmployeeNo,
        string? ContractNo,
        string ContractType,
        DateOnly FromDate,
        DateOnly? ToDate,
        bool IsCurrent,
        int? PreviousContractId,
        string? MovementKind,
        string? Note)
    {
        public int? Remaining(DateOnly asOf) => ContractPolicy.RemainingDays(ToDate, asOf);
        public string RemainingLabel(DateOnly asOf) => ContractPolicy.RemainingLabel(ToDate, asOf);
        public string RangeLabel =>
            $"{FromDate:yyyy/MM/dd} – {(ToDate is { } end ? end.ToString("yyyy/MM/dd") : "غير محدود")}";
    }

    public sealed record MovementRow(
        int Id,
        string? RefNo,
        int EmployeeId,
        string EmployeeName,
        int? ContractId,
        string MovementKind,
        string? ContractType,
        DateOnly? FromDate,
        DateOnly? ToDate,
        DateOnly? EffectiveDate,
        string? Source,
        string Status,
        string? Notes)
    {
        public bool IsLocked => string.Equals(Status, "Locked", StringComparison.OrdinalIgnoreCase);
        public string KindLabel => ContractPolicy.MovementLabel(MovementKind);
    }

    public static async Task EnsureAsync(ApplicationDbContext db) =>
        await EmployeeContractSchema.EnsureAsync(db);

    /// <summary>
    /// السجلّ كاملاً. الترتيب: الموظف ثم الأحدث أولاً — فسلسلة عقود الموظف تُقرأ
    /// نزولاً من الحالي للأقدم، وهو ترتيب السؤال الطبيعي («ما عقده الآن؟ وقبله؟»).
    /// </summary>
    public static async Task<List<ContractRow>> LoadContractsAsync(
        ApplicationDbContext db,
        Security.CompanyScope scope,
        int? employeeId = null,
        string? search = null)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.IsDeniedAll) return new List<ContractRow>();

        var filters = new List<string> { Security.EmployeeCompanyGuard.ListFilter(scope, "e.CompanyId") };
        if (employeeId is not null) filters.Add("c.EmployeeId = @EmployeeId");
        if (!string.IsNullOrWhiteSpace(search))
            filters.Add("(e.FullName LIKE @Search OR e.EmployeeNo LIKE @Search OR c.ContractNo LIKE @Search)");

        var where = filters.Count == 0 ? string.Empty : " AND " + string.Join(" AND ", filters);

        return await HrmsDatabase.QueryAsync(
            db,
            $"""
SELECT c.Id, c.EmployeeId, ISNULL(e.FullName, N'') AS EmployeeName,
       ISNULL(e.EmployeeNo, N'') AS EmployeeNo, c.ContractNo, c.ContractType,
       c.FromDate, c.ToDate, c.IsCurrent, c.PreviousContractId, c.MovementKind, c.Note
FROM EmployeeContracts c
INNER JOIN Employees e ON e.Id = c.EmployeeId
WHERE ISNULL(c.IsDeleted, 0) = 0 AND ISNULL(e.IsDeleted, 0) = 0{where}
ORDER BY e.FullName, c.FromDate DESC, c.Id DESC;
""",
            command =>
            {
                if (employeeId is not null) HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                if (!string.IsNullOrWhiteSpace(search)) HrmsDatabase.AddParameter(command, "@Search", $"%{search.Trim()}%");
            },
            reader => new ContractRow(
                HrmsDatabase.GetInt(reader, "Id"),
                HrmsDatabase.GetInt(reader, "EmployeeId"),
                HrmsDatabase.GetString(reader, "EmployeeName"),
                HrmsDatabase.GetString(reader, "EmployeeNo"),
                HrmsDatabase.GetString(reader, "ContractNo"),
                HrmsDatabase.GetString(reader, "ContractType"),
                HrmsDatabase.GetDateOnly(reader, "FromDate") ?? default,
                HrmsDatabase.GetDateOnly(reader, "ToDate"),
                HrmsDatabase.GetBool(reader, "IsCurrent"),
                HrmsDatabase.GetNullableInt(reader, "PreviousContractId"),
                HrmsDatabase.GetString(reader, "MovementKind"),
                HrmsDatabase.GetString(reader, "Note")));
    }

    public static async Task<ContractRow?> FindContractAsync(
        ApplicationDbContext db, Security.CompanyScope scope, int id) =>
        (await LoadContractsAsync(db, scope)).FirstOrDefault(row => row.Id == id);

    /// <summary>المدة الافتراضية لكل نوع عقد — مصدر اشتقاق تاريخ الانتهاء.</summary>
    public static async Task<Dictionary<string, int?>> LoadContractTypesAsync(ApplicationDbContext db)
    {
        var rows = await HrmsDatabase.QueryAsync(
            db,
            """
SELECT ArabicName, DefaultMonths
FROM HrLookups
WHERE Category = N'contracttypes' AND IsActive = 1
ORDER BY SortOrder, Id;
""",
            command => { },
            reader => (
                Name: HrmsDatabase.GetString(reader, "ArabicName"),
                Months: HrmsDatabase.GetNullableInt(reader, "DefaultMonths")));

        var map = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, months) in rows)
        {
            if (name.Length > 0) map[name] = months;
        }

        return map;
    }

    public static async Task SetContractTypeMonthsAsync(ApplicationDbContext db, string name, int? months) =>
        await HrmsDatabase.ExecuteAsync(
            db,
            "UPDATE HrLookups SET DefaultMonths = @Months WHERE Category = N'contracttypes' AND ArabicName = @Name;",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Months", months);
                HrmsDatabase.AddParameter(command, "@Name", name);
            });

    // ── كتابة العقود ───────────────────────────────────────────────────────────

    /// <summary>
    /// يضيف عقداً. <paramref name="makeCurrent"/> ينزع صفة «الحالي» عن بقية عقود
    /// الموظف بنفس المعاملة — عقدان «حاليان» لموظف واحد حالةٌ متناقضة تُفسد كل
    /// شاشة تسأل «ما عقده الآن؟».
    /// </summary>
    public static async Task<int> AddContractAsync(
        ApplicationDbContext db,
        int employeeId,
        string? contractNo,
        string contractType,
        DateOnly fromDate,
        DateOnly? toDate,
        bool makeCurrent,
        int? previousContractId,
        string? movementKind,
        DateOnly? effectiveDate,
        string? note,
        string? createdBy)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();

        if (makeCurrent)
        {
            await HrmsDatabase.ExecuteAsync(
                db,
                "UPDATE EmployeeContracts SET IsCurrent = 0 WHERE EmployeeId = @EmployeeId;",
                command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId));
        }

        var id = await HrmsDatabase.ScalarAsync<int>(
            db,
            """
INSERT INTO EmployeeContracts
    (EmployeeId, ContractNo, ContractType, FromDate, ToDate, IsCurrent,
     PreviousContractId, MovementKind, EffectiveDate, Note, CreatedBy)
OUTPUT INSERTED.Id
VALUES (@EmployeeId, @ContractNo, @ContractType, @FromDate, @ToDate, @IsCurrent,
        @PreviousContractId, @MovementKind, @EffectiveDate, @Note, @CreatedBy);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                HrmsDatabase.AddParameter(command, "@ContractNo", contractNo);
                HrmsDatabase.AddParameter(command, "@ContractType", contractType);
                HrmsDatabase.AddParameter(command, "@FromDate", fromDate.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@ToDate", toDate?.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@IsCurrent", makeCurrent);
                HrmsDatabase.AddParameter(command, "@PreviousContractId", previousContractId);
                HrmsDatabase.AddParameter(command, "@MovementKind", movementKind);
                HrmsDatabase.AddParameter(command, "@EffectiveDate", effectiveDate?.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@Note", note);
                HrmsDatabase.AddParameter(command, "@CreatedBy", createdBy);
            });

        await transaction.CommitAsync();
        return id;
    }

    public static async Task UpdateContractAsync(
        ApplicationDbContext db,
        int id,
        string? contractNo,
        string contractType,
        DateOnly fromDate,
        DateOnly? toDate,
        string? note,
        string? updatedBy) =>
        await HrmsDatabase.ExecuteAsync(
            db,
            """
UPDATE EmployeeContracts
SET ContractNo = @ContractNo, ContractType = @ContractType, FromDate = @FromDate,
    ToDate = @ToDate, Note = @Note, UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @UpdatedBy
WHERE Id = @Id;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", id);
                HrmsDatabase.AddParameter(command, "@ContractNo", contractNo);
                HrmsDatabase.AddParameter(command, "@ContractType", contractType);
                HrmsDatabase.AddParameter(command, "@FromDate", fromDate.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@ToDate", toDate?.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@Note", note);
                HrmsDatabase.AddParameter(command, "@UpdatedBy", updatedBy);
            });

    /// <summary>حذف منطقي — عقدٌ يُمحى فعلياً يمحو معه تاريخ خدمةٍ قد يُحتاج مالياً.</summary>
    public static async Task DeleteContractAsync(ApplicationDbContext db, int id) =>
        await HrmsDatabase.ExecuteAsync(
            db,
            "UPDATE EmployeeContracts SET IsDeleted = 1, UpdatedAt = SYSUTCDATETIME() WHERE Id = @Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", id));

    // ── الحركات ────────────────────────────────────────────────────────────────

    public static async Task<List<MovementRow>> LoadMovementsAsync(
        ApplicationDbContext db, Security.CompanyScope scope, bool locked)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.IsDeniedAll) return new List<MovementRow>();

        var status = locked ? "Locked" : "Open";

        return await HrmsDatabase.QueryAsync(
            db,
            $"""
SELECT m.Id, m.RefNo, m.EmployeeId, ISNULL(e.FullName, N'') AS EmployeeName, m.ContractId,
       m.MovementKind, m.ContractType, m.FromDate, m.ToDate, m.EffectiveDate,
       m.Source, m.Status, m.Notes
FROM EmployeeContractMovements m
LEFT JOIN Employees e ON e.Id = m.EmployeeId
WHERE m.Status = @Status
  AND {Security.EmployeeCompanyGuard.ListFilter(scope, "e.CompanyId")}
ORDER BY m.Id DESC;
""",
            command => HrmsDatabase.AddParameter(command, "@Status", status),
            reader => new MovementRow(
                HrmsDatabase.GetInt(reader, "Id"),
                HrmsDatabase.GetString(reader, "RefNo"),
                HrmsDatabase.GetInt(reader, "EmployeeId"),
                HrmsDatabase.GetString(reader, "EmployeeName"),
                HrmsDatabase.GetNullableInt(reader, "ContractId"),
                HrmsDatabase.GetString(reader, "MovementKind"),
                HrmsDatabase.GetString(reader, "ContractType"),
                HrmsDatabase.GetDateOnly(reader, "FromDate"),
                HrmsDatabase.GetDateOnly(reader, "ToDate"),
                HrmsDatabase.GetDateOnly(reader, "EffectiveDate"),
                HrmsDatabase.GetString(reader, "Source"),
                HrmsDatabase.GetString(reader, "Status"),
                HrmsDatabase.GetString(reader, "Notes")));
    }

    /// <summary>
    /// ينفّذ حركة عقد ويكتب أثرها بالسجلّ بمعاملة واحدة.
    ///
    /// <b>تمديد</b> يعدّل تاريخ انتهاء العقد القائم (استمرارية واحدة)، و<b>تجديد</b>
    /// يُنشئ عقداً جديداً يبدأ بعد انتهاء السابق ويربطه به بـ<c>PreviousContractId</c>.
    /// خلط الاثنين هو ما يُفسد حساب الخدمة المتصلة، فالتمييز مسجَّل لا مستنتَج.
    /// </summary>
    public static async Task<int> ApplyMovementAsync(
        ApplicationDbContext db,
        Security.CompanyScope scope,
        int employeeId,
        int? contractId,
        string movementKind,
        string contractType,
        DateOnly? toDate,
        DateOnly effectiveDate,
        string? notes,
        string? createdBy,
        string source = "يدوي")
    {
        // الموظف المستهدَف يجب أن يكون ضمن نطاق المستخدم — معرّفٌ من الطلب.
        if (!await Security.EmployeeCompanyGuard.CanAccessEmployeeAsync(db, employeeId, scope))
            throw new InvalidOperationException("الموظف خارج نطاق شركاتك.");

        var current = contractId is null ? null : await FindContractAsync(db, scope, contractId.Value);

        await using var transaction = await db.Database.BeginTransactionAsync();

        int? resultContractId = null;
        DateOnly? fromDate = null;

        if (movementKind == ContractPolicy.MovementExtend && current is not null)
        {
            fromDate = current.FromDate;
            resultContractId = current.Id;

            await HrmsDatabase.ExecuteAsync(
                db,
                """
UPDATE EmployeeContracts
SET ToDate = @ToDate, MovementKind = @MovementKind, EffectiveDate = @EffectiveDate,
    UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @UpdatedBy
WHERE Id = @Id;
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Id", current.Id);
                    HrmsDatabase.AddParameter(command, "@ToDate", toDate?.ToDateTime(TimeOnly.MinValue));
                    HrmsDatabase.AddParameter(command, "@MovementKind", ContractPolicy.MovementExtend);
                    HrmsDatabase.AddParameter(command, "@EffectiveDate", effectiveDate.ToDateTime(TimeOnly.MinValue));
                    HrmsDatabase.AddParameter(command, "@UpdatedBy", createdBy);
                });
        }
        else if (movementKind == ContractPolicy.MovementRenew)
        {
            var start = ContractPolicy.NextStartDate(
                ContractPolicy.MovementRenew,
                current?.FromDate ?? effectiveDate,
                current?.ToDate);

            fromDate = start;

            await HrmsDatabase.ExecuteAsync(
                db,
                "UPDATE EmployeeContracts SET IsCurrent = 0 WHERE EmployeeId = @EmployeeId;",
                command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId));

            resultContractId = await HrmsDatabase.ScalarAsync<int>(
                db,
                """
INSERT INTO EmployeeContracts
    (EmployeeId, ContractType, FromDate, ToDate, IsCurrent, PreviousContractId,
     MovementKind, EffectiveDate, Note, CreatedBy)
OUTPUT INSERTED.Id
VALUES (@EmployeeId, @ContractType, @FromDate, @ToDate, 1, @PreviousContractId,
        @MovementKind, @EffectiveDate, @Note, @CreatedBy);
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                    HrmsDatabase.AddParameter(command, "@ContractType", contractType);
                    HrmsDatabase.AddParameter(command, "@FromDate", start.ToDateTime(TimeOnly.MinValue));
                    HrmsDatabase.AddParameter(command, "@ToDate", toDate?.ToDateTime(TimeOnly.MinValue));
                    HrmsDatabase.AddParameter(command, "@PreviousContractId", current?.Id);
                    HrmsDatabase.AddParameter(command, "@MovementKind", ContractPolicy.MovementRenew);
                    HrmsDatabase.AddParameter(command, "@EffectiveDate", effectiveDate.ToDateTime(TimeOnly.MinValue));
                    HrmsDatabase.AddParameter(command, "@Note", notes);
                    HrmsDatabase.AddParameter(command, "@CreatedBy", createdBy);
                });
        }
        else if (current is not null)
        {
            // تعديل: بنود العقد تتغيّر بلا تجديد ولا تمديد.
            fromDate = current.FromDate;
            resultContractId = current.Id;

            await HrmsDatabase.ExecuteAsync(
                db,
                """
UPDATE EmployeeContracts
SET ContractType = @ContractType, ToDate = @ToDate, MovementKind = @MovementKind,
    EffectiveDate = @EffectiveDate, UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @UpdatedBy
WHERE Id = @Id;
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Id", current.Id);
                    HrmsDatabase.AddParameter(command, "@ContractType", contractType);
                    HrmsDatabase.AddParameter(command, "@ToDate", toDate?.ToDateTime(TimeOnly.MinValue));
                    HrmsDatabase.AddParameter(command, "@MovementKind", ContractPolicy.MovementAmend);
                    HrmsDatabase.AddParameter(command, "@EffectiveDate", effectiveDate.ToDateTime(TimeOnly.MinValue));
                    HrmsDatabase.AddParameter(command, "@UpdatedBy", createdBy);
                });
        }

        var movementId = await HrmsDatabase.ScalarAsync<int>(
            db,
            """
INSERT INTO EmployeeContractMovements
    (EmployeeId, ContractId, ResultContractId, MovementKind, ContractType,
     FromDate, ToDate, EffectiveDate, Source, Status, Notes, CreatedBy)
OUTPUT INSERTED.Id
VALUES (@EmployeeId, @ContractId, @ResultContractId, @MovementKind, @ContractType,
        @FromDate, @ToDate, @EffectiveDate, @Source, N'Open', @Notes, @CreatedBy);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                HrmsDatabase.AddParameter(command, "@ContractId", contractId);
                HrmsDatabase.AddParameter(command, "@ResultContractId", resultContractId);
                HrmsDatabase.AddParameter(command, "@MovementKind", movementKind);
                HrmsDatabase.AddParameter(command, "@ContractType", contractType);
                HrmsDatabase.AddParameter(command, "@FromDate", fromDate?.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@ToDate", toDate?.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@EffectiveDate", effectiveDate.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@Source", source);
                HrmsDatabase.AddParameter(command, "@Notes", notes);
                HrmsDatabase.AddParameter(command, "@CreatedBy", createdBy);
            });

        // الرقم المرجعي يُشتقّ من المعرّف بعد الإدراج: مخطط ترقيم مستقل للعقود
        // (نمط كيان) بلا عدّاد منفصل قابل للتسابق.
        await HrmsDatabase.ExecuteAsync(
            db,
            "UPDATE EmployeeContractMovements SET RefNo = @RefNo WHERE Id = @Id;",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", movementId);
                HrmsDatabase.AddParameter(command, "@RefNo", $"CM-{movementId:00000}");
            });

        await transaction.CommitAsync();
        return movementId;
    }

    /// <summary>قفل الحركة — نمط «حركات مقفلة» بكيان: بعد القفل لا تُعدَّل ولا تُحذف.</summary>
    public static async Task LockMovementAsync(ApplicationDbContext db, int id, string? lockedBy) =>
        await HrmsDatabase.ExecuteAsync(
            db,
            """
UPDATE EmployeeContractMovements
SET Status = N'Locked', LockedAt = SYSUTCDATETIME(), LockedBy = @LockedBy
WHERE Id = @Id AND Status = N'Open';
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", id);
                HrmsDatabase.AddParameter(command, "@LockedBy", lockedBy);
            });

    public static async Task DeleteMovementAsync(ApplicationDbContext db, int id) =>
        await HrmsDatabase.ExecuteAsync(
            db,
            "DELETE FROM EmployeeContractMovements WHERE Id = @Id AND Status = N'Open';",
            command => HrmsDatabase.AddParameter(command, "@Id", id));
}
