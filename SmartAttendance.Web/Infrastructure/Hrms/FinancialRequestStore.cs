using System.Data.Common;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// مركز الطلبات المالية (توحيد نمط ZenHR «Financial Requests» + كيان): بدل بناء اعتماد
/// معزول داخل كل شاشة مالية، تمرّ الطلبات المالية (قرض/سُلفة/بدل/استرداد/زيادة راتب) عبر
/// نفس العمود الفقري: صف في <c>SelfServiceRequests</c> + لجنة موافقة من
/// <see cref="ApprovalWorkflowEngine"/> (قالب من <see cref="ApprovalTemplateStore"/>).
/// تفاصيل الطلب المالي (النوع/المبلغ/الأقساط…) تُخزَّن بجدول فرعي، وعند الاعتماد النهائي
/// فقط يُفعَّل الأثر المالي عبر <see cref="ApplyIfFinancialAsync"/> — تماماً كما يطبّق
/// <see cref="DataChangeRequestStore"/> تعديلات البيانات بعد اكتمال اللجنة.
/// نمط self-healing (CREATE idempotent). القرض/السُّلفة تُنشئ سجل <see cref="LoanStore"/>
/// معتمداً؛ البدل/الاسترداد يُنشئ حركة مسير؛ زيادة الراتب تُنشئ وتُطبّق <see cref="SalaryRaiseStore"/>.
/// </summary>
public static class FinancialRequestStore
{
    public const string Loan = "Loan";
    public const string Advance = "Advance";
    public const string Allowance = "Allowance";
    public const string Reimbursement = "Reimbursement";
    public const string Raise = "Raise";

    /// <summary>
    /// نوع طلب مالي: مفتاح ثابت + تسمية عربية (تُخزَّن كـ RequestType على الطلب) +
    /// مفتاح قالب الموافقة (من كتالوج <see cref="ApprovalTemplateStore"/>) + وصف للمنتقي.
    /// </summary>
    public sealed record Kind(string Key, string Label, string TemplateKey, string Icon, string Hint);

    /// <summary>كتالوج الأنواع المالية المتاحة للطلب. الترتيب = ترتيب ظهورها بالمنتقي.</summary>
    public static readonly IReadOnlyList<Kind> Catalog = new List<Kind>
    {
        new(Loan,          "قرض",            "Loan",          "🏦", "مبلغ يُقسَّم على عدة أشهر ويُخصم آلياً من المسير"),
        new(Advance,       "سُلفة",          "Loan",          "💵", "مبلغ يُسترجع عادةً بقسط واحد أو أقساط قليلة"),
        new(Allowance,     "بدل مالي",       "FinancialClaim","➕", "مبلغ يُضاف لراتب الموظف (بدل/مكافأة) داخل المسير"),
        new(Reimbursement, "استرداد نفقات",  "FinancialClaim","🧾", "استرداد مصاريف تكبّدها الموظف (خارج الراتب)"),
        new(Raise,         "زيادة راتب",     "SalaryIncrease","📈", "تعديل دائم للراتب الأساسي (مبلغ أو نسبة)"),
    };

    public static Kind? KindOf(string key) => Catalog.FirstOrDefault(k => k.Key == key);
    public static string KindLabel(string key) => KindOf(key)?.Label ?? key;

    /// <summary>تسميات الأنواع المالية المخزَّنة كـ RequestType — بها يُميَّز الطلب المالي وتُحلّ قوالبه.</summary>
    public static IReadOnlyDictionary<string, string> RequestTypeToTemplate { get; } =
        Catalog.ToDictionary(k => k.Label, k => k.TemplateKey, StringComparer.OrdinalIgnoreCase);

    public sealed class Detail
    {
        public int Id { get; set; }
        public int RequestId { get; set; }
        public string Kind { get; set; } = Loan;
        public decimal Amount { get; set; }
        public int InstallmentCount { get; set; } = 1;
        public int StartYear { get; set; }
        public int StartMonth { get; set; }
        public string RaiseType { get; set; } = SalaryRaiseStore.ByAmount; // Amount | Percentage
        public string PaymentType { get; set; } = "InSalary";              // InSalary | OutSalary
        public bool Taxable { get; set; }
        public string? Reason { get; set; }
        public string? Note { get; set; }
        public bool Applied { get; set; }
        public int? AppliedRefId { get; set; }

