using System.Data.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.EmployeeProfileSettings;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    private static readonly ProfileSectionDefinition[] FixedSections =
    {
        new("basic", "\u0627\u0644\u0628\u064A\u0627\u0646\u0627\u062A \u0627\u0644\u0623\u0633\u0627\u0633\u064A\u0629", "Basic Data"),
        new("personal", "\u0627\u0644\u0645\u0639\u0644\u0648\u0645\u0627\u062A \u0627\u0644\u0634\u062E\u0635\u064A\u0629", "Personal Information"),
        new("job", "\u0627\u0644\u0645\u0639\u0644\u0648\u0645\u0627\u062A \u0627\u0644\u0648\u0638\u064A\u0641\u064A\u0629", "Job Information"),
        new("financial", "\u0627\u0644\u0645\u0639\u0644\u0648\u0645\u0627\u062A \u0627\u0644\u0645\u0627\u0644\u064A\u0629", "Financial Information"),
        new("additional", "\u0645\u0639\u0644\u0648\u0645\u0627\u062A \u0625\u0636\u0627\u0641\u064A\u0629", "Additional Information")
    };

    public IndexModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public List<ProfileSectionView> Sections { get; private set; } = new();

    public int TotalFields => Sections.Sum(section => section.Fields.Count);

    [BindProperty]
    public NewFieldInput Field { get; set; } = new();

    public async Task OnGetAsync()
    {
        await EnsureSchemaAsync();
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostAddFieldAsync()
    {
        await EnsureSchemaAsync();

        Field.FieldLabel = (Field.FieldLabel ?? string.Empty).Trim();
        Field.SectionKey = (Field.SectionKey ?? string.Empty).Trim();
        Field.FieldType = NormalizeFieldType(Field.FieldType);

        if (!FixedSections.Any(section => section.Key.Equals(Field.SectionKey, StringComparison.OrdinalIgnoreCase)))
        {
            TempData["ProfileSettingsError"] = "\u0627\u0644\u0642\u0633\u0645 \u063A\u064A\u0631 \u0635\u062D\u064A\u062D.";
            return RedirectToPage();
        }

        if (string.IsNullOrWhiteSpace(Field.FieldLabel))
        {
            TempData["ProfileSettingsError"] = "\u0627\u0633\u0645 \u0627\u0644\u062D\u0642\u0644 \u0645\u0637\u0644\u0648\u0628.";
            return RedirectToPage();
        }

        var fieldKey = GenerateFieldKey(Field.SectionKey);

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
INSERT INTO EmployeeProfileFieldDefinitions
(
    SectionKey,
    FieldKey,
    FieldLabel,
    FieldType,
    IsRequired,
    IsActive,
    SortOrder,
    CreatedAt,
    UpdatedAt
)
VALUES
(
    @SectionKey,
    @FieldKey,
    @FieldLabel,
    @FieldType,
    @IsRequired,
    1,
    @SortOrder,
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@SectionKey", Field.SectionKey);
                HrmsDatabase.AddParameter(command, "@FieldKey", fieldKey);
                HrmsDatabase.AddParameter(command, "@FieldLabel", Field.FieldLabel);
                HrmsDatabase.AddParameter(command, "@FieldType", Field.FieldType);
                HrmsDatabase.AddParameter(command, "@IsRequired", Field.IsRequired ? 1 : 0);
                HrmsDatabase.AddParameter(command, "@SortOrder", Field.SortOrder);
            });

        TempData["ProfileSettingsMessage"] = "\u062A\u0645\u062A \u0625\u0636\u0627\u0641\u0629 \u0627\u0644\u062D\u0642\u0644 \u0628\u0646\u062C\u0627\u062D.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleFieldAsync(int id)
    {
        await EnsureSchemaAsync();

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
UPDATE EmployeeProfileFieldDefinitions
SET IsActive = CASE WHEN IsActive = 1 THEN 0 ELSE 1 END,
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @Id;
""",
            command => HrmsDatabase.AddParameter(command, "@Id", id));

        TempData["ProfileSettingsMessage"] = "\u062A\u0645 \u062A\u062D\u062F\u064A\u062B \u062D\u0627\u0644\u0629 \u0627\u0644\u062D\u0642\u0644.";
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        var fields = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT
    Id,
    SectionKey,
    FieldKey,
    FieldLabel,
    FieldType,
    IsRequired,
    IsActive,
    SortOrder
FROM EmployeeProfileFieldDefinitions
ORDER BY SectionKey, SortOrder, Id;
""",
            command => { },
            reader => new ProfileFieldView
            {
                Id = GetInt(reader, "Id"),
                SectionKey = GetString(reader, "SectionKey"),
                FieldKey = GetString(reader, "FieldKey"),
                FieldLabel = GetString(reader, "FieldLabel"),
                FieldType = NormalizeFieldType(GetString(reader, "FieldType")),
                IsRequired = GetBool(reader, "IsRequired"),
                IsActive = GetBool(reader, "IsActive"),
                SortOrder = GetInt(reader, "SortOrder")
            });

        var grouped = fields
            .GroupBy(field => field.SectionKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.OrderBy(field => field.SortOrder).ThenBy(field => field.Id).ToList(), StringComparer.OrdinalIgnoreCase);

        Sections = FixedSections
            .Select(section => new ProfileSectionView
            {
                Key = section.Key,
                Label = section.Label,
                Description = section.Description,
                Fields = grouped.TryGetValue(section.Key, out var sectionFields) ? sectionFields : new List<ProfileFieldView>()
            })
            .ToList();
    }

    private async Task EnsureSchemaAsync()
    {
        await HrmsDatabase.ExecuteAsync(
            _dbContext,
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
""");
    }

    private static string GenerateFieldKey(string sectionKey)
    {
        var safeSection = FixedSections.Any(section => section.Key.Equals(sectionKey, StringComparison.OrdinalIgnoreCase))
            ? sectionKey.ToLowerInvariant()
            : "additional";

        return $"{safeSection}_field_{DateTime.UtcNow:yyyyMMddHHmmssfff}";
    }

    private static string NormalizeFieldType(string? value)
    {
        var normalized = (value ?? "text").Trim().ToLowerInvariant();

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

    private sealed record ProfileSectionDefinition(string Key, string Label, string Description);

    public sealed class NewFieldInput
    {
        public string SectionKey { get; set; } = "basic";
        public string FieldLabel { get; set; } = string.Empty;
        public string FieldType { get; set; } = "text";
        public bool IsRequired { get; set; }
        public int SortOrder { get; set; }
    }

    public sealed class ProfileSectionView
    {
        public string Key { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<ProfileFieldView> Fields { get; set; } = new();
    }

    public sealed class ProfileFieldView
    {
        public int Id { get; set; }
        public string SectionKey { get; set; } = string.Empty;
        public string FieldKey { get; set; } = string.Empty;
        public string FieldLabel { get; set; } = string.Empty;
        public string FieldType { get; set; } = "text";
        public bool IsRequired { get; set; }
        public bool IsActive { get; set; }
        public int SortOrder { get; set; }

        public string FieldTypeLabel => FieldType switch
        {
            "number" => "\u0631\u0642\u0645",
            "date" => "\u062A\u0627\u0631\u064A\u062E",
            "textarea" => "\u0646\u0635 \u0637\u0648\u064A\u0644",
            _ => "\u0646\u0635"
        };
    }
}