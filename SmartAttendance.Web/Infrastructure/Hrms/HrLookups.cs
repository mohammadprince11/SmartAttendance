using System.Data.Common;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// Generic admin-managed reference lists (master data) — one table for all
/// categories, fitting the no-code vision: the admin maintains the lists from
/// the UI and employee fields store the Arabic value.
/// </summary>
public static class HrLookups
{
    public sealed record LookupCategory(string Key, string Label);

    public static readonly IReadOnlyList<LookupCategory> Categories = new[]
    {
        new LookupCategory("religions", "الديانات"),
        new LookupCategory("worktypes", "أنواع الدوام"),
        new LookupCategory("grades", "الدرجات الوظيفية"),
        new LookupCategory("sponsors", "الكفلاء"),
        new LookupCategory("assettypes", "أنواع العهد"),
        new LookupCategory("nationalities", "الجنسيات")
    };

    public sealed class LookupItem
    {
        public int Id { get; set; }
        public string Category { get; set; } = string.Empty;
        public string ArabicName { get; set; } = string.Empty;
        public string? EnglishName { get; set; }
        public bool IsActive { get; set; }
        public int SortOrder { get; set; }
    }

    public static async Task EnsureSchemaAsync(ApplicationDbContext db)
    {
        await HrmsDatabase.ExecuteAsync(
            db,
            """
IF OBJECT_ID(N'[dbo].[HrLookups]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[HrLookups]
    (
        [Id] int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [Category] nvarchar(60) NOT NULL,
        [ArabicName] nvarchar(150) NOT NULL,
        [EnglishName] nvarchar(150) NULL,
        [IsActive] bit NOT NULL CONSTRAINT DF_HrLookups_IsActive DEFAULT 1,
        [SortOrder] int NOT NULL CONSTRAINT DF_HrLookups_SortOrder DEFAULT 0,
        [CreatedAt] datetime2 NOT NULL CONSTRAINT DF_HrLookups_CreatedAt DEFAULT SYSUTCDATETIME()
    );

    CREATE INDEX IX_HrLookups_Category ON HrLookups (Category, SortOrder, Id);
END;

IF NOT EXISTS (SELECT 1 FROM HrLookups)
BEGIN
    INSERT INTO HrLookups (Category, ArabicName, EnglishName, SortOrder)
    VALUES
        (N'religions', N'مسلم',    N'Muslim',    10),
        (N'religions', N'مسيحي',   N'Christian', 20),
        (N'religions', N'إيزيدي',  N'Yazidi',    30),
        (N'religions', N'صابئي',   N'Sabean',    40),
        (N'religions', N'أخرى',    N'Other',     50),
        (N'worktypes', N'دوام كامل',  N'Full Time', 10),
        (N'worktypes', N'دوام جزئي',  N'Part Time', 20),
        (N'worktypes', N'مناوبات',    N'Shifts',    30),
        (N'worktypes', N'عن بعد',     N'Remote',    40),
        (N'grades', N'الأولى',   N'Grade 1', 10),
        (N'grades', N'الثانية',  N'Grade 2', 20),
        (N'grades', N'الثالثة',  N'Grade 3', 30),
        (N'grades', N'الرابعة',  N'Grade 4', 40),
        (N'grades', N'الخامسة',  N'Grade 5', 50),
        (N'assettypes', N'حاسوب محمول', N'Laptop',  10),
        (N'assettypes', N'هاتف',        N'Phone',   20),
        (N'assettypes', N'سيارة',       N'Vehicle', 30),
        (N'assettypes', N'أدوات عمل',   N'Tools',   40),
        (N'nationalities', N'عراقي',      N'Iraqi',       10),
        (N'nationalities', N'سوري',       N'Syrian',      20),
        (N'nationalities', N'أردني',      N'Jordanian',   30),
        (N'nationalities', N'لبناني',     N'Lebanese',    40),
        (N'nationalities', N'مصري',       N'Egyptian',    50),
        (N'nationalities', N'هندي',       N'Indian',      60),
        (N'nationalities', N'باكستاني',   N'Pakistani',   70),
        (N'nationalities', N'بنغلادشي',   N'Bangladeshi', 80),
        (N'nationalities', N'فلبيني',     N'Filipino',    90),
        (N'nationalities', N'نيبالي',     N'Nepali',      100),
        (N'nationalities', N'أخرى',       N'Other',       110);
END;
""");
    }

    public static async Task<List<LookupItem>> LoadAsync(
        ApplicationDbContext db, string? category = null, bool activeOnly = false)
    {
        await EnsureSchemaAsync(db);

        var where = new List<string>();
        if (!string.IsNullOrWhiteSpace(category)) where.Add("Category = @Category");
        if (activeOnly) where.Add("IsActive = 1");

        return await HrmsDatabase.QueryAsync(
            db,
            $"""
SELECT Id, Category, ArabicName, EnglishName, IsActive, SortOrder
FROM HrLookups
{(where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : string.Empty)}
ORDER BY Category, SortOrder, Id;
""",
            command =>
            {
                if (!string.IsNullOrWhiteSpace(category)) HrmsDatabase.AddParameter(command, "@Category", category);
            },
            Map);
    }

    /// <summary>Active Arabic values of one category — for dropdowns.</summary>
    public static async Task<List<string>> ValuesAsync(ApplicationDbContext db, string category)
    {
        var items = await LoadAsync(db, category, activeOnly: true);
        return items.Select(i => i.ArabicName).ToList();
    }

    private static LookupItem Map(DbDataReader reader) => new()
    {
        Id = HrmsDatabase.GetInt(reader, "Id"),
        Category = HrmsDatabase.GetString(reader, "Category"),
        ArabicName = HrmsDatabase.GetString(reader, "ArabicName"),
        EnglishName = HrmsDatabase.GetString(reader, "EnglishName"),
        IsActive = HrmsDatabase.GetBool(reader, "IsActive"),
        SortOrder = HrmsDatabase.GetInt(reader, "SortOrder")
    };
}
