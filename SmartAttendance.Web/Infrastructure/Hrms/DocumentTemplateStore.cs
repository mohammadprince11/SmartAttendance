using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// قوالب الوثائق وأرشيف المولَّد منها (منشئ الوثائق — المرحلة الأولى).
///
/// ⭐ <b>القرار المحوري: الوثيقة المولَّدة تُخزَّن بنصّها النهائي لا بمرجعٍ للقالب.</b>
/// شهادةٌ صدرت وسُلِّمت للبنك واقعةٌ تاريخية؛ لو حُسبت عند كل عرض لتغيّر مضمونها
/// بتعديل القالب أو بترقية راتب — فتصير الوثيقة المؤرشفة كذبةً على نفسها.
/// </summary>
public static class DocumentTemplateStore
{
    public sealed record Template(
        int Id,
        string Name,
        string? NameEn,
        string? Description,
        string? Body,
        string ConditionsJson,
        string? RefPrefix,
        bool AllowEmployeeRequest,
        bool IsActive)
    {
        public HrConditions.ConditionSet Conditions => HrConditions.Deserialize(ConditionsJson);
        public List<string> Tokens => DocumentTokenEngine.ExtractTokens(Body);
    }

    public sealed record Generated(
        int Id,
        string? ReferenceNo,
        int TemplateId,
        string? TemplateName,
        int EmployeeId,
        string EmployeeName,
        string EmployeeNo,
        string? BodyHtml,
        string? UnresolvedTokens,
        DateOnly IssuedOn,
        string? IssuedBy,
        string? Source,
        string? Notes)
    {
        public bool HasUnresolved => !string.IsNullOrWhiteSpace(UnresolvedTokens);
    }

    // ── القوالب ────────────────────────────────────────────────────────────────

    public static async Task<List<Template>> LoadTemplatesAsync(ApplicationDbContext db, bool activeOnly = false)
    {
        var filter = activeOnly ? " AND IsActive = 1" : string.Empty;

        return await HrmsDatabase.QueryAsync(
            db,
            $"""
SELECT Id, Name, NameEn, Description, Body, ISNULL(ConditionsJson, N'') AS ConditionsJson,
       RefPrefix, AllowEmployeeRequest, IsActive
FROM DocumentTemplates
WHERE ISNULL(IsDeleted, 0) = 0{filter}
ORDER BY Name;
""",
            command => { },
            reader => new Template(
                HrmsDatabase.GetInt(reader, "Id"),
                HrmsDatabase.GetString(reader, "Name"),
                HrmsDatabase.GetString(reader, "NameEn"),
                HrmsDatabase.GetString(reader, "Description"),
                HrmsDatabase.GetString(reader, "Body"),
                HrmsDatabase.GetString(reader, "ConditionsJson"),
                HrmsDatabase.GetString(reader, "RefPrefix"),
                HrmsDatabase.GetBool(reader, "AllowEmployeeRequest"),
                HrmsDatabase.GetBool(reader, "IsActive")));
    }

    public static async Task<Template?> FindTemplateAsync(ApplicationDbContext db, int id) =>
        (await LoadTemplatesAsync(db)).FirstOrDefault(row => row.Id == id);

    /// <summary>يحفظ القالب بعد **تنقية** نصّه — لا يُخزَّن HTML خام أبداً.</summary>
    public static async Task<int> SaveTemplateAsync(
        ApplicationDbContext db,
        int id,
        string name,
        string? nameEn,
        string? description,
        string? body,
        HrConditions.ConditionSet conditions,
        string? refPrefix,
        bool allowEmployeeRequest,
        bool isActive,
        string? user)
    {
        var clean = DocumentHtmlSanitizer.Sanitize(body);

        if (id > 0)
        {
            await HrmsDatabase.ExecuteAsync(
                db,
                """
UPDATE DocumentTemplates
SET Name = @Name, NameEn = @NameEn, Description = @Description, Body = @Body,
    ConditionsJson = @Conditions, RefPrefix = @RefPrefix,
    AllowEmployeeRequest = @AllowRequest, IsActive = @IsActive, UpdatedAt = SYSUTCDATETIME()
WHERE Id = @Id;
""",
                command => Bind(command, id, name, nameEn, description, clean, conditions, refPrefix, allowEmployeeRequest, isActive, user));

            return id;
        }

        return await HrmsDatabase.ScalarAsync<int>(
            db,
            """
INSERT INTO DocumentTemplates
    (Name, NameEn, Description, Body, ConditionsJson, RefPrefix, AllowEmployeeRequest, IsActive, CreatedBy)
OUTPUT INSERTED.Id
VALUES (@Name, @NameEn, @Description, @Body, @Conditions, @RefPrefix, @AllowRequest, @IsActive, @CreatedBy);
""",
            command => Bind(command, id, name, nameEn, description, clean, conditions, refPrefix, allowEmployeeRequest, isActive, user));
    }

