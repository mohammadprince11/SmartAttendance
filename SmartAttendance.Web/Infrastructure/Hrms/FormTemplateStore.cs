using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// تخزين قوالب النماذج ومجموعاتها وحقولها. المنطق كلّه بـ<see cref="FormBuilder"/>.
///
/// ⭐ جدول واحد للقوالب بكل الأنواع (طلب · استبيان · مقابلة · تدريب) — لأن البنية
/// واحدة كما أثبت الفحص الحيّ لكيان، و<c>FormType</c> يفصل الاستعمال لا المخزن.
/// </summary>
public static class FormTemplateStore
{
    public sealed record Template(
        int Id,
        string Name,
        string? NameEn,
        string? Description,
        string FormType,
        string ConditionsJson,
        bool AllowEmployeeRequest,
        bool AllowManagerRequest,
        bool IsActive)
    {
        public HrConditions.ConditionSet Conditions => HrConditions.Deserialize(ConditionsJson);
        public string TypeLabel => FormBuilder.FormTypeLabel(FormType);
        public bool IsSurvey => FormBuilder.IsSurveyLike(FormType);
    }

    public sealed record Group(int Id, int TemplateId, string Name, string? NameEn, int SortOrder);

    public sealed record Field(
        int Id,
        int TemplateId,
        int? GroupId,
        string Label,
        string? LabelEn,
        string ControlType,
        string? Options,
        int? Scale,
        bool IsRequired,
        int SortOrder)
    {
        public FormBuilder.ControlType? Control => FormBuilder.FindControl(ControlType);
        public string ControlLabel => FormBuilder.ControlLabel(ControlType);
        public List<string> OptionList => FormBuilder.ParseOptions(Options);
        public int EffectiveScale => FormBuilder.NormalizeScale(Scale);
        /// <summary>حقل خارج أي مجموعة — «الحقول غير المجمعة» بمصطلح كيان.</summary>
        public bool IsUngrouped => GroupId is null;
    }

    // ── القوالب ────────────────────────────────────────────────────────────────

    public static async Task<List<Template>> LoadTemplatesAsync(
        ApplicationDbContext db, string? formType = null, bool activeOnly = false)
    {
        var filters = new List<string>();
        if (formType is not null) filters.Add("FormType = @FormType");
        if (activeOnly) filters.Add("IsActive = 1");
        var where = filters.Count == 0 ? string.Empty : " AND " + string.Join(" AND ", filters);

        return await HrmsDatabase.QueryAsync(
            db,
            $"""
SELECT Id, Name, NameEn, Description, FormType, ISNULL(ConditionsJson, N'') AS ConditionsJson,
       AllowEmployeeRequest, AllowManagerRequest, IsActive
FROM FormTemplates
WHERE ISNULL(IsDeleted, 0) = 0{where}
ORDER BY FormType, Name;
""",
            command =>
            {
                if (formType is not null) HrmsDatabase.AddParameter(command, "@FormType", formType);
            },
            reader => new Template(
                HrmsDatabase.GetInt(reader, "Id"),
                HrmsDatabase.GetString(reader, "Name"),
                HrmsDatabase.GetString(reader, "NameEn"),
                HrmsDatabase.GetString(reader, "Description"),
                HrmsDatabase.GetString(reader, "FormType"),
                HrmsDatabase.GetString(reader, "ConditionsJson"),
                HrmsDatabase.GetBool(reader, "AllowEmployeeRequest"),
                HrmsDatabase.GetBool(reader, "AllowManagerRequest"),
                HrmsDatabase.GetBool(reader, "IsActive")));
    }

    public static async Task<Template?> FindTemplateAsync(ApplicationDbContext db, int id) =>
        (await LoadTemplatesAsync(db)).FirstOrDefault(row => row.Id == id);