        public string KindLabelText => KindLabel(Kind);
        public string PeriodText => $"{StartMonth:00}/{StartYear}";
    }

    public static async Task EnsureAsync(ApplicationDbContext db)
    {
        await ApprovalWorkflowEngine.EnsureAsync(db);
        await HrmsDatabase.ExecuteAsync(db, """
IF OBJECT_ID('FinancialRequestDetails','U') IS NULL
BEGIN
    CREATE TABLE FinancialRequestDetails(
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        RequestId int NOT NULL,
        Kind nvarchar(20) NOT NULL,
        Amount decimal(18,2) NOT NULL DEFAULT(0),
        InstallmentCount int NOT NULL DEFAULT(1),
        StartYear int NOT NULL,
        StartMonth int NOT NULL,
        RaiseType nvarchar(20) NOT NULL DEFAULT(N'Amount'),
        PaymentType nvarchar(20) NOT NULL DEFAULT(N'InSalary'),
        Taxable bit NOT NULL DEFAULT(0),
        Reason nvarchar(300) NULL,
        Note nvarchar(500) NULL,
        Applied bit NOT NULL DEFAULT(0),
        AppliedRefId int NULL,
        AppliedAt datetime2 NULL
    );
    CREATE INDEX IX_FinancialRequestDetails_Request ON FinancialRequestDetails(RequestId);
END;
""");
    }

    public sealed class EmployeeBasic
    {
        public int Id { get; set; }
        public string No { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public decimal Basic { get; set; }
    }

    /// <summary>الموظفون النشطون مع راتبهم الأساسي الحالي (لمنتقي النموذج ومعاينة الزيادة).</summary>
    /// <summary>منتقي الموظفين — بنطاق الشركات (تسريب الـlookup يكشف هيكل شركة أخرى وراتبها الأساسي).</summary>
    public static async Task<List<EmployeeBasic>> EmployeeBasicsAsync(
        ApplicationDbContext db, Security.CompanyScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.IsDeniedAll) return new List<EmployeeBasic>();
        await EnsureAsync(db);
        return await HrmsDatabase.QueryAsync(db, $"""
SELECT e.Id, ISNULL(e.EmployeeNo, N'') AS EmployeeNo, ISNULL(e.FullName, N'') AS FullName,
       ISNULL(f.BasicSalary, 0) AS BasicSalary
FROM Employees e
LEFT JOIN EmployeeFinancialInfos f ON f.EmployeeId = e.Id AND ISNULL(f.IsDeleted,0) = 0
WHERE ISNULL(e.IsDeleted,0) = 0 AND ISNULL(e.IsActive,1) = 1
  AND {Security.EmployeeCompanyGuard.ListFilter(scope, "e.CompanyId")}
ORDER BY e.FullName;
""",
            null,
            r => new EmployeeBasic
            {
                Id = HrmsDatabase.GetInt(r, "Id"),
                No = HrmsDatabase.GetString(r, "EmployeeNo"),
                Name = HrmsDatabase.GetString(r, "FullName"),
                Basic = r["BasicSalary"] is decimal b ? b : 0
            });
    }

