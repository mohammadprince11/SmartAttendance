using SmartAttendance.Application.PeopleAi;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

public static class PeopleAiSettingsStore
{
    public static async Task EnsureDefaultsAsync(ApplicationDbContext db)
    {
        await PeopleAiSchema.VerifyAsync(db);
        await HrmsDatabase.ExecuteAsync(db, """
INSERT INTO dbo.CompanyPeopleAiSettings (CompanyId)
SELECT c.Id
FROM dbo.Companies c
WHERE ISNULL(c.IsDeleted, 0) = 0
  AND NOT EXISTS (
      SELECT 1 FROM dbo.CompanyPeopleAiSettings s WHERE s.CompanyId = c.Id);

INSERT INTO dbo.CompanyPeopleAiDocumentTypes
    (CompanyId, DocumentType, DisplayLabel, SortOrder, IsActive)
SELECT c.Id, v.DocumentType, v.DisplayLabel, v.SortOrder, 1
FROM dbo.Companies c
CROSS JOIN (VALUES
    (N'NationalId', N'البطاقة الوطنية', 10),
    (N'Passport', N'جواز السفر', 20),
    (N'Residence', N'الإقامة', 30),
    (N'Visa', N'التأشيرة', 40),
    (N'Contract', N'العقد', 50),
    (N'CV', N'السيرة الذاتية', 60),
    (N'Unknown', N'غير معروف', 999)
) v(DocumentType, DisplayLabel, SortOrder)
WHERE ISNULL(c.IsDeleted, 0) = 0
  AND NOT EXISTS (
      SELECT 1
      FROM dbo.CompanyPeopleAiDocumentTypes dt
      WHERE dt.CompanyId = c.Id
        AND dt.DocumentType = v.DocumentType);

INSERT INTO dbo.CompanyEmployeeDocumentPolicies
    (CompanyId, DocumentType, EmployeeCategory, Requirement,
     RequireExpiryDate, RequireOriginalVerification, IsActive)
SELECT c.Id, v.DocumentType, v.EmployeeCategory, v.Requirement,
       v.RequireExpiryDate, v.RequireOriginalVerification, 1
FROM dbo.Companies c
CROSS JOIN (VALUES
    (N'NationalId', N'Citizen', N'Required', CAST(0 AS bit), CAST(1 AS bit)),
    (N'Contract',   N'All',     N'Required', CAST(0 AS bit), CAST(0 AS bit)),
    (N'Passport',   N'Expat',   N'Required', CAST(1 AS bit), CAST(1 AS bit)),
    (N'Residence',  N'Expat',   N'Required', CAST(1 AS bit), CAST(1 AS bit))
) v(DocumentType, EmployeeCategory, Requirement, RequireExpiryDate, RequireOriginalVerification)
WHERE ISNULL(c.IsDeleted, 0) = 0
  AND NOT EXISTS (
      SELECT 1
      FROM dbo.CompanyEmployeeDocumentPolicies p
      WHERE p.CompanyId = c.Id
        AND p.DocumentType = v.DocumentType
        AND p.EmployeeCategory = v.EmployeeCategory);

INSERT INTO dbo.CompanyPeopleAiFieldPolicies
    (CompanyId, DocumentType, FieldKey, DisplayLabel,
     Requirement, SortOrder, AllowBulkApprove, IsActive)
SELECT c.Id, v.DocumentType, v.FieldKey, v.DisplayLabel,
       v.Requirement, v.SortOrder, v.AllowBulkApprove, 1
FROM dbo.Companies c
CROSS JOIN (VALUES
    (N'NationalId', N'NationalNumber', N'الرقم الوطني', N'Required', 10, CAST(1 AS bit)),
    (N'NationalId', N'DocumentNumber', N'رقم المستند', N'Optional', 20, CAST(1 AS bit)),
    (N'NationalId', N'FamilyNumber', N'الرقم العائلي', N'Optional', 30, CAST(1 AS bit)),
    (N'NationalId', N'FirstName', N'الاسم', N'Required', 40, CAST(1 AS bit)),
    (N'NationalId', N'SecondName', N'اسم الأب', N'Required', 50, CAST(1 AS bit)),
    (N'NationalId', N'ThirdName', N'اسم الجد', N'Optional', 60, CAST(1 AS bit)),
    (N'NationalId', N'LastName', N'اللقب', N'Optional', 70, CAST(1 AS bit)),
    (N'NationalId', N'MotherName', N'اسم الأم', N'Optional', 80, CAST(1 AS bit)),
    (N'NationalId', N'DateOfBirth', N'تاريخ الميلاد', N'Required', 90, CAST(1 AS bit)),
    (N'NationalId', N'ExpiryDate', N'تاريخ الانتهاء', N'Optional', 100, CAST(1 AS bit)),
    (N'NationalId', N'Nationality', N'الجنسية', N'Optional', 110, CAST(1 AS bit)),
    (N'NationalId', N'Sex', N'الجنس', N'Optional', 120, CAST(1 AS bit)),
    (N'NationalId', N'IssuingCountry', N'بلد الإصدار', N'Optional', 130, CAST(1 AS bit)),
    (N'Passport', N'DocumentNumber', N'رقم الجواز', N'Required', 10, CAST(1 AS bit)),
    (N'Passport', N'GivenNames', N'الأسماء', N'Required', 20, CAST(1 AS bit)),
    (N'Passport', N'Surname', N'اللقب / اسم العائلة', N'Required', 30, CAST(1 AS bit)),
    (N'Passport', N'DateOfBirth', N'تاريخ الميلاد', N'Required', 40, CAST(1 AS bit)),
    (N'Passport', N'ExpiryDate', N'تاريخ الانتهاء', N'Required', 50, CAST(1 AS bit)),
    (N'Passport', N'Nationality', N'الجنسية', N'Optional', 60, CAST(1 AS bit)),
    (N'Passport', N'Sex', N'الجنس', N'Optional', 70, CAST(1 AS bit)),
    (N'Passport', N'IssuingCountry', N'بلد الإصدار', N'Optional', 80, CAST(1 AS bit)),
    (N'Residence', N'DocumentNumber', N'رقم الإقامة', N'Required', 10, CAST(1 AS bit)),
    (N'Residence', N'ExpiryDate', N'تاريخ الانتهاء', N'Required', 20, CAST(1 AS bit)),
    (N'CV', N'FullName', N'الاسم الكامل', N'Optional', 10, CAST(1 AS bit)),
    (N'CV', N'Phone', N'الهاتف', N'Optional', 20, CAST(1 AS bit)),
    (N'CV', N'PersonalEmail', N'البريد الشخصي', N'Optional', 30, CAST(1 AS bit)),
    (N'CV', N'Address', N'العنوان', N'Optional', 40, CAST(1 AS bit)),
    (N'CV', N'Nationality', N'الجنسية', N'Optional', 50, CAST(1 AS bit)),
    (N'CV', N'Skills', N'المهارات', N'Optional', 60, CAST(1 AS bit)),
    (N'CV', N'Languages', N'اللغات', N'Optional', 70, CAST(1 AS bit))
) v(DocumentType, FieldKey, DisplayLabel, Requirement, SortOrder, AllowBulkApprove)
WHERE ISNULL(c.IsDeleted, 0) = 0
  AND NOT EXISTS (
      SELECT 1
      FROM dbo.CompanyPeopleAiFieldPolicies fp
      WHERE fp.CompanyId = c.Id
        AND fp.DocumentType = v.DocumentType
        AND fp.FieldKey = v.FieldKey);
""");
    }

