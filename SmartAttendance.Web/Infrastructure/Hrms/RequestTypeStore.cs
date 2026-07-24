using System.Data.Common;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// مركز أنواع الطلبات الداينمك: تبويبات (فئات) + أنواع بضوابط يديرها الأدمن.
/// التبويبات نفسها قابلة للإضافة، وكل نوع يحمل ضوابطه (أيام/مالي/رصيد/مرفق/وقت…).
/// ملاحظة: قالب الموافقة **ليس هنا** — له شاشته المنفصلة التفصيلية.
/// </summary>
public static class RequestTypeStore
{
    public sealed class Category
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? NameEn { get; set; }
        public int DisplayOrder { get; set; }
        public bool IsActive { get; set; } = true;
        // مدخل مستقل: يظهر كعنصر في منتقي «طلب جديد» بشاشته الخاصة، لا كتبويب داخل الإجازة.
        public bool IsPickerEntry { get; set; }
    }

    public sealed class ReqType
    {
        public int Id { get; set; }
        public int CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? NameEn { get; set; }
        public int? AllowedDays { get; set; }        // عدد الأيام المسموح (null = بلا حد)
        public string Repeat { get; set; } = "yearly"; // once | yearly | event
        public int ServiceMonths { get; set; }        // شرط مدة الخدمة (أشهر)
        public string Gender { get; set; } = "all";    // all | male | female
        public string PaidMode { get; set; } = "full"; // full | half | unpaid
        public bool DeductFromSalary { get; set; }
        public bool CountsInService { get; set; } = true;
        public bool HasBalance { get; set; }
        public int? MaxPerRequest { get; set; }
        public bool AttachmentRequired { get; set; }
        public string? AttachmentLabel { get; set; }
        public bool NeedsTime { get; set; }
        public bool IsActive { get; set; } = true;
        public int DisplayOrder { get; set; }
    }

    public static async Task EnsureAsync(ApplicationDbContext db)
    {
        await HrmsDatabase.ExecuteAsync(db, """
IF OBJECT_ID('RequestCategories','U') IS NULL
BEGIN
    CREATE TABLE RequestCategories(
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Name nvarchar(80) NOT NULL,
        NameEn nvarchar(80) NULL,
        DisplayOrder int NOT NULL DEFAULT(0),
        IsActive bit NOT NULL DEFAULT(1),
        IsPickerEntry bit NOT NULL DEFAULT(0)
    );
END;
IF COL_LENGTH('RequestCategories','IsPickerEntry') IS NULL
    ALTER TABLE RequestCategories ADD IsPickerEntry bit NOT NULL CONSTRAINT DF_RC_Picker DEFAULT(0);
IF OBJECT_ID('RequestTypes','U') IS NULL
BEGIN
    CREATE TABLE RequestTypes(
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        CategoryId int NOT NULL,
        Name nvarchar(120) NOT NULL,
        NameEn nvarchar(120) NULL,
        AllowedDays int NULL,
        Repeat nvarchar(20) NOT NULL DEFAULT('yearly'),
        ServiceMonths int NOT NULL DEFAULT(0),
        Gender nvarchar(10) NOT NULL DEFAULT('all'),
        PaidMode nvarchar(10) NOT NULL DEFAULT('full'),
        DeductFromSalary bit NOT NULL DEFAULT(0),
        CountsInService bit NOT NULL DEFAULT(1),
        HasBalance bit NOT NULL DEFAULT(0),
        MaxPerRequest int NULL,
        AttachmentRequired bit NOT NULL DEFAULT(0),
        AttachmentLabel nvarchar(120) NULL,
        NeedsTime bit NOT NULL DEFAULT(0),
        IsActive bit NOT NULL DEFAULT(1),
        DisplayOrder int NOT NULL DEFAULT(0)
    );
END;
""");
        await SeedAsync(db);
    }

    private static async Task SeedAsync(ApplicationDbContext db)
    {
        var count = await HrmsDatabase.ScalarAsync<int>(db, "SELECT COUNT(1) FROM RequestCategories");
        if (count > 0) return;

        await HrmsDatabase.ExecuteAsync(db, """
INSERT INTO RequestCategories(Name, NameEn, DisplayOrder, IsPickerEntry) VALUES
(N'الإجازات', 'Leaves', 1, 0),
(N'العرضية', 'Casual', 2, 0),
(N'المغادرات', 'Departures', 3, 0),
(N'الأوفرتايم', 'Overtime', 4, 1);

DECLARE @lv int=(SELECT Id FROM RequestCategories WHERE Name=N'الإجازات');
DECLARE @cs int=(SELECT Id FROM RequestCategories WHERE Name=N'العرضية');
DECLARE @dp int=(SELECT Id FROM RequestCategories WHERE Name=N'المغادرات');
DECLARE @ot int=(SELECT Id FROM RequestCategories WHERE Name=N'الأوفرتايم');

INSERT INTO RequestTypes(CategoryId,Name,PaidMode,DeductFromSalary,HasBalance,NeedsTime,AttachmentRequired,DisplayOrder) VALUES
(@lv,N'إجازة سنوية','full',0,1,0,0,1),
(@lv,N'إجازة مرضية','full',0,1,0,0,2),
(@cs,N'مهمة عمل','full',0,0,0,0,1),
(@cs,N'إجازة وفاة','full',0,0,0,0,2),
(@cs,N'إجازة غير مدفوعة','unpaid',1,0,0,0,3),
(@dp,N'مغادرة شخصية','full',0,0,1,0,1),
(@dp,N'مغادرة عمل','full',0,0,1,0,2),
(@dp,N'مغادرة غير مدفوعة','unpaid',1,0,1,0,3),
(@ot,N'عمل إضافي','full',0,0,1,0,1);
""");
    }

    public static Task<List<Category>> ListCategoriesAsync(ApplicationDbContext db, bool onlyActive = false) =>
        HrmsDatabase.QueryAsync(db,
            $"SELECT * FROM RequestCategories {(onlyActive ? "WHERE IsActive=1" : "")} ORDER BY DisplayOrder, Id",
            null,
            r => new Category
            {
                Id = HrmsDatabase.GetInt(r, "Id"),
                Name = HrmsDatabase.GetString(r, "Name"),
                NameEn = HrmsDatabase.GetString(r, "NameEn"),
                DisplayOrder = HrmsDatabase.GetInt(r, "DisplayOrder"),
                IsActive = HrmsDatabase.GetBool(r, "IsActive"),
                IsPickerEntry = HrmsDatabase.GetBool(r, "IsPickerEntry")
            });

    public static Task<List<ReqType>> ListTypesAsync(ApplicationDbContext db, int? categoryId = null, bool onlyActive = false)
    {
        var where = new List<string>();
        if (categoryId.HasValue) where.Add("t.CategoryId=@cat");
        if (onlyActive) where.Add("t.IsActive=1");
        var clause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";
        return HrmsDatabase.QueryAsync(db,
            $@"SELECT t.*, c.Name AS CategoryName FROM RequestTypes t
               JOIN RequestCategories c ON c.Id=t.CategoryId {clause}
               ORDER BY t.CategoryId, t.DisplayOrder, t.Id",
            cmd => { if (categoryId.HasValue) HrmsDatabase.AddParameter(cmd, "@cat", categoryId.Value); },
            Map);
    }

    public static async Task<ReqType?> GetTypeAsync(ApplicationDbContext db, int id)
    {
        var list = await HrmsDatabase.QueryAsync(db,
            "SELECT t.*, c.Name AS CategoryName FROM RequestTypes t JOIN RequestCategories c ON c.Id=t.CategoryId WHERE t.Id=@id",
            cmd => HrmsDatabase.AddParameter(cmd, "@id", id), Map);
        return list.FirstOrDefault();
    }

    private static ReqType Map(DbDataReader r) => new()
    {
        Id = HrmsDatabase.GetInt(r, "Id"),
        CategoryId = HrmsDatabase.GetInt(r, "CategoryId"),
        CategoryName = HrmsDatabase.GetString(r, "CategoryName"),
        Name = HrmsDatabase.GetString(r, "Name"),
        NameEn = HrmsDatabase.GetString(r, "NameEn"),
        AllowedDays = HrmsDatabase.GetNullableInt(r, "AllowedDays"),
        Repeat = HrmsDatabase.GetString(r, "Repeat"),
        ServiceMonths = HrmsDatabase.GetInt(r, "ServiceMonths"),
        Gender = HrmsDatabase.GetString(r, "Gender"),
        PaidMode = HrmsDatabase.GetString(r, "PaidMode"),
        DeductFromSalary = HrmsDatabase.GetBool(r, "DeductFromSalary"),
        CountsInService = HrmsDatabase.GetBool(r, "CountsInService"),
        HasBalance = HrmsDatabase.GetBool(r, "HasBalance"),
        MaxPerRequest = HrmsDatabase.GetNullableInt(r, "MaxPerRequest"),
        AttachmentRequired = HrmsDatabase.GetBool(r, "AttachmentRequired"),
        AttachmentLabel = HrmsDatabase.GetString(r, "AttachmentLabel"),
        NeedsTime = HrmsDatabase.GetBool(r, "NeedsTime"),
        IsActive = HrmsDatabase.GetBool(r, "IsActive"),
        DisplayOrder = HrmsDatabase.GetInt(r, "DisplayOrder")
    };

    public static async Task SaveCategoryAsync(ApplicationDbContext db, Category c)
    {
        if (c.Id > 0)
            await HrmsDatabase.ExecuteAsync(db,
                "UPDATE RequestCategories SET Name=@n, NameEn=@e, DisplayOrder=@o, IsActive=@a, IsPickerEntry=@p WHERE Id=@id",
                cmd => { CatParams(cmd, c); HrmsDatabase.AddParameter(cmd, "@id", c.Id); });
        else
            await HrmsDatabase.ExecuteAsync(db,
                "INSERT INTO RequestCategories(Name,NameEn,DisplayOrder,IsActive,IsPickerEntry) VALUES(@n,@e,@o,@a,@p)",
                cmd => CatParams(cmd, c));
    }

    private static void CatParams(DbCommand cmd, Category c)
    {
        HrmsDatabase.AddParameter(cmd, "@n", c.Name);
        HrmsDatabase.AddParameter(cmd, "@e", (object?)c.NameEn ?? DBNull.Value);
        HrmsDatabase.AddParameter(cmd, "@o", c.DisplayOrder);
        HrmsDatabase.AddParameter(cmd, "@a", c.IsActive);
        HrmsDatabase.AddParameter(cmd, "@p", c.IsPickerEntry);
    }

    public static async Task SaveTypeAsync(ApplicationDbContext db, ReqType t)
    {
        const string cols = "CategoryId=@cat,Name=@n,NameEn=@e,AllowedDays=@days,Repeat=@rep,ServiceMonths=@sm,Gender=@g,PaidMode=@pm,DeductFromSalary=@ded,CountsInService=@cis,HasBalance=@bal,MaxPerRequest=@max,AttachmentRequired=@att,AttachmentLabel=@attl,NeedsTime=@time,IsActive=@a,DisplayOrder=@o";
        if (t.Id > 0)
            await HrmsDatabase.ExecuteAsync(db, $"UPDATE RequestTypes SET {cols} WHERE Id=@id",
                cmd => { TypeParams(cmd, t); HrmsDatabase.AddParameter(cmd, "@id", t.Id); });
        else
            await HrmsDatabase.ExecuteAsync(db,
                "INSERT INTO RequestTypes(CategoryId,Name,NameEn,AllowedDays,Repeat,ServiceMonths,Gender,PaidMode,DeductFromSalary,CountsInService,HasBalance,MaxPerRequest,AttachmentRequired,AttachmentLabel,NeedsTime,IsActive,DisplayOrder) VALUES(@cat,@n,@e,@days,@rep,@sm,@g,@pm,@ded,@cis,@bal,@max,@att,@attl,@time,@a,@o)",
                cmd => TypeParams(cmd, t));
    }

    private static void TypeParams(DbCommand cmd, ReqType t)
    {
        HrmsDatabase.AddParameter(cmd, "@cat", t.CategoryId);
        HrmsDatabase.AddParameter(cmd, "@n", t.Name);
        HrmsDatabase.AddParameter(cmd, "@e", (object?)t.NameEn ?? DBNull.Value);
        HrmsDatabase.AddParameter(cmd, "@days", (object?)t.AllowedDays ?? DBNull.Value);
        HrmsDatabase.AddParameter(cmd, "@rep", t.Repeat);
        HrmsDatabase.AddParameter(cmd, "@sm", t.ServiceMonths);
        HrmsDatabase.AddParameter(cmd, "@g", t.Gender);
        HrmsDatabase.AddParameter(cmd, "@pm", t.PaidMode);
        HrmsDatabase.AddParameter(cmd, "@ded", t.DeductFromSalary);
        HrmsDatabase.AddParameter(cmd, "@cis", t.CountsInService);
        HrmsDatabase.AddParameter(cmd, "@bal", t.HasBalance);
        HrmsDatabase.AddParameter(cmd, "@max", (object?)t.MaxPerRequest ?? DBNull.Value);
        HrmsDatabase.AddParameter(cmd, "@att", t.AttachmentRequired);
        HrmsDatabase.AddParameter(cmd, "@attl", (object?)t.AttachmentLabel ?? DBNull.Value);
        HrmsDatabase.AddParameter(cmd, "@time", t.NeedsTime);
        HrmsDatabase.AddParameter(cmd, "@a", t.IsActive);
        HrmsDatabase.AddParameter(cmd, "@o", t.DisplayOrder);
    }

    public static Task DeleteTypeAsync(ApplicationDbContext db, int id) =>
        HrmsDatabase.ExecuteAsync(db, "DELETE FROM RequestTypes WHERE Id=@id", cmd => HrmsDatabase.AddParameter(cmd, "@id", id));

    public static Task DeleteCategoryAsync(ApplicationDbContext db, int id) =>
        HrmsDatabase.ExecuteAsync(db, "DELETE FROM RequestTypes WHERE CategoryId=@id; DELETE FROM RequestCategories WHERE Id=@id;", cmd => HrmsDatabase.AddParameter(cmd, "@id", id));
}