    /// <summary>
    /// يقدّم طلباً مالياً: يُنشئ صف <c>SelfServiceRequests</c> (RequestType = تسمية النوع)
    /// وصف التفاصيل المالية، ثم يبدأ سريان الموافقة عبر <see cref="ApprovalWorkflowEngine.StartAsync"/>
    /// (قالب النوع من كتالوج القوالب). يرجع رقم الطلب. المُقدِّم قد يكون HR (نيابةً) أو الموظف نفسه.
    /// </summary>
    public static async Task<int> SubmitAsync(ApplicationDbContext db, Detail detail, int employeeId, string createdBy)
    {
        await EnsureAsync(db);
        var kind = KindOf(detail.Kind) ?? Catalog[0];
        detail.Kind = kind.Key;
        if (detail.InstallmentCount < 1) detail.InstallmentCount = 1;

        var requestId = await HrmsDatabase.ScalarAsync<int>(db, """
INSERT INTO SelfServiceRequests
(EmployeeId, RequestType, RequestDate, Reason, Status, CurrentStep, CreatedBy)
VALUES
(@Emp, @Type, CAST(SYSUTCDATETIME() AS date), @Reason, 'Pending', 'Direct Manager', @By);

DECLARE @RequestId int = SCOPE_IDENTITY();

INSERT INTO ApprovalHistories (RequestId, StepName, Action, ActionBy, Notes)
VALUES (@RequestId, 'Submission', 'Submitted', @By, @Reason);

INSERT INTO SystemNotifications (Title, Message, TargetRole, Url)
VALUES (N'طلب مالي جديد', N'طلب ' + @Type + N' بانتظار الموافقة', 'HR', '/Payroll/FinancialRequests');

SELECT @RequestId;
""",
            cmd =>
            {
                HrmsDatabase.AddParameter(cmd, "@Emp", employeeId);
                HrmsDatabase.AddParameter(cmd, "@Type", kind.Label);
                HrmsDatabase.AddParameter(cmd, "@Reason", (object?)detail.Reason ?? DBNull.Value);
                HrmsDatabase.AddParameter(cmd, "@By", createdBy);
            });

        if (requestId <= 0) return 0;

        await HrmsDatabase.ExecuteAsync(db, """
INSERT INTO FinancialRequestDetails
(RequestId, Kind, Amount, InstallmentCount, StartYear, StartMonth, RaiseType, PaymentType, Taxable, Reason, Note)
VALUES
(@Req, @Kind, @Amount, @Count, @SYear, @SMonth, @RaiseType, @PayType, @Taxable, @Reason, @Note);
""",
            cmd =>
            {
                HrmsDatabase.AddParameter(cmd, "@Req", requestId);
                HrmsDatabase.AddParameter(cmd, "@Kind", detail.Kind);
                HrmsDatabase.AddParameter(cmd, "@Amount", detail.Amount);
                HrmsDatabase.AddParameter(cmd, "@Count", detail.InstallmentCount);
                HrmsDatabase.AddParameter(cmd, "@SYear", detail.StartYear);
                HrmsDatabase.AddParameter(cmd, "@SMonth", detail.StartMonth);
                HrmsDatabase.AddParameter(cmd, "@RaiseType", detail.RaiseType);
                HrmsDatabase.AddParameter(cmd, "@PayType", detail.PaymentType);
                HrmsDatabase.AddParameter(cmd, "@Taxable", detail.Taxable);
                HrmsDatabase.AddParameter(cmd, "@Reason", (object?)detail.Reason ?? DBNull.Value);
                HrmsDatabase.AddParameter(cmd, "@Note", (object?)detail.Note ?? DBNull.Value);
            });

        // سريان الموافقة: قالب النوع المالي (Loan/FinancialClaim/SalaryIncrease) يُحلّ من التسمية.
        await ApprovalWorkflowEngine.StartAsync(db, requestId, kind.Label, employeeId);
        return requestId;
    }

    public static async Task<Detail?> GetDetailAsync(ApplicationDbContext db, int requestId)
    {
        await EnsureAsync(db);
        var list = await HrmsDatabase.QueryAsync(db,
            "SELECT * FROM FinancialRequestDetails WHERE RequestId = @r",
            cmd => HrmsDatabase.AddParameter(cmd, "@r", requestId),
            Map);
        return list.FirstOrDefault();
    }

    /// <summary>تفاصيل مجموعة طلبات (للعرض بشاشة الموافقات) — مفتاحها RequestId.</summary>
    public static async Task<Dictionary<int, Detail>> ListForRequestsAsync(ApplicationDbContext db, IEnumerable<int> requestIds)
    {
        var ids = requestIds.Distinct().ToList();
        if (ids.Count == 0) return new();
        await EnsureAsync(db);
        var inClause = string.Join(",", ids.Select((_, i) => $"@r{i}"));
        var rows = await HrmsDatabase.QueryAsync(db,
            $"SELECT * FROM FinancialRequestDetails WHERE RequestId IN ({inClause})",
            cmd => { for (var i = 0; i < ids.Count; i++) HrmsDatabase.AddParameter(cmd, $"@r{i}", ids[i]); },
            Map);
        return rows.GroupBy(r => r.RequestId).ToDictionary(g => g.Key, g => g.First());
    }

