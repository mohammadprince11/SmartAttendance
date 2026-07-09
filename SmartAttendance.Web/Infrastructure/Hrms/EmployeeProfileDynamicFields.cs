using System.Data.Common;
using Microsoft.AspNetCore.Http;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

public static class EmployeeProfileDynamicFields
{
    private static readonly EmployeeProfileDynamicSectionDefinition[] FixedSections =
    {
        new("basic", "\u0627\u0644\u0628\u064A\u0627\u0646\u0627\u062A \u0627\u0644\u0623\u0633\u0627\u0633\u064A\u0629", 10),
        new("personal", "\u0627\u0644\u0645\u0639\u0644\u0648\u0645\u0627\u062A \u0627\u0644\u0634\u062E\u0635\u064A\u0629", 20),
        new("job", "\u0627\u0644\u0645\u0639\u0644\u0648\u0645\u0627\u062A \u0627\u0644\u0648\u0638\u064A\u0641\u064A\u0629", 30),
        new("financial", "\u0627\u0644\u0645\u0639\u0644\u0648\u0645\u0627\u062A \u0627\u0644\u0645\u0627\u0644\u064A\u0629", 40),
        new("additional", "\u0645\u0639\u0644\u0648\u0645\u0627\u062A \u0625\u0636\u0627\u0641\u064A\u0629", 50)
    };

    public static async Task<List<EmployeeProfileDynamicSection>> LoadSectionsAsync(ApplicationDbContext dbContext, int employeeId)
    {
        await EnsureSchemaAsync(dbContext);

        var fields = await HrmsDatabase.QueryAsync(
            dbContext,
            """
SELECT
    d.Id,
    d.SectionKey,
    d.FieldKey,
    d.FieldLabel,
    d.FieldType,
    d.IsRequired,
    d.SortOrder,
    ISNULL(v.FieldValue, '') AS FieldValue
FROM EmployeeProfileFieldDefinitions d
LEFT JOIN EmployeeCustomFields v
    ON v.EmployeeId = @EmployeeId
   AND v.FieldKey = d.FieldKey
WHERE d.IsActive = 1
ORDER BY
    CASE d.SectionKey
        WHEN 'basic' THEN 10
        WHEN 'personal' THEN 20
        WHEN 'job' THEN 30
        WHEN 'financial' THEN 40
        WHEN 'additional' THEN 50
        ELSE 99
    END,
    d.SortOrder,
    d.Id;
""",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId),
            reader => new EmployeeProfileDynamicField
            {
                Id = GetInt(reader, "Id"),
                SectionKey = GetString(reader, "SectionKey"),
                FieldKey = GetString(reader, "FieldKey"),
                FieldLabel = GetString(reader, "FieldLabel"),
                FieldType = NormalizeFieldType(GetString(reader, "FieldType")),
                IsRequired = GetBool(reader, "IsRequired"),
                SortOrder = GetInt(reader, "SortOrder"),
                FieldValue = GetString(reader, "FieldValue")
            });

        var grouped = fields
            .GroupBy(field => field.SectionKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        return FixedSections
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
SELECT FieldKey, FieldLabel
FROM EmployeeProfileFieldDefinitions
WHERE IsActive = 1
ORDER BY SortOrder, Id;
""",
            command => { },
            reader => new EmployeeProfileFieldSaveDefinition
            {
                FieldKey = GetString(reader, "FieldKey"),
                FieldLabel = GetString(reader, "FieldLabel")
            });

        foreach (var definition in definitions)
        {
            var formKey = $"ProfileCustomValues[{definition.FieldKey}]";

            if (!form.TryGetValue(formKey, out var rawValue))
            {
                continue;
            }

            var value = rawValue.ToString();

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

    public static async Task EnsureSchemaAsync(ApplicationDbContext dbContext)
    {
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
IF OBJECT_ID(N'[dbo].[EmployeeProfileFieldDefinitions]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[EmployeeProfileFieldDefinitions]
    (
        [Id] int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [SectionKey] nvarchar(80) NOT NULL,
        [FieldKey] nvarchar(120) NOT NULL,
        [FieldLabel] nvarchar(150) NOT NULL,
        [FieldType] nvarchar(40) NOT NULL CONSTRAINT DF_EmployeeProfileFieldDefinitions_FieldType DEFAULT N'text',
        [IsRequired] bit NOT NULL CONSTRAINT DF_EmployeeProfileFieldDefinitions_IsRequired DEFAULT 0,
        [IsActive] bit NOT NULL CONSTRAINT DF_EmployeeProfileFieldDefinitions_IsActive DEFAULT 1,
        [SortOrder] int NOT NULL CONSTRAINT DF_EmployeeProfileFieldDefinitions_SortOrder DEFAULT 0,
        [CreatedAt] datetime2 NOT NULL CONSTRAINT DF_EmployeeProfileFieldDefinitions_CreatedAt DEFAULT SYSUTCDATETIME(),
        [UpdatedAt] datetime2 NULL
    );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'UX_EmployeeProfileFieldDefinitions_FieldKey'
      AND object_id = OBJECT_ID(N'[dbo].[EmployeeProfileFieldDefinitions]')
)
BEGIN
    CREATE UNIQUE INDEX UX_EmployeeProfileFieldDefinitions_FieldKey
    ON [dbo].[EmployeeProfileFieldDefinitions] ([FieldKey]);
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_EmployeeProfileFieldDefinitions_Section'
      AND object_id = OBJECT_ID(N'[dbo].[EmployeeProfileFieldDefinitions]')
)
BEGIN
    CREATE INDEX IX_EmployeeProfileFieldDefinitions_Section
    ON [dbo].[EmployeeProfileFieldDefinitions] ([SectionKey], [SortOrder], [Id]);
END;

IF OBJECT_ID(N'[dbo].[EmployeeCustomFields]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[EmployeeCustomFields]
    (
        [Id] int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [EmployeeId] int NOT NULL,
        [FieldKey] nvarchar(120) NOT NULL,
        [FieldLabel] nvarchar(150) NULL,
        [FieldValue] nvarchar(max) NULL,
        [UpdatedAt] datetime2 NULL
    );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'UX_EmployeeCustomFields_Employee_Field'
      AND object_id = OBJECT_ID(N'[dbo].[EmployeeCustomFields]')
)
BEGIN
    CREATE UNIQUE INDEX UX_EmployeeCustomFields_Employee_Field
    ON [dbo].[EmployeeCustomFields] ([EmployeeId], [FieldKey]);
END;
""");
    }

    private static string NormalizeFieldType(string value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();

        return normalized switch
        {
            "number" => "number",
            "date" => "date",
            "textarea" => "textarea",
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

    private sealed record EmployeeProfileDynamicSectionDefinition(string Key, string Label, int SortOrder);

    private sealed class EmployeeProfileFieldSaveDefinition
    {
        public string FieldKey { get; set; } = string.Empty;
        public string FieldLabel { get; set; } = string.Empty;
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
    public string FieldValue { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
    public int SortOrder { get; set; }

    public bool IsTextArea => FieldType.Equals("textarea", StringComparison.OrdinalIgnoreCase);

    public string InputType => FieldType switch
    {
        "number" => "number",
        "date" => "date",
        _ => "text"
    };
}