    public static async Task<CompanyPeopleAiPolicy> GetAsync(
        ApplicationDbContext db,
        int companyId)
    {
        await EnsureDefaultsAsync(db);

        var rows = await HrmsDatabase.QueryAsync(
            db,
            """
SELECT TOP 1 CompanyId, DuplicateScope, DuplicateAction, ReviewerMode,
       EnabledLanguages, CloudProcessingAllowed, IsEnabled
FROM dbo.CompanyPeopleAiSettings
WHERE CompanyId = @CompanyId;
""",
            command => HrmsDatabase.AddParameter(command, "@CompanyId", companyId),
            reader => new CompanyPeopleAiPolicy(
                HrmsDatabase.GetInt(reader, "CompanyId"),
                ParseDuplicateScope(HrmsDatabase.GetString(reader, "DuplicateScope")),
                ParseDuplicateAction(HrmsDatabase.GetString(reader, "DuplicateAction")),
                ParseReviewerMode(HrmsDatabase.GetString(reader, "ReviewerMode")),
                ParseLanguages(HrmsDatabase.GetString(reader, "EnabledLanguages")),
                HrmsDatabase.GetBool(reader, "CloudProcessingAllowed"),
                HrmsDatabase.GetBool(reader, "IsEnabled")));

        return rows.FirstOrDefault() ?? new CompanyPeopleAiPolicy(
            companyId,
            PeopleAiDuplicateScope.AuthorizedCompanies,
            PeopleAiDuplicateAction.RequireReview,
            PeopleAiReviewerMode.AdminOrCreatorWithPermission,
            ["ar", "en"],
            CloudProcessingAllowed: false,
            IsEnabled: true);
    }