    /// <summary>صف مركز الطلبات المالية = طلب الخدمة الذاتية + تفاصيله المالية + بيانات الموظف.</summary>
    public sealed class Row
    {
        public int RequestId { get; set; }
        public int EmployeeId { get; set; }
        public string EmployeeNo { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string RequestType { get; set; } = string.Empty;
        public string Status { get; set; } = "Pending";
        public string CurrentStep { get; set; } = string.Empty;
        public DateTime? CreatedAt { get; set; }
        public Detail Detail { get; set; } = new();

        public string StatusText => Status switch
        {
            "Pending" => "قيد الموافقة",
            "Approved" => "معتمد",
            "Rejected" => "مرفوض",
            _ => Status
        };
    }

    /// <summary>قائمة الطلبات المالية للمركز، مع فلاتر النوع/الحالة/البحث.</summary>
    public static async Task<List<Row>> ListAsync(
        ApplicationDbContext db, Security.CompanyScope scope,
        string? kind = null, string? status = null, string? search = null)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.IsDeniedAll) return new List<Row>();
        await EnsureAsync(db);
        var rows = await HrmsDatabase.QueryAsync(db, $"""
SELECT r.Id AS RequestId, r.EmployeeId, r.RequestType, ISNULL(r.Status,'Pending') AS Status,
       ISNULL(r.CurrentStep,'') AS CurrentStep, r.CreatedAt,
       ISNULL(e.EmployeeNo, N'') AS EmployeeNo, ISNULL(e.FullName, N'') AS FullName,
       ISNULL(d.Name, N'') AS DepartmentName,
       f.Id AS Id, f.Kind, f.Amount, f.InstallmentCount, f.StartYear, f.StartMonth, f.RaiseType,
       f.PaymentType, f.Taxable, f.Reason, f.Note, f.Applied, f.AppliedRefId
FROM FinancialRequestDetails f
INNER JOIN SelfServiceRequests r ON r.Id = f.RequestId
INNER JOIN Employees e ON e.Id = r.EmployeeId
LEFT JOIN Departments d ON d.Id = e.DepartmentId
WHERE {Security.EmployeeCompanyGuard.ListFilter(scope, "e.CompanyId")}
ORDER BY r.CreatedAt DESC;
""",
            null,
            r => new Row
            {
                RequestId = HrmsDatabase.GetInt(r, "RequestId"),
                EmployeeId = HrmsDatabase.GetInt(r, "EmployeeId"),
                EmployeeNo = HrmsDatabase.GetString(r, "EmployeeNo"),
                EmployeeName = HrmsDatabase.GetString(r, "FullName"),
                Department = HrmsDatabase.GetString(r, "DepartmentName"),
                RequestType = HrmsDatabase.GetString(r, "RequestType"),
                Status = HrmsDatabase.GetString(r, "Status"),
                CurrentStep = HrmsDatabase.GetString(r, "CurrentStep"),
                CreatedAt = HrmsDatabase.GetDateTime(r, "CreatedAt"),
                Detail = Map(r)
            });

