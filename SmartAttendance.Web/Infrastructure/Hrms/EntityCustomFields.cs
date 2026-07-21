using Microsoft.AspNetCore.Http;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// الداينمك مرحلة 2 (نمط كيان «الحقول الإضافية» — 19 كياناً قابلاً للتخصيص):
/// حقول مخصصة يعرّفها الأدمن لأي كيان فرعي بملف الموظف (معالون/عقود/علاوات/
/// بطاقات الملفات التسع). التعريف بجدول HrEntityFieldDefs والقيم لكل سجل
/// بجدول HrEntityFieldValues. أسماء المدخلات بالنماذج: cf_&lt;EntityKey&gt;_&lt;FieldKey&gt;.
/// أنواع الحقول نفسها المدعومة بباني حقول الموظف (text/number/date/textarea/select/checkbox).
/// </summary>
public static class EntityCustomFields
{
    public sealed record EntityDef(string Key, string Label);

    /// <summary>الكيانات القابلة للتخصيص — مفاتيح بطاقات الملفات تطابق أسماء EmployeeRecordType.</summary>
    public static readonly IReadOnlyList<EntityDef> Entities = new List<EntityDef>
    {
        new("Dependent",        "العائلة والمعالون"),
        new("Contract",         "العقود"),
        new("Allowance",        "العلاوات"),
        new("Education",        "التعليم"),
        new("Experience",       "الخبرات"),
        new("Certificate",      "الشهادات"),
        new("Training",         "الدورات التدريبية"),
        new("Medical",          "الطبية"),
        new("Asset",            "العهد"),
        new("Address",          "العنوان"),
        new("EmergencyContact", "جهات الطوارئ"),
        new("Residency",        "الإقامة"),
    };

    public sealed class FieldDefinition
    {
        public int Id { get; set; }
        public string EntityKey { get; set; } = string.Empty;
        public string FieldKey { get; set; } = string.Empty;
        public string FieldLabel { get; set; } = string.Empty;
        public string FieldType { get; set; } = "text";
        public string FieldOptions { get; set; } = string.Empty;
        public bool IsRequired { get; set; }
        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; }

        public string InputName => $"cf_{EntityKey}_{FieldKey}";
        public bool IsSelect => FieldType == "select";
        public bool IsCheckbox => FieldType == "checkbox";
        public bool IsTextArea => FieldType == "textarea";
        public string InputType => FieldType switch { "number" => "number", "date" => "date", _ => "text" };
        public List<string> Options => FieldOptions
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct()
            .ToList();
    }

    public static async Task EnsureAsync(ApplicationDbContext dbContext)
    {
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
IF OBJECT_ID('HrEntityFieldDefs', 'U') IS NULL
BEGIN
    CREATE TABLE HrEntityFieldDefs
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EntityKey nvarchar(60) NOT NULL,
        FieldKey nvarchar(120) NOT NULL,
        FieldLabel nvarchar(150) NOT NULL,
        FieldType nvarchar(40) NOT NULL DEFAULT(N'text'),
        FieldOptions nvarchar(max) NULL,
        IsRequired bit NOT NULL DEFAULT(0),
        IsActive bit NOT NULL DEFAULT(1),
        SortOrder int NOT NULL DEFAULT(0),
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
    CREATE UNIQUE INDEX UX_HrEntityFieldDefs_Entity_Field ON HrEntityFieldDefs (EntityKey, FieldKey);
END;

IF OBJECT_ID('HrEntityFieldValues', 'U') IS NULL
BEGIN
    CREATE TABLE HrEntityFieldValues
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EntityKey nvarchar(60) NOT NULL,
        RecordId int NOT NULL,
        FieldKey nvarchar(120) NOT NULL,
        FieldValue nvarchar(max) NULL,
        UpdatedAt datetime2 NULL
    );
    CREATE UNIQUE INDEX UX_HrEntityFieldValues_Entity_Record_Field ON HrEntityFieldValues (EntityKey, RecordId, FieldKey);