    public static async Task<int> SaveTemplateAsync(
        ApplicationDbContext db,
        int id,
        string name,
        string? nameEn,
        string? description,
        string formType,
        HrConditions.ConditionSet conditions,
        bool allowEmployee,
        bool allowManager,
        bool isActive,
        string? user)
    {
        if (id > 0)
        {
            await HrmsDatabase.ExecuteAsync(
                db,
                """
UPDATE FormTemplates
SET Name = @Name, NameEn = @NameEn, Description = @Description, FormType = @FormType,
    ConditionsJson = @Conditions, AllowEmployeeRequest = @AllowEmp,
    AllowManagerRequest = @AllowMgr, IsActive = @IsActive, UpdatedAt = SYSUTCDATETIME()
WHERE Id = @Id;
""",
                command => Bind(command, id, name, nameEn, description, formType, conditions, allowEmployee, allowManager, isActive, user));

            return id;
        }

        return await HrmsDatabase.ScalarAsync<int>(
            db,
            """
INSERT INTO FormTemplates
    (Name, NameEn, Description, FormType, ConditionsJson, AllowEmployeeRequest,
     AllowManagerRequest, IsActive, CreatedBy)
OUTPUT INSERTED.Id
VALUES (@Name, @NameEn, @Description, @FormType, @Conditions, @AllowEmp, @AllowMgr, @IsActive, @CreatedBy);
""",
            command => Bind(command, id, name, nameEn, description, formType, conditions, allowEmployee, allowManager, isActive, user));
    }

    private static void Bind(
        System.Data.Common.DbCommand command, int id, string name, string? nameEn, string? description,
        string formType, HrConditions.ConditionSet conditions, bool allowEmp, bool allowMgr, bool isActive, string? user)
    {
        if (id > 0) HrmsDatabase.AddParameter(command, "@Id", id);
        HrmsDatabase.AddParameter(command, "@Name", name);
        HrmsDatabase.AddParameter(command, "@NameEn", nameEn);
        HrmsDatabase.AddParameter(command, "@Description", description);
        HrmsDatabase.AddParameter(command, "@FormType", formType);
        HrmsDatabase.AddParameter(command, "@Conditions", HrConditions.Serialize(conditions));
        HrmsDatabase.AddParameter(command, "@AllowEmp", allowEmp);
        HrmsDatabase.AddParameter(command, "@AllowMgr", allowMgr);
        HrmsDatabase.AddParameter(command, "@IsActive", isActive);
        if (id <= 0) HrmsDatabase.AddParameter(command, "@CreatedBy", user);
    }

