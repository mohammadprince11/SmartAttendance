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
    // Kept for source compatibility. Schema and default rows are owned by
    // SqlSchemaMigrator migration 20260826-20.
    public static Task EnsureSchemaAsync(ApplicationDbContext dbContext) => Task.CompletedTask;

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