END;
""");
    }

    /// <summary>كل التعريفات الفعالة مجمّعة بالكيان (للملف 360° والباني).</summary>
    public static async Task<Dictionary<string, List<FieldDefinition>>> DefinitionsByEntityAsync(
        ApplicationDbContext dbContext, bool activeOnly = true)
    {
        await EnsureAsync(dbContext);
        var rows = await HrmsDatabase.QueryAsync(
            dbContext,
            $"SELECT * FROM HrEntityFieldDefs {(activeOnly ? "WHERE IsActive = 1" : "")} ORDER BY SortOrder, Id;",
            command => { },
            ReadDefinition);

        return rows
            .GroupBy(r => r.EntityKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
    }

    public static async Task SaveDefinitionAsync(ApplicationDbContext dbContext, FieldDefinition definition)
    {
        await EnsureAsync(dbContext);

        if (definition.Id > 0)
        {
            await HrmsDatabase.ExecuteAsync(
                dbContext,
                """
UPDATE HrEntityFieldDefs
SET FieldLabel = @Label, FieldType = @Type, FieldOptions = @Options,
    IsRequired = @Required, IsActive = @Active
WHERE Id = @Id;
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Id", definition.Id);
                    AddDefinitionParameters(command, definition);
                });
            return;
        }

        // مفتاح الحقل يتولّد من التسمية إن لم يُعطَ (فريد ضمن الكيان).
        var fieldKey = string.IsNullOrWhiteSpace(definition.FieldKey)
            ? "f" + Guid.NewGuid().ToString("N")[..10]
            : definition.FieldKey.Trim();

        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
