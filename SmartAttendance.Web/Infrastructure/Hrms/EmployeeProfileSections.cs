using System.Data.Common;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// Admin-defined profile sections (tabs/groups) for the dynamic field builder.
/// Replaces the old fixed 5-section list: the same five are seeded as system
/// sections (renamable/orderable, not deletable) and the admin can add more.
/// </summary>
public static class EmployeeProfileSections
{
    public static async Task EnsureSchemaAsync(ApplicationDbContext dbContext)
    {
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
IF OBJECT_ID(N'[dbo].[EmployeeProfileSections]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[EmployeeProfileSections]
    (
        [Id] int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [SectionKey] nvarchar(80) NOT NULL,
        [Label] nvarchar(150) NOT NULL,
        [SortOrder] int NOT NULL CONSTRAINT DF_EmployeeProfileSections_SortOrder DEFAULT 0,
        [IsSystem] bit NOT NULL CONSTRAINT DF_EmployeeProfileSections_IsSystem DEFAULT 0,
        [IsActive] bit NOT NULL CONSTRAINT DF_EmployeeProfileSections_IsActive DEFAULT 1,
        [CreatedAt] datetime2 NOT NULL CONSTRAINT DF_EmployeeProfileSections_CreatedAt DEFAULT SYSUTCDATETIME(),
        [UpdatedAt] datetime2 NULL
    );

    CREATE UNIQUE INDEX UX_EmployeeProfileSections_Key ON [dbo].[EmployeeProfileSections] ([SectionKey]);
END;

IF NOT EXISTS (SELECT 1 FROM EmployeeProfileSections)
BEGIN
    INSERT INTO EmployeeProfileSections (SectionKey, Label, SortOrder, IsSystem, IsActive)
    VALUES
        (N'basic',      N'البيانات الأساسية',    10, 1, 1),
        (N'personal',   N'المعلومات الشخصية',    20, 1, 1),
        (N'job',        N'المعلومات الوظيفية',   30, 1, 1),
        (N'financial',  N'المعلومات المالية',    40, 1, 1),
        (N'additional', N'معلومات إضافية',       50, 1, 1);
END;
""");
    }

    public static async Task<List<ProfileSectionDefinition>> LoadAsync(
        ApplicationDbContext dbContext,
        bool activeOnly = true)
    {
        await EnsureSchemaAsync(dbContext);

        return await HrmsDatabase.QueryAsync(
            dbContext,
            $"""
SELECT Id, SectionKey, Label, SortOrder, IsSystem, IsActive
FROM EmployeeProfileSections
{(activeOnly ? "WHERE IsActive = 1" : string.Empty)}
ORDER BY SortOrder, Id;
""",
            command => { },
            reader => new ProfileSectionDefinition
            {
                Id = GetInt(reader, "Id"),
                Key = GetString(reader, "SectionKey"),
                Label = GetString(reader, "Label"),
                SortOrder = GetInt(reader, "SortOrder"),
                IsSystem = GetBool(reader, "IsSystem"),
                IsActive = GetBool(reader, "IsActive")
            });
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
}

public sealed class ProfileSectionDefinition
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; }
}