    private static void Bind(
        System.Data.Common.DbCommand command,
        int id, string name, string? nameEn, string? description, string body,
        HrConditions.ConditionSet conditions, string? refPrefix, bool allowRequest, bool isActive, string? user)
    {
        if (id > 0) HrmsDatabase.AddParameter(command, "@Id", id);
        HrmsDatabase.AddParameter(command, "@Name", name);
        HrmsDatabase.AddParameter(command, "@NameEn", nameEn);
        HrmsDatabase.AddParameter(command, "@Description", description);
        HrmsDatabase.AddParameter(command, "@Body", body);
        HrmsDatabase.AddParameter(command, "@Conditions", HrConditions.Serialize(conditions));
        HrmsDatabase.AddParameter(command, "@RefPrefix", refPrefix);
        HrmsDatabase.AddParameter(command, "@AllowRequest", allowRequest);
        HrmsDatabase.AddParameter(command, "@IsActive", isActive);
        if (id <= 0) HrmsDatabase.AddParameter(command, "@CreatedBy", user);
    }

    public static async Task DeleteTemplateAsync(ApplicationDbContext db, int id) =>
        await HrmsDatabase.ExecuteAsync(
            db,
            "UPDATE DocumentTemplates SET IsDeleted = 1, UpdatedAt = SYSUTCDATETIME() WHERE Id = @Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", id));

    // ── مصدر بيانات الرموز ─────────────────────────────────────────────────────

    /// <summary>
    /// يقرأ ما لا يحمله <see cref="HrConditionFacts.EmployeeRow"/> من بيانات الرموز
    /// (الأسماء والمسمّيات والعقد الحالي). استعلامٌ واحد لموظف واحد — التوليد
    /// الجماعي يستدعيه بحلقة، وهو مقبول لأن التوليد فعلٌ مقصود لا صفحةُ عرض.
    /// </summary>
    public static async Task<DocumentTokenEngine.EmployeeExtras?> LoadExtrasAsync(
        ApplicationDbContext db, int employeeId)
    {
        var rows = await HrmsDatabase.QueryAsync(
            db,
            """
SELECT e.FullName, e.FirstNameEn, e.LastNameEn, e.FirstName, e.LastName,
       e.NationalId, e.PassportNo, e.Phone, e.Email, e.Position,
       ISNULL(d.Name, N'') AS DepartmentName, ISNULL(b.Name, N'') AS BranchName,
       ISNULL(m.FullName, N'') AS ManagerName,
       ISNULL(p.Name, N'') AS PositionName,
       ec.BankName,
       c.ContractNo, c.FromDate AS ContractStart
FROM Employees e
LEFT JOIN Departments d ON d.Id = e.DepartmentId
LEFT JOIN Branches b ON b.Id = e.BranchId
LEFT JOIN Employees m ON m.Id = e.DirectManagerId
LEFT JOIN Positions p ON p.Id = e.PositionId
LEFT JOIN EmployeeCompensations ec ON ec.EmployeeId = e.Id
OUTER APPLY (
    SELECT TOP 1 ContractNo, FromDate
    FROM EmployeeContracts
    WHERE EmployeeId = e.Id AND ISNULL(IsDeleted, 0) = 0
    ORDER BY IsCurrent DESC, FromDate DESC, Id DESC
) c
WHERE e.Id = @EmployeeId AND ISNULL(e.IsDeleted, 0) = 0;
""",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId),
            reader =>
            {
                var positionName = HrmsDatabase.GetString(reader, "PositionName");
                if (positionName.Length == 0) positionName = HrmsDatabase.GetString(reader, "Position");

                var firstEn = HrmsDatabase.GetString(reader, "FirstNameEn");
                var lastEn = HrmsDatabase.GetString(reader, "LastNameEn");
                var fullEn = string.Join(' ', new[] { firstEn, lastEn }.Where(part => part.Length > 0));

                return new DocumentTokenEngine.EmployeeExtras(
                    HrmsDatabase.GetString(reader, "FullName"),
                    fullEn,
                    HrmsDatabase.GetString(reader, "FirstName"),
                    HrmsDatabase.GetString(reader, "LastName"),
                    HrmsDatabase.GetString(reader, "NationalId"),
                    HrmsDatabase.GetString(reader, "PassportNo"),
                    HrmsDatabase.GetString(reader, "Phone"),
                    HrmsDatabase.GetString(reader, "Email"),
                    positionName,
                    HrmsDatabase.GetString(reader, "DepartmentName"),
                    HrmsDatabase.GetString(reader, "BranchName"),
                    HrmsDatabase.GetString(reader, "ManagerName"),
                    HrmsDatabase.GetString(reader, "ContractNo"),
                    HrmsDatabase.GetDateOnly(reader, "ContractStart"),
                    HrmsDatabase.GetString(reader, "BankName"));
            });

        return rows.FirstOrDefault();
    }

    // ── التوليد ────────────────────────────────────────────────────────────────

    /// <summary>
    /// يولّد وثيقةً لموظف ويؤرشفها. يُرجع المعرّف والرموز غير المحلولة.
    /// <paramref name="persist"/> = false يعطي **معاينة** بلا كتابة — والمعاينة
    /// قبل الاعتماد هي ما يمنع إصدار مئة شهادة بحقلٍ فارغ.
    /// </summary>
    public static async Task<(int Id, string Html, IReadOnlyList<string> Unresolved)> GenerateAsync(
        ApplicationDbContext db,
        Template template,
        int employeeId,
        string? issuedBy,
        string? notes,
        DateOnly issuedOn,
        bool persist,
        string source = "مباشر")
    {
        var rows = await HrConditionFacts.LoadAsync(db, employeeId);
        var extras = await LoadExtrasAsync(db, employeeId);

        if (rows.Count == 0 || extras is null)
        {
            return (0, string.Empty, new[] { "الموظف غير موجود" });
        }

        var reference = persist
            ? await NextReferenceAsync(db, template.RefPrefix, issuedOn.Year)
            : $"{(string.IsNullOrWhiteSpace(template.RefPrefix) ? "DOC" : template.RefPrefix)}{issuedOn.Year % 100:00}-معاينة";

        var context = new DocumentTokenEngine.DocumentContext(
            reference, issuedOn, issuedBy, await CompanyNameAsync(db, rows[0].CompanyId), extras.BranchName, null);

        var tokens = DocumentTokenEngine.Build(rows[0], extras, context, issuedOn);
        var render = DocumentTokenEngine.Render(template.Body, tokens);

        if (!persist)
        {
            return (0, render.Html, render.UnresolvedTokens);
        }

        var id = await HrmsDatabase.ScalarAsync<int>(
            db,
            """
INSERT INTO GeneratedDocuments
    (ReferenceNo, TemplateId, TemplateName, EmployeeId, BodyHtml, UnresolvedTokens,
     IssuedOn, IssuedBy, Source, Notes)
OUTPUT INSERTED.Id
VALUES (@Ref, @TemplateId, @TemplateName, @EmployeeId, @Body, @Unresolved,
        @IssuedOn, @IssuedBy, @Source, @Notes);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Ref", reference);
                HrmsDatabase.AddParameter(command, "@TemplateId", template.Id);
                HrmsDatabase.AddParameter(command, "@TemplateName", template.Name);
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                HrmsDatabase.AddParameter(command, "@Body", render.Html);
                HrmsDatabase.AddParameter(command, "@Unresolved",
                    render.UnresolvedTokens.Count == 0 ? null : string.Join(", ", render.UnresolvedTokens));
                HrmsDatabase.AddParameter(command, "@IssuedOn", issuedOn.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@IssuedBy", issuedBy);
                HrmsDatabase.AddParameter(command, "@Source", source);
                HrmsDatabase.AddParameter(command, "@Notes", notes);
            });

        return (id, render.Html, render.UnresolvedTokens);
    }

    /// <summary>ترقيم سنوي متسلسل — يُعاد من واحد كل سنة فيبقى الرقم قصيراً.</summary>
    private static async Task<string> NextReferenceAsync(ApplicationDbContext db, string? prefix, int year)
    {
        var count = await HrmsDatabase.ScalarAsync<int>(
            db,
            "SELECT COUNT(*) FROM GeneratedDocuments WHERE YEAR(IssuedOn) = @Year;",
            command => HrmsDatabase.AddParameter(command, "@Year", year));

        return ViolationConfigPolicy.ReferenceNumber(
            string.IsNullOrWhiteSpace(prefix) ? "DOC" : prefix, year, count + 1);
    }

    private static async Task<string?> CompanyNameAsync(ApplicationDbContext db, int? companyId) =>
        companyId is null
            ? null
            : await HrmsDatabase.ScalarAsync<string>(
                db,
                "SELECT TOP 1 Name FROM Companies WHERE Id = @Id;",
                command => HrmsDatabase.AddParameter(command, "@Id", companyId));

    // ── الأرشيف ────────────────────────────────────────────────────────────────

    public static async Task<List<Generated>> LoadGeneratedAsync(
        ApplicationDbContext db, int? employeeId = null, int? templateId = null)
    {
        var filters = new List<string>();
        if (employeeId is not null) filters.Add("g.EmployeeId = @EmployeeId");
        if (templateId is not null) filters.Add("g.TemplateId = @TemplateId");
        var where = filters.Count == 0 ? string.Empty : " AND " + string.Join(" AND ", filters);

        return await HrmsDatabase.QueryAsync(
            db,
            $"""
SELECT g.Id, g.ReferenceNo, g.TemplateId, g.TemplateName, g.EmployeeId,
       ISNULL(e.FullName, N'') AS EmployeeName, ISNULL(e.EmployeeNo, N'') AS EmployeeNo,
       g.BodyHtml, g.UnresolvedTokens, g.IssuedOn, g.IssuedBy, g.Source, g.Notes
FROM GeneratedDocuments g
LEFT JOIN Employees e ON e.Id = g.EmployeeId
WHERE ISNULL(g.IsDeleted, 0) = 0{where}
ORDER BY g.Id DESC;
""",
            command =>
            {
                if (employeeId is not null) HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                if (templateId is not null) HrmsDatabase.AddParameter(command, "@TemplateId", templateId);
            },
            reader => new Generated(
                HrmsDatabase.GetInt(reader, "Id"),
                HrmsDatabase.GetString(reader, "ReferenceNo"),
                HrmsDatabase.GetInt(reader, "TemplateId"),
                HrmsDatabase.GetString(reader, "TemplateName"),
                HrmsDatabase.GetInt(reader, "EmployeeId"),
                HrmsDatabase.GetString(reader, "EmployeeName"),
                HrmsDatabase.GetString(reader, "EmployeeNo"),
                HrmsDatabase.GetString(reader, "BodyHtml"),
                HrmsDatabase.GetString(reader, "UnresolvedTokens"),
                HrmsDatabase.GetDateOnly(reader, "IssuedOn") ?? default,
                HrmsDatabase.GetString(reader, "IssuedBy"),
                HrmsDatabase.GetString(reader, "Source"),
                HrmsDatabase.GetString(reader, "Notes")));
    }

    public static async Task<Generated?> FindGeneratedAsync(ApplicationDbContext db, int id) =>
        (await LoadGeneratedAsync(db)).FirstOrDefault(row => row.Id == id);

    public static async Task DeleteGeneratedAsync(ApplicationDbContext db, int id) =>
        await HrmsDatabase.ExecuteAsync(
            db,
            "UPDATE GeneratedDocuments SET IsDeleted = 1 WHERE Id = @Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", id));
}
