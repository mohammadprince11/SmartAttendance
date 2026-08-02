using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// إصدار «كتاب العقوبة» آلياً لحظة اتخاذ الإجراء — البند الخامس من دراسة كيان.
///
/// كانت القوالب تُحرَّر بشاشة التهيئة و**لا يقرأها أحد**: تُختار «كتاب عقوبة» ثم
/// لا يخرج كتاب. والفجوة ليست تجميلية — الكتاب الرسمي هو ما يُبلَّغ به الموظف وما
/// يُبنى عليه حقّ الاعتراض، فبدونه تصدر العقوبة بلا وثيقةٍ يعترض عليها.
///
/// ⚠️ **يُصدَر مرّة واحدة** (<c>LetterIssuedAt IS NULL</c>): إعادةُ إصداره تبدّل نصّ
/// كتابٍ قد يكون الموظف وقّعه، وتُعيد إشعاره بعقوبةٍ يعرفها. والإصدار **لا يفشِل
/// الإجراء**: مخالفةٌ اعتُمدت وكتابُها لم يخرج تُعالَج بإعادة المحاولة، أمّا اعتمادٌ
/// يُلغى لأن نصّاً لم يُركَّب فخسارةٌ صافية.
/// </summary>
public static class DisciplinaryLetterStore
{
    /// <summary>
    /// يُصدر الكتاب لحالة مخالفة إن لم يكن صدر. يعيد <c>true</c> إن صدر الآن.
    /// </summary>
    public static async Task<bool> IssueAsync(ApplicationDbContext db, int caseId, string issuedBy)
    {
        await ViolationCaseSchema.EnsureAsync(db);
        await DisciplinarySchema.EnsureAsync(db);

        var context = await LoadContextAsync(db, caseId, issuedBy);
        if (context is null)
        {
            return false;
        }

        var settings = await DisciplinaryConfigStore.LoadSettingsAsync(db);
        var template = await PickTemplateAsync(db, settings.DefaultTemplateType);

        var issuedOn = DateOnly.FromDateTime(DateTime.Today);
        var subject = DisciplinaryLetter.Render(
            template?.Subject is { Length: > 0 } s ? s : DisciplinaryLetter.FallbackSubject,
            context,
            issuedOn);
        var body = DisciplinaryLetter.Render(
            template?.Body is { Length: > 0 } b ? b : DisciplinaryLetter.FallbackBody,
            context,
            issuedOn);

        // الشرط بالجملة نفسها لا بفحصٍ سابق: طلبان متزامنان على نفس الحالة يمرّ
        // أحدهما فقط، فلا يخرج كتابان بتاريخين.
        await HrmsDatabase.ExecuteAsync(
            db,
            """
UPDATE EmployeeViolationCases
SET LetterSubject = @Subject,
    LetterBody = @Body,
    LetterTemplateId = @TemplateId,
    LetterIssuedAt = SYSUTCDATETIME(),
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @Id AND ISNULL(IsDeleted, 0) = 0 AND LetterIssuedAt IS NULL;

INSERT INTO SystemNotifications (Title, Message, TargetRole, Url)
SELECT N'صدر كتاب عقوبة بحقك',
       N'صدر كتاب العقوبة للمخالفة ' + v.ReferenceNo + N' — يمكنك الاطلاع عليه والاعتراض خلال المهلة.',
       'Employee', '/EmployeePortal?tab=requests'
FROM EmployeeViolationCases v
WHERE v.Id = @Id AND v.LetterIssuedAt IS NOT NULL AND @@ROWCOUNT > 0;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", caseId);
                HrmsDatabase.AddParameter(command, "@Subject", Truncate(subject, 300));
                HrmsDatabase.AddParameter(command, "@Body", body);
                HrmsDatabase.AddParameter(
                    command,
                    "@TemplateId",
                    template is null ? (object)DBNull.Value : template.Id);
            });

        return true;
    }

    private sealed record TemplatePick(int Id, string Subject, string Body);

    /// <summary>
    /// القالب النشط لنوع الكتاب المختار بالتهيئة، والافتراضي منه أوّلاً. لا قالب
    /// ⟹ <c>null</c> ويتولّى النصّ الاحتياطي — لا يُترك الموظف بلا كتاب.
    /// </summary>
    private static async Task<TemplatePick?> PickTemplateAsync(ApplicationDbContext db, string templateType)
    {
        var rows = await HrmsDatabase.QueryAsync(
            db,
            """
SELECT TOP 1 Id, ISNULL(Subject, N'') AS Subject, ISNULL(Body, N'') AS Body
FROM DisciplinaryMessageTemplates
WHERE ISNULL(IsActive, 1) = 1 AND TemplateType = @Type
ORDER BY IsDefault DESC, Id;
""",
            command => HrmsDatabase.AddParameter(command, "@Type", templateType),
            reader => new TemplatePick(
                HrmsDatabase.GetInt(reader, "Id"),
                HrmsDatabase.GetString(reader, "Subject"),
                HrmsDatabase.GetString(reader, "Body")));

        return rows.FirstOrDefault();
    }

    private static async Task<DisciplinaryLetter.Context?> LoadContextAsync(
        ApplicationDbContext db,
        int caseId,
        string issuedBy)
    {
        var rows = await HrmsDatabase.QueryAsync(
            db,
            """
SELECT TOP 1
    ISNULL(v.ReferenceNo, N'') AS ReferenceNo,
    ISNULL(e.FullName, N'') AS EmployeeName,
    ISNULL(e.EmployeeNo, N'') AS EmployeeCode,
    ISNULL(d.Name, N'-') AS DepartmentName,
    ISNULL(e.Position, N'-') AS Position,
    v.EventDate,
    ISNULL(v.ViolationCategory, N'') AS ViolationCategory,
    ISNULL(v.ViolationTitle, N'') AS ViolationTitle,
    ISNULL(v.Notes, N'') AS Notes,
    ISNULL(v.FinalPenaltyAction, ISNULL(v.ProposedAction, N'')) AS PenaltyAction,
    ISNULL(v.FinancialImpactType, N'None') AS FinancialImpactType,
    ISNULL(v.FinancialImpactValue, 0) AS FinancialImpactValue,
    ISNULL(v.DeductionAmount, 0) AS DeductionAmount,
    ISNULL(t.ArticleNo, N'') AS ArticleNo,
    ISNULL(v.ApprovedBy, N'') AS ApprovedBy
FROM EmployeeViolationCases v
LEFT JOIN Employees e ON e.Id = v.EmployeeId
LEFT JOIN Departments d ON d.Id = e.DepartmentId
LEFT JOIN DisciplinaryViolationTypes t ON t.Id = v.ViolationTypeId
WHERE v.Id = @Id AND ISNULL(v.IsDeleted, 0) = 0 AND v.LetterIssuedAt IS NULL;
""",
            command => HrmsDatabase.AddParameter(command, "@Id", caseId),
            reader =>
            {
                var approvedBy = HrmsDatabase.GetString(reader, "ApprovedBy");

                return new DisciplinaryLetter.Context(
                    ReferenceNo: HrmsDatabase.GetString(reader, "ReferenceNo"),
                    EmployeeName: HrmsDatabase.GetString(reader, "EmployeeName"),
                    EmployeeCode: HrmsDatabase.GetString(reader, "EmployeeCode"),
                    Department: HrmsDatabase.GetString(reader, "DepartmentName"),
                    Position: HrmsDatabase.GetString(reader, "Position"),
                    EventDate: HrmsDatabase.GetDateOnly(reader, "EventDate") ?? DateOnly.FromDateTime(DateTime.Today),
                    ViolationCategory: HrmsDatabase.GetString(reader, "ViolationCategory"),
                    ViolationName: HrmsDatabase.GetString(reader, "ViolationTitle"),
                    Description: HrmsDatabase.GetString(reader, "Notes"),
                    PenaltyAction: HrmsDatabase.GetString(reader, "PenaltyAction"),
                    FinancialImpact: DisciplinaryLetter.FinancialImpactText(
                        HrmsDatabase.GetString(reader, "FinancialImpactType"),
                        HrmsDatabase.GetNullableDecimal(reader, "FinancialImpactValue") ?? 0),
                    DeductionAmount: HrmsDatabase.GetNullableDecimal(reader, "DeductionAmount") ?? 0,
                    ArticleNo: HrmsDatabase.GetString(reader, "ArticleNo"),
                    ApprovedBy: string.IsNullOrWhiteSpace(approvedBy) ? issuedBy : approvedBy);
            });

        return rows.FirstOrDefault();
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
