using System.Data.Common;
using Microsoft.AspNetCore.Http;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

public static class EmployeeProfileDynamicFields
{
    public static async Task<List<EmployeeProfileDynamicSection>> LoadSectionsAsync(ApplicationDbContext dbContext, int employeeId)
    {
        await EnsureSchemaAsync(dbContext);

        var sections = await EmployeeProfileSections.LoadAsync(dbContext, activeOnly: true);

        var fields = await HrmsDatabase.QueryAsync(
            dbContext,
            """
SELECT
    d.Id,
    d.SectionKey,
    d.FieldKey,
    d.FieldLabel,
    d.FieldType,
    d.FieldOptions,
    d.IsRequired,
    d.SortOrder,
    ISNULL(v.FieldValue, '') AS FieldValue
FROM EmployeeProfileFieldDefinitions d
LEFT JOIN EmployeeCustomFields v
    ON v.EmployeeId = @EmployeeId
   AND v.FieldKey = d.FieldKey
WHERE d.IsActive = 1
ORDER BY d.SortOrder, d.Id;
""",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId),
            reader => new EmployeeProfileDynamicField
            {
                Id = GetInt(reader, "Id"),
                SectionKey = GetString(reader, "SectionKey"),
                FieldKey = GetString(reader, "FieldKey"),
                FieldLabel = GetString(reader, "FieldLabel"),
                FieldType = NormalizeFieldType(GetString(reader, "FieldType")),
                FieldOptions = GetString(reader, "FieldOptions"),
                IsRequired = GetBool(reader, "IsRequired"),
                SortOrder = GetInt(reader, "SortOrder"),
                FieldValue = GetString(reader, "FieldValue")
            });

        var grouped = fields
            .GroupBy(field => field.SectionKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        return sections
            .Select(section => new EmployeeProfileDynamicSection
            {
                Key = section.Key,
                Label = section.Label,
                SortOrder = section.SortOrder,
                Fields = grouped.TryGetValue(section.Key, out var sectionFields)
                    ? sectionFields.OrderBy(field => field.SortOrder).ThenBy(field => field.Id).ToList()
                    : new List<EmployeeProfileDynamicField>()
            })
            .ToList();
    }

    public static async Task SaveAsync(ApplicationDbContext dbContext, int employeeId, IFormCollection form)
    {
        if (employeeId <= 0)
        {
            return;
        }

        await EnsureSchemaAsync(dbContext);

        var definitions = await HrmsDatabase.QueryAsync(
            dbContext,
            """
SELECT FieldKey, FieldLabel, FieldType
FROM EmployeeProfileFieldDefinitions
WHERE IsActive = 1
ORDER BY SortOrder, Id;
""",
            command => { },
            reader => new EmployeeProfileFieldSaveDefinition
            {
                FieldKey = GetString(reader, "FieldKey"),
                FieldLabel = GetString(reader, "FieldLabel"),
                FieldType = NormalizeFieldType(GetString(reader, "FieldType"))
            });

        foreach (var definition in definitions)
        {
            var formKey = $"ProfileCustomValues[{definition.FieldKey}]";
            var isCheckbox = definition.FieldType == "checkbox";

            if (!form.TryGetValue(formKey, out var rawValue))
            {
                // An unchecked checkbox posts nothing — persist it as cleared.
                if (!isCheckbox)
                {
                    continue;
                }

                rawValue = string.Empty;
            }

            var value = isCheckbox
                ? (rawValue.ToString().Contains("true", StringComparison.OrdinalIgnoreCase) ? "true" : "")
                : rawValue.ToString();

            await HrmsDatabase.ExecuteAsync(
                dbContext,
                """
IF EXISTS
(
    SELECT 1
    FROM EmployeeCustomFields
    WHERE EmployeeId = @EmployeeId
      AND FieldKey = @FieldKey
)
BEGIN
    UPDATE EmployeeCustomFields
    SET FieldLabel = @FieldLabel,
        FieldValue = @FieldValue,
        UpdatedAt = SYSUTCDATETIME()
    WHERE EmployeeId = @EmployeeId
      AND FieldKey = @FieldKey;
END
ELSE
BEGIN
    INSERT INTO EmployeeCustomFields
    (
        EmployeeId,
        FieldKey,
        FieldLabel,
        FieldValue,
        UpdatedAt
    )
    VALUES
    (
        @EmployeeId,
        @FieldKey,
        @FieldLabel,
        @FieldValue,
        SYSUTCDATETIME()
    );
END;
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                    HrmsDatabase.AddParameter(command, "@FieldKey", definition.FieldKey);
                    HrmsDatabase.AddParameter(command, "@FieldLabel", definition.FieldLabel);
                    HrmsDatabase.AddParameter(command, "@FieldValue", value ?? string.Empty);
                });
        }
    }

    // Kept for source compatibility with import code. Schema creation moved to
    // SqlSchemaMigrator migration 20260826-20.
    public static Task EnsureSchemaAsync(ApplicationDbContext dbContext) => Task.CompletedTask;

    public static string NormalizeFieldType(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();

        return normalized switch
        {
            "number" => "number",
            "date" => "date",
            "textarea" => "textarea",
            "select" => "select",
            "checkbox" => "checkbox",
            _ => "text"
        };
    }

    private static int GetInt(DbDataReader reader, string name)
    {
        var value = reader[name];
        return value == DBNull.Value ? 0 : Convert.ToInt32(value);
    }

    private static bool GetBool(DbDataReader reader, string name)
    {
        var value = reader[name];
        return value != DBNull.Value && Convert.ToBoolean(value);
    }

    private static string GetString(DbDataReader reader, string name)
    {
        var value = reader[name];
        return value == DBNull.Value ? string.Empty : Convert.ToString(value) ?? string.Empty;
    }

    private sealed class EmployeeProfileFieldSaveDefinition
    {
        public string FieldKey { get; set; } = string.Empty;
        public string FieldLabel { get; set; } = string.Empty;
        public string FieldType { get; set; } = "text";
    }
}

public sealed class EmployeeProfileDynamicSection
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public List<EmployeeProfileDynamicField> Fields { get; set; } = new();
}

public sealed class EmployeeProfileDynamicField
{
    public int Id { get; set; }
    public string SectionKey { get; set; } = string.Empty;
    public string FieldKey { get; set; } = string.Empty;
    public string FieldLabel { get; set; } = string.Empty;
    public string FieldType { get; set; } = "text";
    public string FieldOptions { get; set; } = string.Empty;
    public string FieldValue { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
    public int SortOrder { get; set; }

    public bool IsTextArea => FieldType.Equals("textarea", StringComparison.OrdinalIgnoreCase);

    public bool IsSelect => FieldType.Equals("select", StringComparison.OrdinalIgnoreCase);

    public bool IsCheckbox => FieldType.Equals("checkbox", StringComparison.OrdinalIgnoreCase);

    public bool IsCheckedValue => FieldValue.Equals("true", StringComparison.OrdinalIgnoreCase);

    /// <summary>Dropdown options — one per line in FieldOptions.</summary>
    public List<string> Options => FieldOptions
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct()
        .ToList();

    public string InputType => FieldType switch
    {
        "number" => "number",
        "date" => "date",
        _ => "text"
    };
}