        if (!string.IsNullOrWhiteSpace(kind)) rows = rows.Where(r => r.Detail.Kind == kind).ToList();
        if (!string.IsNullOrWhiteSpace(status)) rows = rows.Where(r => r.Status == status).ToList();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var v = search.Trim();
            rows = rows.Where(r =>
                r.EmployeeNo.Contains(v, StringComparison.OrdinalIgnoreCase) ||
                r.EmployeeName.Contains(v, StringComparison.OrdinalIgnoreCase) ||
                (r.Detail.Reason ?? "").Contains(v, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        return rows;
    }

    /// <summary>حذف طلب مالي معلّق (قبل الاعتماد) — يزيل التفاصيل والسريان وصف الطلب.</summary>
    public static async Task<bool> DeletePendingAsync(ApplicationDbContext db, int requestId)
    {
        await EnsureAsync(db);
        var pending = await HrmsDatabase.ScalarAsync<int>(db,
            "SELECT COUNT(1) FROM SelfServiceRequests WHERE Id=@r AND Status='Pending'",
            cmd => HrmsDatabase.AddParameter(cmd, "@r", requestId));
        if (pending == 0) return false;

        await HrmsDatabase.ExecuteAsync(db, """
DELETE FROM FinancialRequestDetails WHERE RequestId=@r;
IF OBJECT_ID('ApprovalRequestSteps','U') IS NOT NULL DELETE FROM ApprovalRequestSteps WHERE RequestId=@r;
IF OBJECT_ID('ApprovalRequestFlows','U') IS NOT NULL DELETE FROM ApprovalRequestFlows WHERE RequestId=@r;
IF OBJECT_ID('ApprovalHistories','U') IS NOT NULL DELETE FROM ApprovalHistories WHERE RequestId=@r;
DELETE FROM SelfServiceRequests WHERE Id=@r;
""",
            cmd => HrmsDatabase.AddParameter(cmd, "@r", requestId));
        return true;
    }

    /// <summary>
    /// يُفعّل الأثر المالي لطلب مُعتمد نهائياً (يُستدعى بعد آخر خطوة موافقة من شاشة الموافقات
    /// أو المركز المالي). آمن التكرار (Applied=1 يمنع الإعادة). يرجع <c>true</c> إن كان
    /// الطلب مالياً وطُبّق أثره:
    /// <list type="bullet">
    /// <item>قرض/سُلفة → إنشاء سجل <see cref="LoanStore"/> معتمداً (يولّد جدول الأقساط).</item>
    /// <item>بدل → حركة مسير «دخل» داخل الراتب.</item>
    /// <item>استرداد نفقات → حركة مسير «دخل» خارج الراتب.</item>
    /// <item>زيادة راتب → إنشاء وتطبيق <see cref="SalaryRaiseStore"/> (يحدّث الأساسي).</item>
    /// </list>
    /// </summary>
    public static async Task<bool> ApplyIfFinancialAsync(ApplicationDbContext db, int requestId, string actor, string? ip)
    {
        await EnsureAsync(db);

        var detail = await GetDetailAsync(db, requestId);
        if (detail == null || detail.Applied) return false;

        var employeeId = await HrmsDatabase.ScalarAsync<int>(db,
            "SELECT ISNULL((SELECT EmployeeId FROM SelfServiceRequests WHERE Id=@r), 0)",
            cmd => HrmsDatabase.AddParameter(cmd, "@r", requestId));
        if (employeeId <= 0) return false;

        int? refId = null;
        string effect;

        switch (detail.Kind)
        {
            case Loan:
            case Advance:
            {
                // مسارٌ داخليّ بعد اعتماد الطلب: التخويل حُسم بمسار الاعتماد (يُفحَص
                // بالنطاق)، والموظف هنا من صفّ الطلب المعتمَد لا من مدخل مستخدم —
                // فالنطاق غير مقيَّد عمداً كي لا يُرفَض تطبيقُ طلبٍ اعتُمد بحقّه.
                var loanId = await LoanStore.SaveAsync(db, Security.CompanyScope.Unrestricted(), new LoanStore.Loan_
                {
                    EmployeeId = employeeId,
                    LoanType = detail.Kind == Advance ? LoanStore.Advance : LoanStore.Loan,
                    Amount = detail.Amount,
                    InstallmentCount = detail.InstallmentCount,
                    StartYear = detail.StartYear,
                    StartMonth = detail.StartMonth,
                    Reason = detail.Reason,
                    Note = $"من طلب مالي #{requestId}",
                    Status = LoanStore.Approved
                }, actor);
                refId = loanId;
                effect = $"{KindLabel(detail.Kind)} معتمد (#{loanId}) — مبلغ {detail.Amount:0.##} على {detail.InstallmentCount} قسط";
                break;
            }

            case Allowance:
            case Reimbursement:
            {
                var outSalary = detail.Kind == Reimbursement;
                var txId = await PayrollTransactionStore.SaveAsync(db, new PayrollTransactionStore.Transaction
                {
                    EmployeeId = employeeId,
                    Year = detail.StartYear,
                    Month = detail.StartMonth,
                    ItemName = $"{KindLabel(detail.Kind)}{(string.IsNullOrWhiteSpace(detail.Reason) ? "" : " - " + detail.Reason)}",
                    Amount = detail.Amount,
                    TxType = PayrollTransactionStore.Income,
                    Taxable = detail.Taxable,
                    PaymentType = outSalary ? "OutSalary" : "InSalary",
                    Source = "طلب مالي",
                    Status = "Approved",
                    Note = $"من طلب مالي #{requestId}"
                }, actor);
                refId = txId;
                effect = $"{KindLabel(detail.Kind)} — حركة دخل (#{txId}) بمبلغ {detail.Amount:0.##}";
                break;
            }

            case Raise:
            {
                var oldBasic = await HrmsDatabase.ScalarAsync<decimal>(db,
                    "SELECT ISNULL((SELECT TOP 1 BasicSalary FROM EmployeeFinancialInfos WHERE EmployeeId=@e AND ISNULL(IsDeleted,0)=0), 0)",
                    cmd => HrmsDatabase.AddParameter(cmd, "@e", employeeId));
                var newBasic = detail.RaiseType == SalaryRaiseStore.ByPercentage
                    ? Math.Round(oldBasic * (1 + detail.Amount / 100m), 2)
                    : oldBasic + detail.Amount;

                // تطبيق داخلي بعد اعتماد الطلب المالي: معرّف الموظف من صفّ الطلب المُعتمَد
                // (لا من المتصفح) وقد مرّ بعزل الطلبات المالية عند السرد/الاعتماد، فالنطاق
                // هنا غير مقيَّد عمداً (نمط المسارات الداخلية الموثوقة).
                var internalScope = Security.CompanyScope.Unrestricted();
                var raiseId = await SalaryRaiseStore.SaveAsync(db, internalScope, new SalaryRaiseStore.Raise
                {
                    EmployeeId = employeeId,
                    OldBasic = oldBasic,
                    RaiseType = detail.RaiseType,
                    RaiseValue = detail.Amount,
                    NewBasic = newBasic,
                    EffectiveDate = new DateOnly(detail.StartYear, Math.Clamp(detail.StartMonth, 1, 12), 1),
                    Reason = detail.Reason,
                    Note = $"من طلب مالي #{requestId}",
                    Status = "Approved"
                }, actor);
                await SalaryRaiseStore.ApplyAsync(db, internalScope, raiseId, actor);
                refId = raiseId;
                effect = $"زيادة راتب معتمدة (#{raiseId}) — الأساسي {oldBasic:0.##} ← {newBasic:0.##}";
                break;
            }

            default:
                return false;
        }

        await HrmsDatabase.ExecuteAsync(db, """
UPDATE FinancialRequestDetails SET Applied=1, AppliedRefId=@ref, AppliedAt=SYSUTCDATETIME() WHERE RequestId=@r;

IF OBJECT_ID('AuditLogs','U') IS NOT NULL
    INSERT INTO AuditLogs (EntityName, EntityId, Action, NewValues, UserName, IpAddress)
    VALUES ('FinancialRequest', CAST(@r AS nvarchar(80)), 'Financial Request Applied', @effect, @actor, @ip);
""",
            cmd =>
            {
                HrmsDatabase.AddParameter(cmd, "@r", requestId);
                HrmsDatabase.AddParameter(cmd, "@ref", (object?)refId ?? DBNull.Value);
                HrmsDatabase.AddParameter(cmd, "@effect", effect);
                HrmsDatabase.AddParameter(cmd, "@actor", actor);
                HrmsDatabase.AddParameter(cmd, "@ip", (object?)ip ?? DBNull.Value);
            });
        return true;
    }

    private static Detail Map(DbDataReader r) => new()
    {
        Id = HrmsDatabase.GetInt(r, "Id"),
        RequestId = HrmsDatabase.GetInt(r, "RequestId"),
        Kind = HrmsDatabase.GetString(r, "Kind") is { Length: > 0 } k ? k : Loan,
        Amount = r["Amount"] is decimal a ? a : 0,
        InstallmentCount = HrmsDatabase.GetInt(r, "InstallmentCount"),
        StartYear = HrmsDatabase.GetInt(r, "StartYear"),
        StartMonth = HrmsDatabase.GetInt(r, "StartMonth"),
        RaiseType = HrmsDatabase.GetString(r, "RaiseType") is { Length: > 0 } rt ? rt : SalaryRaiseStore.ByAmount,
        PaymentType = HrmsDatabase.GetString(r, "PaymentType") is { Length: > 0 } pt ? pt : "InSalary",
        Taxable = HrmsDatabase.GetBool(r, "Taxable"),
        Reason = HrmsDatabase.GetString(r, "Reason") is { Length: > 0 } rs ? rs : null,
        Note = HrmsDatabase.GetString(r, "Note") is { Length: > 0 } n ? n : null,
        Applied = HrmsDatabase.GetBool(r, "Applied"),
        AppliedRefId = HrmsDatabase.GetNullableInt(r, "AppliedRefId")
    };
}
