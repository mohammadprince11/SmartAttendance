using System.Data.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.EmployeeProfileSettings;

/// <summary>
/// باني حقول ملف الموظف (الداينمك مرحلة 1): أقسام ديناميكية + حقول مخصصة
/// (6 أنواع) لكيان الموظف نفسه. الكيانات الفرعية لها باني منفصل /HrSettings/EntityFields.
/// </summary>
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    private const int FieldLabelMaxLength = 150;

    public IndexModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public List<ProfileSectionView> Sections { get; private set; } = new();

    public int TotalFields => Sections.Sum(section => section.Fields.Count);

    [BindProperty]
    public NewFieldInput Field { get; set; } = new();

    [BindProperty]
    public EditFieldInput EditField { get; set; } = new();

    [BindProperty]
    public SectionInput Section { get; set; } = new();

    public async Task OnGetAsync()
    {
        await EnsureSchemaAsync();
        await LoadAsync();
    }

    // ---------- Section management (dynamic tabs/groups) ----------

    public async Task<IActionResult> OnPostAddSectionAsync()
    {
        await EnsureSchemaAsync();

        var label = NormalizeLabel(Section.Label);
        if (!ValidateLabel(label, out var labelError))
        {
            TempData["ProfileSettingsError"] = labelError;
            return RedirectToPage();
        }

        var key = GenerateSectionKey();
        var sortOrder = Section.SortOrder > 0 ? Section.SortOrder : await GetNextSectionSortOrderAsync();

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
INSERT INTO EmployeeProfileSections (SectionKey, Label, SortOrder, IsSystem, IsActive)
VALUES (@Key, @Label, @SortOrder, 0, 1);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Key", key);
                HrmsDatabase.AddParameter(command, "@Label", label);
                HrmsDatabase.AddParameter(command, "@SortOrder", sortOrder);
            });

        TempData["ProfileSettingsMessage"] = "\u062A\u0645\u062A \u0625\u0636\u0627\u0641\u0629 \u0627\u0644\u0642\u0633\u0645.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpdateSectionAsync(int id)
    {
        await EnsureSchemaAsync();

        var label = NormalizeLabel(Section.Label);
        if (id <= 0 || !ValidateLabel(label, out var labelError))
        {
            TempData["ProfileSettingsError"] = id <= 0 ? "\u0627\u0644\u0642\u0633\u0645 \u063A\u064A\u0631 \u0635\u062D\u064A\u062D." : "\u0627\u0633\u0645 \u0627\u0644\u0642\u0633\u0645 \u0645\u0637\u0644\u0648\u0628.";
            return RedirectToPage();
        }

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
UPDATE EmployeeProfileSections
SET Label = @Label,
    SortOrder = @SortOrder,
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @Id;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", id);
                HrmsDatabase.AddParameter(command, "@Label", label);
                HrmsDatabase.AddParameter(command, "@SortOrder", Math.Max(0, Section.SortOrder));
            });

        TempData["ProfileSettingsMessage"] = "\u062A\u0645 \u062A\u0639\u062F\u064A\u0644 \u0627\u0644\u0642\u0633\u0645.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleSectionAsync(int id)
    {
        await EnsureSchemaAsync();

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
UPDATE EmployeeProfileSections
SET IsActive = CASE WHEN IsActive = 1 THEN 0 ELSE 1 END,
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @Id AND IsSystem = 0;
""",
            command => HrmsDatabase.AddParameter(command, "@Id", id));

        TempData["ProfileSettingsMessage"] = "\u062A\u0645 \u062A\u062D\u062F\u064A\u062B \u062D\u0627\u0644\u0629 \u0627\u0644\u0642\u0633\u0645.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteSectionAsync(int id)
    {
        await EnsureSchemaAsync();

        var fieldCount = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT COUNT(1) AS CountValue
FROM EmployeeProfileFieldDefinitions d
INNER JOIN EmployeeProfileSections s ON s.SectionKey = d.SectionKey
WHERE s.Id = @Id;
""",
            command => HrmsDatabase.AddParameter(command, "@Id", id),
            reader => GetInt(reader, "CountValue"));

        if (fieldCount.FirstOrDefault() > 0)
        {
            TempData["ProfileSettingsError"] = "\u0644\u0627 \u064A\u0645\u0643\u0646 \u062D\u0630\u0641 \u0642\u0633\u0645 \u064A\u062D\u062A\u0648\u064A \u0639\u0644\u0649 \u062D\u0642\u0648\u0644 \u2014 \u0627\u0646\u0642\u0644 \u0627\u0644\u062D\u0642\u0648\u0644 \u0623\u0648 \u0627\u062D\u0630\u0641\u0647\u0627 \u0623\u0648\u0644\u0627\u064B.";
            return RedirectToPage();
        }

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            "DELETE FROM EmployeeProfileSections WHERE Id = @Id AND IsSystem = 0;",
            command => HrmsDatabase.AddParameter(command, "@Id", id));

        TempData["ProfileSettingsMessage"] = "\u062A\u0645 \u062D\u0630\u0641 \u0627\u0644\u0642\u0633\u0645.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAddFieldAsync()
    {
        await EnsureSchemaAsync();

        Field.SectionKey = NormalizeSectionKey(Field.SectionKey);
        Field.FieldLabel = NormalizeLabel(Field.FieldLabel);
        Field.FieldType = NormalizeFieldType(Field.FieldType);

        if (!await IsValidSectionAsync(Field.SectionKey))
        {
            TempData["ProfileSettingsError"] = "\u0627\u0644\u0642\u0633\u0645 \u063A\u064A\u0631 \u0635\u062D\u064A\u062D.";
            return RedirectToPage();
        }

        if (!ValidateLabel(Field.FieldLabel, out var labelError))
        {
            TempData["ProfileSettingsError"] = labelError;
            return RedirectToPage();
        }

        if (await FieldLabelExistsAsync(Field.SectionKey, Field.FieldLabel, null))
        {
            TempData["ProfileSettingsError"] = "\u064A\u0648\u062C\u062F \u062D\u0642\u0644 \u0628\u0646\u0641\u0633 \u0627\u0644\u0627\u0633\u0645 \u062F\u0627\u062E\u0644 \u0646\u0641\u0633 \u0627\u0644\u0642\u0633\u0645.";
            return RedirectToPage();
        }

        var sortOrder = Field.SortOrder > 0
            ? Field.SortOrder
            : await GetNextSortOrderAsync(Field.SectionKey);

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
    FieldOptions,
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
    @FieldOptions,
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
                HrmsDatabase.AddParameter(command, "@FieldOptions", NormalizeOptions(Field.FieldType, Field.FieldOptions));
                HrmsDatabase.AddParameter(command, "@IsRequired", Field.IsRequired ? 1 : 0);
                HrmsDatabase.AddParameter(command, "@SortOrder", sortOrder);
            });

        TempData["ProfileSettingsMessage"] = "\u062A\u0645\u062A \u0625\u0636\u0627\u0641\u0629 \u0627\u0644\u062D\u0642\u0644 \u0628\u0646\u062C\u0627\u062D.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpdateFieldAsync(int id)
    {
        await EnsureSchemaAsync();

        EditField.SectionKey = NormalizeSectionKey(EditField.SectionKey);
        EditField.FieldLabel = NormalizeLabel(EditField.FieldLabel);
        EditField.FieldType = NormalizeFieldType(EditField.FieldType);
        EditField.SortOrder = Math.Max(0, EditField.SortOrder);

        if (id <= 0)
        {
            TempData["ProfileSettingsError"] = "\u0627\u0644\u062D\u0642\u0644 \u063A\u064A\u0631 \u0635\u062D\u064A\u062D.";
            return RedirectToPage();
        }

        if (!await IsValidSectionAsync(EditField.SectionKey))
        {
            TempData["ProfileSettingsError"] = "\u0627\u0644\u0642\u0633\u0645 \u063A\u064A\u0631 \u0635\u062D\u064A\u062D.";
            return RedirectToPage();
        }

        if (!await FieldExistsAsync(id))
        {
            TempData["ProfileSettingsError"] = "\u0644\u0645 \u064A\u062A\u0645 \u0627\u0644\u0639\u062B\u0648\u0631 \u0639\u0644\u0649 \u0647\u0630\u0627 \u0627\u0644\u062D\u0642\u0644.";
            return RedirectToPage();
        }

        if (!ValidateLabel(EditField.FieldLabel, out var labelError))
        {
            TempData["ProfileSettingsError"] = labelError;
            return RedirectToPage();
        }

        if (await FieldLabelExistsAsync(EditField.SectionKey, EditField.FieldLabel, id))
        {
            TempData["ProfileSettingsError"] = "\u064A\u0648\u062C\u062F \u062D\u0642\u0644 \u0622\u062E\u0631 \u0628\u0646\u0641\u0633 \u0627\u0644\u0627\u0633\u0645 \u062F\u0627\u062E\u0644 \u0646\u0641\u0633 \u0627\u0644\u0642\u0633\u0645.";
            return RedirectToPage();
        }

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
DECLARE @FieldKey nvarchar(120);

SELECT @FieldKey = FieldKey
FROM EmployeeProfileFieldDefinitions
WHERE Id = @Id;

UPDATE EmployeeProfileFieldDefinitions
SET SectionKey = @SectionKey,
    FieldLabel = @FieldLabel,
    FieldType = @FieldType,
    FieldOptions = @FieldOptions,
    IsRequired = @IsRequired,
    SortOrder = @SortOrder,
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @Id;

IF @FieldKey IS NOT NULL
BEGIN
    UPDATE EmployeeCustomFields
    SET FieldLabel = @FieldLabel,
        UpdatedAt = SYSUTCDATETIME()
    WHERE FieldKey = @FieldKey;
END;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", id);
                HrmsDatabase.AddParameter(command, "@SectionKey", EditField.SectionKey);
                HrmsDatabase.AddParameter(command, "@FieldLabel", EditField.FieldLabel);
                HrmsDatabase.AddParameter(command, "@FieldType", EditField.FieldType);
                HrmsDatabase.AddParameter(command, "@FieldOptions", NormalizeOptions(EditField.FieldType, EditField.FieldOptions));
                HrmsDatabase.AddParameter(command, "@IsRequired", EditField.IsRequired ? 1 : 0);
                HrmsDatabase.AddParameter(command, "@SortOrder", EditField.SortOrder);
            });

        TempData["ProfileSettingsMessage"] = "\u062A\u0645 \u062A\u0639\u062F\u064A\u0644 \u0627\u0644\u062D\u0642\u0644.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleFieldAsync(int id)
    {
        await EnsureSchemaAsync();

        if (id <= 0 || !await FieldExistsAsync(id))
        {
            TempData["ProfileSettingsError"] = "\u0627\u0644\u062D\u0642\u0644 \u063A\u064A\u0631 \u0635\u062D\u064A\u062D.";
            return RedirectToPage();
        }

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

    public async Task<IActionResult> OnPostDeleteFieldAsync(int id)
    {
        await EnsureSchemaAsync();

        if (id <= 0 || !await FieldExistsAsync(id))
        {
            TempData["ProfileSettingsError"] = "\u0627\u0644\u062D\u0642\u0644 \u063A\u064A\u0631 \u0635\u062D\u064A\u062D.";
            return RedirectToPage();
        }

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
DECLARE @FieldKey nvarchar(120);

SELECT @FieldKey = FieldKey
FROM EmployeeProfileFieldDefinitions
WHERE Id = @Id;

IF @FieldKey IS NOT NULL
BEGIN
    DELETE FROM EmployeeCustomFields
    WHERE FieldKey = @FieldKey;

    DELETE FROM EmployeeProfileFieldDefinitions
    WHERE Id = @Id;
END;
""",
            command => HrmsDatabase.AddParameter(command, "@Id", id));

        TempData["ProfileSettingsMessage"] = "\u062A\u0645 \u062D\u0630\u0641 \u0627\u0644\u062D\u0642\u0644.";
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        var sections = await EmployeeProfileSections.LoadAsync(_dbContext, activeOnly: false);

        var fields = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT
    Id,
    SectionKey,
    FieldKey,
    FieldLabel,
    FieldType,
    FieldOptions,
    IsRequired,
    IsActive,
    SortOrder
FROM EmployeeProfileFieldDefinitions
ORDER BY SortOrder, Id;
""",
            command => { },
            reader => new ProfileFieldView
            {
                Id = GetInt(reader, "Id"),
                SectionKey = GetString(reader, "SectionKey"),
                FieldKey = GetString(reader, "FieldKey"),
                FieldLabel = GetString(reader, "FieldLabel"),
                FieldType = NormalizeFieldType(GetString(reader, "FieldType")),
                FieldOptions = GetString(reader, "FieldOptions"),
                IsRequired = GetBool(reader, "IsRequired"),
                IsActive = GetBool(reader, "IsActive"),
                SortOrder = GetInt(reader, "SortOrder")
            });

        var grouped = fields
            .GroupBy(field => field.SectionKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.OrderBy(field => field.SortOrder).ThenBy(field => field.Id).ToList(), StringComparer.OrdinalIgnoreCase);

        Sections = sections
            .Select(section => new ProfileSectionView
            {
                Id = section.Id,
                Key = section.Key,
                Label = section.Label,
                SortOrder = section.SortOrder,
                IsSystem = section.IsSystem,
                IsActive = section.IsActive,
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

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_EmployeeProfileFieldDefinitions_Section_Label'
      AND object_id = OBJECT_ID(N'[dbo].[EmployeeProfileFieldDefinitions]')
)
BEGIN
    CREATE INDEX IX_EmployeeProfileFieldDefinitions_Section_Label
    ON [dbo].[EmployeeProfileFieldDefinitions] ([SectionKey], [FieldLabel]);
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

IF COL_LENGTH('EmployeeProfileFieldDefinitions', 'FieldOptions') IS NULL
    ALTER TABLE EmployeeProfileFieldDefinitions ADD FieldOptions nvarchar(max) NULL;
""");

        await EmployeeProfileSections.EnsureSchemaAsync(_dbContext);
    }

    private async Task<bool> FieldExistsAsync(int id)
    {
        var result = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT COUNT(1) AS CountValue
FROM EmployeeProfileFieldDefinitions
WHERE Id = @Id;
""",
            command => HrmsDatabase.AddParameter(command, "@Id", id),
            reader => GetInt(reader, "CountValue"));

        return result.FirstOrDefault() > 0;
    }

    private async Task<bool> FieldLabelExistsAsync(string sectionKey, string fieldLabel, int? excludeId)
    {
        var result = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT COUNT(1) AS CountValue
FROM EmployeeProfileFieldDefinitions
WHERE LOWER(LTRIM(RTRIM(SectionKey))) = LOWER(@SectionKey)
  AND LOWER(LTRIM(RTRIM(FieldLabel))) = LOWER(@FieldLabel)
  AND (@ExcludeId IS NULL OR Id <> @ExcludeId);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@SectionKey", NormalizeSectionKey(sectionKey));
                HrmsDatabase.AddParameter(command, "@FieldLabel", NormalizeLabel(fieldLabel));
                HrmsDatabase.AddParameter(command, "@ExcludeId", excludeId.HasValue ? excludeId.Value : DBNull.Value);
            },
            reader => GetInt(reader, "CountValue"));

        return result.FirstOrDefault() > 0;
    }

    private async Task<int> GetNextSortOrderAsync(string sectionKey)
    {
        var result = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT ISNULL(MAX(SortOrder), 0) + 10 AS NextSortOrder
FROM EmployeeProfileFieldDefinitions
WHERE LOWER(LTRIM(RTRIM(SectionKey))) = LOWER(@SectionKey);
""",
            command => HrmsDatabase.AddParameter(command, "@SectionKey", NormalizeSectionKey(sectionKey)),
            reader => GetInt(reader, "NextSortOrder"));

        var next = result.FirstOrDefault();
        return next > 0 ? next : 10;
    }

    private static bool ValidateLabel(string fieldLabel, out string error)
    {
        if (string.IsNullOrWhiteSpace(fieldLabel))
        {
            error = "\u0627\u0633\u0645 \u0627\u0644\u062D\u0642\u0644 \u0645\u0637\u0644\u0648\u0628.";
            return false;
        }

        if (fieldLabel.Length > FieldLabelMaxLength)
        {
            error = "\u0627\u0633\u0645 \u0627\u0644\u062D\u0642\u0644 \u0637\u0648\u064A\u0644 \u062C\u062F\u0627\u064B.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private async Task<bool> IsValidSectionAsync(string sectionKey)
    {
        var sections = await EmployeeProfileSections.LoadAsync(_dbContext, activeOnly: false);
        return sections.Any(section => section.Key.Equals(sectionKey, StringComparison.OrdinalIgnoreCase));
    }

    private static string GenerateSectionKey()
    {
        return $"section_{DateTime.UtcNow:yyyyMMddHHmmssfff}";
    }

    private async Task<int> GetNextSectionSortOrderAsync()
    {
        var result = await HrmsDatabase.QueryAsync(
            _dbContext,
            "SELECT ISNULL(MAX(SortOrder), 0) + 10 AS NextSortOrder FROM EmployeeProfileSections;",
            command => { },
            reader => GetInt(reader, "NextSortOrder"));

        var next = result.FirstOrDefault();
        return next > 0 ? next : 10;
    }

    private static string NormalizeOptions(string fieldType, string? options)
    {
        if (!fieldType.Equals("select", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var lines = (options ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct()
            .ToArray();

        return string.Join("\n", lines);
    }

    private static string NormalizeSectionKey(string? sectionKey)
    {
        return (sectionKey ?? string.Empty).Trim().ToLowerInvariant();
    }

    private static string NormalizeLabel(string? value)
    {
        var label = (value ?? string.Empty).Trim();

        while (label.Contains("  ", StringComparison.Ordinal))
        {
            label = label.Replace("  ", " ", StringComparison.Ordinal);
        }

        return label;
    }

    private static string GenerateFieldKey(string sectionKey)
    {
        var safeSection = new string((sectionKey ?? "additional")
            .ToLowerInvariant()
            .Where(c => char.IsLetterOrDigit(c) || c == '_')
            .ToArray());

        if (string.IsNullOrWhiteSpace(safeSection))
        {
            safeSection = "additional";
        }

        return $"{safeSection}_field_{DateTime.UtcNow:yyyyMMddHHmmssfff}";
    }

    private static string NormalizeFieldType(string? value) =>
        EmployeeProfileDynamicFields.NormalizeFieldType(value);

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

    public sealed class NewFieldInput
    {
        public string SectionKey { get; set; } = "basic";
        public string FieldLabel { get; set; } = string.Empty;
        public string FieldType { get; set; } = "text";
        public string? FieldOptions { get; set; }
        public bool IsRequired { get; set; }
        public int SortOrder { get; set; }
    }

    public sealed class EditFieldInput
    {
        public string SectionKey { get; set; } = "basic";
        public string FieldLabel { get; set; } = string.Empty;
        public string FieldType { get; set; } = "text";
        public string? FieldOptions { get; set; }
        public bool IsRequired { get; set; }
        public int SortOrder { get; set; }
    }

    public sealed class SectionInput
    {
        public string Label { get; set; } = string.Empty;
        public int SortOrder { get; set; }
    }

    public sealed class ProfileSectionView
    {
        public int Id { get; set; }
        public string Key { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public int SortOrder { get; set; }
        public bool IsSystem { get; set; }
        public bool IsActive { get; set; }
        public List<ProfileFieldView> Fields { get; set; } = new();
    }

    public sealed class ProfileFieldView
    {
        public int Id { get; set; }
        public string SectionKey { get; set; } = string.Empty;
        public string FieldKey { get; set; } = string.Empty;
        public string FieldLabel { get; set; } = string.Empty;
        public string FieldType { get; set; } = "text";
        public string FieldOptions { get; set; } = string.Empty;
        public bool IsRequired { get; set; }
        public bool IsActive { get; set; }
        public int SortOrder { get; set; }

        public string FieldTypeLabel => FieldType switch
        {
            "number" => "\u0631\u0642\u0645",
            "date" => "\u062A\u0627\u0631\u064A\u062E",
            "textarea" => "\u0646\u0635 \u0637\u0648\u064A\u0644",
            "select" => "\u0642\u0627\u0626\u0645\u0629 \u0645\u0646\u0633\u062F\u0644\u0629",
            "checkbox" => "\u062E\u0627\u0646\u0629 \u0627\u062E\u062A\u064A\u0627\u0631",
            _ => "\u0646\u0635"
        };
    }
}