INSERT INTO HrEntityFieldDefs (EntityKey, FieldKey, FieldLabel, FieldType, FieldOptions, IsRequired, IsActive, SortOrder)
VALUES (@EntityKey, @FieldKey, @Label, @Type, @Options, @Required, @Active,
        (SELECT ISNULL(MAX(SortOrder), 0) + 1 FROM HrEntityFieldDefs WHERE EntityKey = @EntityKey));
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EntityKey", definition.EntityKey);
                HrmsDatabase.AddParameter(command, "@FieldKey", fieldKey);
                AddDefinitionParameters(command, definition);
            });
    }

    public static async Task DeleteDefinitionAsync(ApplicationDbContext dbContext, int id)
    {
        await EnsureAsync(dbContext);
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
DELETE FROM HrEntityFieldValues
WHERE EXISTS (SELECT 1 FROM HrEntityFieldDefs d
              WHERE d.Id = @Id AND d.EntityKey = HrEntityFieldValues.EntityKey
                AND d.FieldKey = HrEntityFieldValues.FieldKey);
DELETE FROM HrEntityFieldDefs WHERE Id = @Id;
""",
            command => HrmsDatabase.AddParameter(command, "@Id", id));
    }

    /// <summary>إعادة ترتيب حقول كيان واحد حسب تسلسل المعرّفات (سحب وإفلات بالباني).</summary>
    public static async Task ReorderAsync(ApplicationDbContext dbContext, string entityKey, IReadOnlyList<int> orderedIds)
    {
        await EnsureAsync(dbContext);
        for (var i = 0; i < orderedIds.Count; i++)
        {
            var order = i + 1;
            var id = orderedIds[i];
            await HrmsDatabase.ExecuteAsync(
                dbContext,
                "UPDATE HrEntityFieldDefs SET SortOrder = @Order WHERE Id = @Id AND EntityKey = @Entity;",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Order", order);
                    HrmsDatabase.AddParameter(command, "@Id", id);
                    HrmsDatabase.AddParameter(command, "@Entity", entityKey);
                });
        }
    }

    /// <summary>قيم كل سجلات موظفٍ ما دفعة واحدة: entityKey → recordId → fieldKey → value.</summary>
    public static async Task<Dictionary<string, Dictionary<int, Dictionary<string, string>>>> ValuesByEntityAsync(
        ApplicationDbContext dbContext, IReadOnlyDictionary<string, IReadOnlyList<int>> recordIdsByEntity)
    {
        await EnsureAsync(dbContext);
        var result = new Dictionary<string, Dictionary<int, Dictionary<string, string>>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (entityKey, ids) in recordIdsByEntity)
        {
            if (ids.Count == 0) continue;
            var idCsv = string.Join(',', ids.Where(i => i > 0)); // أرقام فقط — آمنة للتضمين
            if (idCsv.Length == 0) continue;

            var rows = await HrmsDatabase.QueryAsync(
                dbContext,
                $"SELECT RecordId, FieldKey, ISNULL(FieldValue, N'') AS FieldValue FROM HrEntityFieldValues WHERE EntityKey = @Entity AND RecordId IN ({idCsv});",
                command => HrmsDatabase.AddParameter(command, "@Entity", entityKey),
                reader => new
                {
                    RecordId = HrmsDatabase.GetInt(reader, "RecordId"),
                    FieldKey = HrmsDatabase.GetString(reader, "FieldKey"),
                    FieldValue = HrmsDatabase.GetString(reader, "FieldValue")
                });

            result[entityKey] = rows
                .GroupBy(r => r.RecordId)
                .ToDictionary(g => g.Key, g => g.ToDictionary(r => r.FieldKey, r => r.FieldValue, StringComparer.Ordinal));
        }
        return result;
    }

    /// <summary>حفظ قيم سجل واحد من النموذج (المدخلات cf_&lt;entity&gt;_&lt;field&gt;) — upsert لكل حقل فعال.</summary>
    public static async Task SaveValuesFromFormAsync(
        ApplicationDbContext dbContext, string entityKey, int recordId, IFormCollection form)
    {
        if (recordId <= 0) return;
        var definitions = await DefinitionsByEntityAsync(dbContext);
        if (!definitions.TryGetValue(entityKey, out var fields) || fields.Count == 0) return;

        foreach (var field in fields)
        {
            var hasValue = form.TryGetValue(field.InputName, out var raw);
            // checkbox غير المؤشر لا يُرسل — نعتبره مسحاً؛ غيره غير المرسل نتجاوزه.
            if (!hasValue && !field.IsCheckbox) continue;

            var value = field.IsCheckbox
                ? (hasValue && raw.ToString().Contains("true", StringComparison.OrdinalIgnoreCase) ? "true" : "")
                : raw.ToString();

            await HrmsDatabase.ExecuteAsync(
                dbContext,
                """
IF EXISTS (SELECT 1 FROM HrEntityFieldValues WHERE EntityKey = @Entity AND RecordId = @RecordId AND FieldKey = @FieldKey)
    UPDATE HrEntityFieldValues SET FieldValue = @Value, UpdatedAt = SYSUTCDATETIME()
    WHERE EntityKey = @Entity AND RecordId = @RecordId AND FieldKey = @FieldKey;
ELSE
    INSERT INTO HrEntityFieldValues (EntityKey, RecordId, FieldKey, FieldValue, UpdatedAt)
    VALUES (@Entity, @RecordId, @FieldKey, @Value, SYSUTCDATETIME());
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Entity", entityKey);
                    HrmsDatabase.AddParameter(command, "@RecordId", recordId);
                    HrmsDatabase.AddParameter(command, "@FieldKey", field.FieldKey);
                    HrmsDatabase.AddParameter(command, "@Value", value ?? string.Empty);
                });
        }
    }

    public static async Task DeleteValuesAsync(ApplicationDbContext dbContext, string entityKey, int recordId)
    {
        await EnsureAsync(dbContext);
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            "DELETE FROM HrEntityFieldValues WHERE EntityKey = @Entity AND RecordId = @RecordId;",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Entity", entityKey);
                HrmsDatabase.AddParameter(command, "@RecordId", recordId);
            });
    }

    private static FieldDefinition ReadDefinition(System.Data.Common.DbDataReader reader) => new()
    {
        Id = HrmsDatabase.GetInt(reader, "Id"),
        EntityKey = HrmsDatabase.GetString(reader, "EntityKey"),
        FieldKey = HrmsDatabase.GetString(reader, "FieldKey"),
        FieldLabel = HrmsDatabase.GetString(reader, "FieldLabel"),
        FieldType = EmployeeProfileDynamicFields.NormalizeFieldType(HrmsDatabase.GetString(reader, "FieldType")),
        FieldOptions = HrmsDatabase.GetString(reader, "FieldOptions"),
        IsRequired = HrmsDatabase.GetBool(reader, "IsRequired"),
        IsActive = HrmsDatabase.GetBool(reader, "IsActive"),
        SortOrder = HrmsDatabase.GetInt(reader, "SortOrder")
    };

    private static void AddDefinitionParameters(System.Data.Common.DbCommand command, FieldDefinition definition)
    {
        HrmsDatabase.AddParameter(command, "@Label", definition.FieldLabel);
        HrmsDatabase.AddParameter(command, "@Type", EmployeeProfileDynamicFields.NormalizeFieldType(definition.FieldType));
        HrmsDatabase.AddParameter(command, "@Options", string.IsNullOrWhiteSpace(definition.FieldOptions) ? DBNull.Value : definition.FieldOptions);
        HrmsDatabase.AddParameter(command, "@Required", definition.IsRequired ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@Active", definition.IsActive ? 1 : 0);
    }
}