    public static async Task DeleteTemplateAsync(ApplicationDbContext db, int id) =>
        await HrmsDatabase.ExecuteAsync(
            db,
            "UPDATE FormTemplates SET IsDeleted = 1, UpdatedAt = SYSUTCDATETIME() WHERE Id = @Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", id));

    /// <summary>«انشئ نسخة» — ينسخ القالب بمجموعاته وحقوله كاملةً بمعاملة واحدة.</summary>
    public static async Task<int?> DuplicateAsync(ApplicationDbContext db, int id, string? user)
    {
        var source = await FindTemplateAsync(db, id);
        if (source is null) return null;

        await using var transaction = await db.Database.BeginTransactionAsync();

        var newId = await SaveTemplateAsync(
            db, 0, $"{source.Name} (نسخة)", source.NameEn, source.Description, source.FormType,
            source.Conditions, source.AllowEmployeeRequest, source.AllowManagerRequest, false, user);

        // خريطة المجموعات القديمة ← الجديدة، فتُعاد نسبة الحقول لمجموعاتها الصحيحة.
        var groupMap = new Dictionary<int, int>();

        foreach (var group in await LoadGroupsAsync(db, id))
        {
            groupMap[group.Id] = await SaveGroupAsync(db, 0, newId, group.Name, group.NameEn, group.SortOrder);
        }

        foreach (var field in await LoadFieldsAsync(db, id))
        {
            var target = field.GroupId is { } oldGroup && groupMap.TryGetValue(oldGroup, out var mapped) ? mapped : (int?)null;

            await SaveFieldAsync(
                db, 0, newId, target, field.Label, field.LabelEn, field.ControlType,
                field.Options, field.Scale, field.IsRequired, field.SortOrder);
        }

        await transaction.CommitAsync();
        return newId;
    }

    // ── المجموعات ──────────────────────────────────────────────────────────────

    public static async Task<List<Group>> LoadGroupsAsync(ApplicationDbContext db, int templateId) =>
        await HrmsDatabase.QueryAsync(
            db,
            "SELECT Id, TemplateId, Name, NameEn, SortOrder FROM FormGroups WHERE TemplateId = @Id ORDER BY SortOrder, Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", templateId),
            reader => new Group(
                HrmsDatabase.GetInt(reader, "Id"),
                HrmsDatabase.GetInt(reader, "TemplateId"),
                HrmsDatabase.GetString(reader, "Name"),
                HrmsDatabase.GetString(reader, "NameEn"),
                HrmsDatabase.GetInt(reader, "SortOrder")));

    public static async Task<int> SaveGroupAsync(
        ApplicationDbContext db, int id, int templateId, string name, string? nameEn, int sortOrder)
    {
        if (id > 0)
        {
            await HrmsDatabase.ExecuteAsync(
                db,
                "UPDATE FormGroups SET Name = @Name, NameEn = @NameEn, SortOrder = @Sort WHERE Id = @Id;",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Id", id);
                    HrmsDatabase.AddParameter(command, "@Name", name);
                    HrmsDatabase.AddParameter(command, "@NameEn", nameEn);
                    HrmsDatabase.AddParameter(command, "@Sort", sortOrder);
                });

            return id;
        }

        return await HrmsDatabase.ScalarAsync<int>(
            db,
            """
INSERT INTO FormGroups (TemplateId, Name, NameEn, SortOrder)
OUTPUT INSERTED.Id VALUES (@TemplateId, @Name, @NameEn, @Sort);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@TemplateId", templateId);
                HrmsDatabase.AddParameter(command, "@Name", name);
                HrmsDatabase.AddParameter(command, "@NameEn", nameEn);
                HrmsDatabase.AddParameter(command, "@Sort", sortOrder);
            });
    }

    /// <summary>
    /// حذف المجموعة **لا يحذف حقولها** — تصير غير مجمَّعة وتبقى بالنموذج.
    /// حذفٌ متتالٍ هنا كان يمحو عشرة أسئلة بضغطة على عنوان مجموعة.
    /// </summary>
    public static async Task DeleteGroupAsync(ApplicationDbContext db, int id) =>
        await HrmsDatabase.ExecuteAsync(
            db,
            """
UPDATE FormFields SET GroupId = NULL WHERE GroupId = @Id;
DELETE FROM FormGroups WHERE Id = @Id;
""",
            command => HrmsDatabase.AddParameter(command, "@Id", id));

    // ── الحقول ─────────────────────────────────────────────────────────────────

    public static async Task<List<Field>> LoadFieldsAsync(ApplicationDbContext db, int templateId) =>
        await HrmsDatabase.QueryAsync(
            db,
            """
SELECT Id, TemplateId, GroupId, Label, LabelEn, ControlType, Options, Scale, IsRequired, SortOrder
FROM FormFields WHERE TemplateId = @Id ORDER BY SortOrder, Id;
""",
            command => HrmsDatabase.AddParameter(command, "@Id", templateId),
            reader => new Field(
                HrmsDatabase.GetInt(reader, "Id"),
                HrmsDatabase.GetInt(reader, "TemplateId"),
                HrmsDatabase.GetNullableInt(reader, "GroupId"),
                HrmsDatabase.GetString(reader, "Label"),
                HrmsDatabase.GetString(reader, "LabelEn"),
                HrmsDatabase.GetString(reader, "ControlType"),
                HrmsDatabase.GetString(reader, "Options"),
                HrmsDatabase.GetNullableInt(reader, "Scale"),
                HrmsDatabase.GetBool(reader, "IsRequired"),
                HrmsDatabase.GetInt(reader, "SortOrder")));

    public static async Task<int> SaveFieldAsync(
        ApplicationDbContext db, int id, int templateId, int? groupId, string label, string? labelEn,
        string controlType, string? options, int? scale, bool isRequired, int sortOrder)
    {
        // السلّم يُخزَّن لحقول التقييم وحدها — قيمةٌ على حقل نصّي تربك أي قارئ لاحق.
        var effectiveScale = FormBuilder.FindControl(controlType)?.NeedsScale == true
            ? FormBuilder.NormalizeScale(scale)
            : (int?)null;

        if (id > 0)
        {
            await HrmsDatabase.ExecuteAsync(
                db,
                """
UPDATE FormFields
SET GroupId = @GroupId, Label = @Label, LabelEn = @LabelEn, ControlType = @Control,
    Options = @Options, Scale = @Scale, IsRequired = @Required, SortOrder = @Sort
WHERE Id = @Id;
""",
                command => BindField(command, id, templateId, groupId, label, labelEn, controlType, options, effectiveScale, isRequired, sortOrder));

            return id;
        }

        return await HrmsDatabase.ScalarAsync<int>(
            db,
            """
INSERT INTO FormFields (TemplateId, GroupId, Label, LabelEn, ControlType, Options, Scale, IsRequired, SortOrder)
OUTPUT INSERTED.Id
VALUES (@TemplateId, @GroupId, @Label, @LabelEn, @Control, @Options, @Scale, @Required, @Sort);
""",
            command => BindField(command, id, templateId, groupId, label, labelEn, controlType, options, effectiveScale, isRequired, sortOrder));
    }

    private static void BindField(
        System.Data.Common.DbCommand command, int id, int templateId, int? groupId, string label,
        string? labelEn, string controlType, string? options, int? scale, bool isRequired, int sortOrder)
    {
        if (id > 0) HrmsDatabase.AddParameter(command, "@Id", id);
        else HrmsDatabase.AddParameter(command, "@TemplateId", templateId);
        HrmsDatabase.AddParameter(command, "@GroupId", groupId);
        HrmsDatabase.AddParameter(command, "@Label", label);
        HrmsDatabase.AddParameter(command, "@LabelEn", labelEn);
        HrmsDatabase.AddParameter(command, "@Control", controlType);
        HrmsDatabase.AddParameter(command, "@Options", options);
        HrmsDatabase.AddParameter(command, "@Scale", scale);
        HrmsDatabase.AddParameter(command, "@Required", isRequired);
        HrmsDatabase.AddParameter(command, "@Sort", sortOrder);
    }

    public static async Task DeleteFieldAsync(ApplicationDbContext db, int id) =>
        await HrmsDatabase.ExecuteAsync(
            db, "DELETE FROM FormFields WHERE Id = @Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", id));

    /// <summary>نقل حقل خطوةً بالترتيب — بديل السحب، ويعمل بلا JS.</summary>
    public static async Task MoveFieldAsync(ApplicationDbContext db, int templateId, int fieldId, int direction)
    {
        var fields = await LoadFieldsAsync(db, templateId);
        var ids = fields.Select(field => field.Id).ToList();
        var index = ids.IndexOf(fieldId);
        var target = index + direction;

        if (index < 0 || target < 0 || target >= ids.Count)
        {
            return;
        }

        (ids[index], ids[target]) = (ids[target], ids[index]);

        await using var transaction = await db.Database.BeginTransactionAsync();

        for (var rank = 0; rank < ids.Count; rank++)
        {
            var id = ids[rank];
            await HrmsDatabase.ExecuteAsync(
                db,
                "UPDATE FormFields SET SortOrder = @Sort WHERE Id = @Id;",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Sort", rank + 1);
                    HrmsDatabase.AddParameter(command, "@Id", id);
                });
        }

        await transaction.CommitAsync();
    }
}