    public static async Task SaveAsync(
        ApplicationDbContext db,
        CompanyPeopleAiPolicy policy,
        string? updatedBy)
    {
        await EnsureDefaultsAsync(db);
        var languages = string.Join(';', policy.EnabledLanguages
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(languages))
        {
            languages = "ar;en";
        }

        await HrmsDatabase.ExecuteAsync(
            db,
            """
UPDATE dbo.CompanyPeopleAiSettings
SET DuplicateScope = @DuplicateScope,
    DuplicateAction = @DuplicateAction,
    ReviewerMode = @ReviewerMode,
    EnabledLanguages = @Languages,
    CloudProcessingAllowed = 0,
    IsEnabled = @IsEnabled,
    UpdatedBy = @UpdatedBy,
    UpdatedAt = SYSUTCDATETIME()
WHERE CompanyId = @CompanyId;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@CompanyId", policy.CompanyId);
                HrmsDatabase.AddParameter(command, "@DuplicateScope", policy.DuplicateScope.ToString());
                HrmsDatabase.AddParameter(command, "@DuplicateAction", policy.DuplicateAction.ToString());
                HrmsDatabase.AddParameter(command, "@ReviewerMode", policy.ReviewerMode.ToString());
                HrmsDatabase.AddParameter(command, "@Languages", languages);
                HrmsDatabase.AddParameter(command, "@IsEnabled", policy.IsEnabled);
                HrmsDatabase.AddParameter(command, "@UpdatedBy", (object?)updatedBy ?? DBNull.Value);
            });
    }

    public static async Task<List<PeopleAiDocumentTypeDefinition>> ListDocumentTypesAsync(
        ApplicationDbContext db,
        int companyId,
        bool includeInactive = false)
    {
        await EnsureDefaultsAsync(db);

        return await HrmsDatabase.QueryAsync(
            db,
            """
SELECT CompanyId, DocumentType, DisplayLabel, SortOrder, IsActive
FROM dbo.CompanyPeopleAiDocumentTypes
WHERE CompanyId = @CompanyId
  AND (@IncludeInactive = 1 OR IsActive = 1)
ORDER BY SortOrder, DisplayLabel, DocumentType;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@CompanyId", companyId);
                HrmsDatabase.AddParameter(command, "@IncludeInactive", includeInactive);
            },
            reader => new PeopleAiDocumentTypeDefinition(
                HrmsDatabase.GetInt(reader, "CompanyId"),
                HrmsDatabase.GetString(reader, "DocumentType"),
                HrmsDatabase.GetString(reader, "DisplayLabel"),
                HrmsDatabase.GetInt(reader, "SortOrder"),
                HrmsDatabase.GetBool(reader, "IsActive")));
    }

    public static async Task SaveDocumentTypeAsync(
        ApplicationDbContext db,
        PeopleAiDocumentTypeDefinition definition)
    {
        await EnsureDefaultsAsync(db);

        var documentType = CleanKey(definition.DocumentType, 50);
        var displayLabel = (definition.DisplayLabel ?? string.Empty).Trim();
        var sortOrder = Math.Clamp(definition.SortOrder, 0, 9999);

        if (documentType.Length == 0 || displayLabel.Length == 0)
        {
            throw new InvalidOperationException(
                "Document type and display label are required.");
        }

        if (displayLabel.Length > 150)
        {
            displayLabel = displayLabel[..150];
        }

        await HrmsDatabase.ExecuteAsync(
            db,
            """
UPDATE dbo.CompanyPeopleAiDocumentTypes
SET DisplayLabel = @DisplayLabel,
    SortOrder = @SortOrder,
    IsActive = @IsActive,
    UpdatedAt = SYSUTCDATETIME()
WHERE CompanyId = @CompanyId
  AND DocumentType = @DocumentType;

IF @@ROWCOUNT = 0
BEGIN
    INSERT INTO dbo.CompanyPeopleAiDocumentTypes
        (CompanyId, DocumentType, DisplayLabel, SortOrder, IsActive)
    VALUES
        (@CompanyId, @DocumentType, @DisplayLabel, @SortOrder, @IsActive);
END;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@CompanyId", definition.CompanyId);
                HrmsDatabase.AddParameter(command, "@DocumentType", documentType);
                HrmsDatabase.AddParameter(command, "@DisplayLabel", displayLabel);
                HrmsDatabase.AddParameter(command, "@SortOrder", sortOrder);
                HrmsDatabase.AddParameter(command, "@IsActive", definition.IsActive);
            });
    }

    public static async Task DisableDocumentTypeAsync(
        ApplicationDbContext db,
        int companyId,
        string documentType)
    {
        await EnsureDefaultsAsync(db);

        await HrmsDatabase.ExecuteAsync(
            db,
            """
UPDATE dbo.CompanyPeopleAiDocumentTypes
SET IsActive = 0,
    UpdatedAt = SYSUTCDATETIME()
WHERE CompanyId = @CompanyId
  AND DocumentType = @DocumentType;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@CompanyId", companyId);
                HrmsDatabase.AddParameter(
                    command,
                    "@DocumentType",
                    CleanKey(documentType, 50));
            });
    }

    public static async Task<List<EmployeeDocumentPolicy>> ListDocumentPoliciesAsync(
        ApplicationDbContext db,
        int companyId)
    {
        await EnsureDefaultsAsync(db);

        return await HrmsDatabase.QueryAsync(
            db,
            """
SELECT CompanyId, DocumentType, EmployeeCategory, Requirement,
       RequireExpiryDate, RequireOriginalVerification, IsActive
FROM dbo.CompanyEmployeeDocumentPolicies
WHERE CompanyId = @CompanyId
ORDER BY DocumentType, EmployeeCategory;
""",
            command => HrmsDatabase.AddParameter(command, "@CompanyId", companyId),
            reader => new EmployeeDocumentPolicy(
                HrmsDatabase.GetInt(reader, "CompanyId"),
                HrmsDatabase.GetString(reader, "DocumentType"),
                HrmsDatabase.GetString(reader, "EmployeeCategory"),
                HrmsDatabase.GetString(reader, "Requirement"),
                HrmsDatabase.GetBool(reader, "RequireExpiryDate"),
                HrmsDatabase.GetBool(reader, "RequireOriginalVerification"),
                HrmsDatabase.GetBool(reader, "IsActive")));
    }

    public static async Task SaveDocumentPolicyAsync(
        ApplicationDbContext db,
        EmployeeDocumentPolicy policy)
    {
        await EnsureDefaultsAsync(db);

        var requirement = policy.Requirement is "Required" or "Optional" or "NotApplicable"
            ? policy.Requirement
            : "Optional";

        await HrmsDatabase.ExecuteAsync(
            db,
            """
UPDATE dbo.CompanyEmployeeDocumentPolicies
SET Requirement = @Requirement,
    RequireExpiryDate = @RequireExpiryDate,
    RequireOriginalVerification = @RequireOriginalVerification,
    IsActive = @IsActive,
    UpdatedAt = SYSUTCDATETIME()
WHERE CompanyId = @CompanyId
  AND DocumentType = @DocumentType
  AND EmployeeCategory = @EmployeeCategory;

IF @@ROWCOUNT = 0
BEGIN
    INSERT INTO dbo.CompanyEmployeeDocumentPolicies
        (CompanyId, DocumentType, EmployeeCategory, Requirement,
         RequireExpiryDate, RequireOriginalVerification, IsActive)
    VALUES
        (@CompanyId, @DocumentType, @EmployeeCategory, @Requirement,
         @RequireExpiryDate, @RequireOriginalVerification, @IsActive);
END;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@CompanyId", policy.CompanyId);
                HrmsDatabase.AddParameter(command, "@DocumentType", policy.DocumentType);
                HrmsDatabase.AddParameter(command, "@EmployeeCategory", policy.EmployeeCategory);
                HrmsDatabase.AddParameter(command, "@Requirement", requirement);
                HrmsDatabase.AddParameter(command, "@RequireExpiryDate", policy.RequireExpiryDate);
                HrmsDatabase.AddParameter(command, "@RequireOriginalVerification", policy.RequireOriginalVerification);
                HrmsDatabase.AddParameter(command, "@IsActive", policy.IsActive);
            });
    }

    public static async Task<List<PeopleAiFieldPolicy>> ListFieldPoliciesAsync(
        ApplicationDbContext db,
        int companyId,
        bool includeInactive = false)
    {
        await EnsureDefaultsAsync(db);

        return await HrmsDatabase.QueryAsync(
            db,
            """
SELECT CompanyId, DocumentType, FieldKey, DisplayLabel,
       Requirement, SortOrder, AllowBulkApprove, IsActive
FROM dbo.CompanyPeopleAiFieldPolicies
WHERE CompanyId = @CompanyId
  AND (@IncludeInactive = 1 OR IsActive = 1)
ORDER BY DocumentType, SortOrder, DisplayLabel, FieldKey;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@CompanyId", companyId);
                HrmsDatabase.AddParameter(command, "@IncludeInactive", includeInactive);
            },
            reader => new PeopleAiFieldPolicy(
                HrmsDatabase.GetInt(reader, "CompanyId"),
                HrmsDatabase.GetString(reader, "DocumentType"),
                HrmsDatabase.GetString(reader, "FieldKey"),
                HrmsDatabase.GetString(reader, "DisplayLabel"),
                HrmsDatabase.GetString(reader, "Requirement"),
                HrmsDatabase.GetInt(reader, "SortOrder"),
                HrmsDatabase.GetBool(reader, "AllowBulkApprove"),
                HrmsDatabase.GetBool(reader, "IsActive")));
    }

    public static async Task SaveFieldPolicyAsync(
        ApplicationDbContext db,
        PeopleAiFieldPolicy policy)
    {
        await EnsureDefaultsAsync(db);

        var documentType = CleanKey(policy.DocumentType, 50);
        var fieldKey = CleanKey(policy.FieldKey, 100);
        var displayLabel = (policy.DisplayLabel ?? string.Empty).Trim();
        var requirement = policy.Requirement is "Required" or "Optional"
            ? policy.Requirement
            : "Optional";
        var sortOrder = Math.Clamp(policy.SortOrder, 0, 9999);

        if (documentType.Length == 0 ||
            fieldKey.Length == 0 ||
            displayLabel.Length == 0)
        {
            throw new InvalidOperationException(
                "Document type, field key and display label are required.");
        }

        if (displayLabel.Length > 150)
        {
            displayLabel = displayLabel[..150];
        }

        await HrmsDatabase.ExecuteAsync(
            db,
            """
UPDATE dbo.CompanyPeopleAiFieldPolicies
SET DisplayLabel = @DisplayLabel,
    Requirement = @Requirement,
    SortOrder = @SortOrder,
    AllowBulkApprove = @AllowBulkApprove,
    IsActive = @IsActive,
    UpdatedAt = SYSUTCDATETIME()
WHERE CompanyId = @CompanyId
  AND DocumentType = @DocumentType
  AND FieldKey = @FieldKey;

IF @@ROWCOUNT = 0
BEGIN
    INSERT INTO dbo.CompanyPeopleAiFieldPolicies
        (CompanyId, DocumentType, FieldKey, DisplayLabel,
         Requirement, SortOrder, AllowBulkApprove, IsActive)
    VALUES
        (@CompanyId, @DocumentType, @FieldKey, @DisplayLabel,
         @Requirement, @SortOrder, @AllowBulkApprove, @IsActive);
END;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@CompanyId", policy.CompanyId);
                HrmsDatabase.AddParameter(command, "@DocumentType", documentType);
                HrmsDatabase.AddParameter(command, "@FieldKey", fieldKey);
                HrmsDatabase.AddParameter(command, "@DisplayLabel", displayLabel);
                HrmsDatabase.AddParameter(command, "@Requirement", requirement);
                HrmsDatabase.AddParameter(command, "@SortOrder", sortOrder);
                HrmsDatabase.AddParameter(
                    command, "@AllowBulkApprove", policy.AllowBulkApprove);
                HrmsDatabase.AddParameter(command, "@IsActive", policy.IsActive);
            });
    }

    public static async Task DisableFieldPolicyAsync(
        ApplicationDbContext db,
        int companyId,
        string documentType,
        string fieldKey)
    {
        await EnsureDefaultsAsync(db);

        await HrmsDatabase.ExecuteAsync(
            db,
            """
UPDATE dbo.CompanyPeopleAiFieldPolicies
SET IsActive = 0,
    UpdatedAt = SYSUTCDATETIME()
WHERE CompanyId = @CompanyId
  AND DocumentType = @DocumentType
  AND FieldKey = @FieldKey;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@CompanyId", companyId);
                HrmsDatabase.AddParameter(
                    command, "@DocumentType", CleanKey(documentType, 50));
                HrmsDatabase.AddParameter(
                    command, "@FieldKey", CleanKey(fieldKey, 100));
            });
    }

    private static string CleanKey(string? value, int maxLength)
    {
        var cleaned = new string((value ?? string.Empty)
            .Trim()
            .Where(c => char.IsLetterOrDigit(c) ||
                        c is '_' or '-' or '.')
            .ToArray());

        return cleaned.Length <= maxLength
            ? cleaned
            : cleaned[..maxLength];
    }

    private static PeopleAiDuplicateScope ParseDuplicateScope(string value) =>
        Enum.TryParse<PeopleAiDuplicateScope>(value, true, out var parsed)
            ? parsed
            : PeopleAiDuplicateScope.AuthorizedCompanies;

    private static PeopleAiDuplicateAction ParseDuplicateAction(string value) =>
        Enum.TryParse<PeopleAiDuplicateAction>(value, true, out var parsed)
            ? parsed
            : PeopleAiDuplicateAction.RequireReview;

    private static PeopleAiReviewerMode ParseReviewerMode(string value) =>
        Enum.TryParse<PeopleAiReviewerMode>(value, true, out var parsed)
            ? parsed
            : PeopleAiReviewerMode.AdminOrCreatorWithPermission;

    private static IReadOnlyList<string> ParseLanguages(string value) =>
        (value ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
