using System.Data.Common;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Infrastructure.Reports;

/// <summary>
/// Storage for saved people reports (system + user-built + shared), Kayan-style.
/// System reports are seeded once and are just rows — same engine as custom ones.
/// </summary>
public static class PeopleReportsStore
{
    public sealed class SavedReport
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string DatasetKey { get; set; } = string.Empty;
        public string? FilterKey { get; set; }
        public string ColumnsCsv { get; set; } = string.Empty;
        public string? OwnerUser { get; set; }
        public bool IsSystem { get; set; }
        public bool IsShared { get; set; }
        public string? SharedWithCsv { get; set; }
        public string? FilterColumnsCsv { get; set; }
        public int SortOrder { get; set; }

        public List<string> Columns => ColumnsCsv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        public List<string> SharedWith => (SharedWithCsv ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        public List<string> FilterColumns => (FilterColumnsCsv ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    public static async Task EnsureSchemaAsync(ApplicationDbContext db)
    {
        await HrmsDatabase.ExecuteAsync(
            db,
            """
IF OBJECT_ID(N'[dbo].[PeopleReports]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[PeopleReports]
    (
        [Id] int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [Name] nvarchar(200) NOT NULL,
        [DatasetKey] nvarchar(60) NOT NULL,
        [FilterKey] nvarchar(60) NULL,
        [ColumnsCsv] nvarchar(max) NOT NULL,
        [OwnerUser] nvarchar(150) NULL,
        [IsSystem] bit NOT NULL CONSTRAINT DF_PeopleReports_IsSystem DEFAULT 0,
        [IsShared] bit NOT NULL CONSTRAINT DF_PeopleReports_IsShared DEFAULT 0,
        [SortOrder] int NOT NULL CONSTRAINT DF_PeopleReports_SortOrder DEFAULT 0,
        [IsDeleted] bit NOT NULL CONSTRAINT DF_PeopleReports_IsDeleted DEFAULT 0,
        [CreatedAt] datetime2 NOT NULL CONSTRAINT DF_PeopleReports_CreatedAt DEFAULT SYSUTCDATETIME()
    );
END;

IF COL_LENGTH('PeopleReports', 'Description') IS NULL
    ALTER TABLE PeopleReports ADD Description nvarchar(500) NULL;

IF COL_LENGTH('PeopleReports', 'SharedWithCsv') IS NULL
    ALTER TABLE PeopleReports ADD SharedWithCsv nvarchar(max) NULL;

IF COL_LENGTH('PeopleReports', 'FilterColumnsCsv') IS NULL
    ALTER TABLE PeopleReports ADD FilterColumnsCsv nvarchar(max) NULL;

-- مرشحات افتراضية لتقارير النظام (نمط كيان: البحث المتقدم يعتمد مرشحات التقرير المعرّفة)
UPDATE PeopleReports SET FilterColumnsCsv = N'branch,department,position,hiredate,status,active' WHERE IsSystem = 1 AND DatasetKey = N'employees'  AND FilterColumnsCsv IS NULL;
UPDATE PeopleReports SET FilterColumnsCsv = N'relation,nationality,isdependent,isemergency'      WHERE IsSystem = 1 AND DatasetKey = N'dependents' AND FilterColumnsCsv IS NULL;
UPDATE PeopleReports SET FilterColumnsCsv = N'type,country,fromdate,todate,iscurrent'            WHERE IsSystem = 1 AND DatasetKey = N'records'    AND FilterColumnsCsv IS NULL;
UPDATE PeopleReports SET FilterColumnsCsv = N'doctype,expirydate,uploadedat'                     WHERE IsSystem = 1 AND DatasetKey = N'documents'  AND FilterColumnsCsv IS NULL;
UPDATE PeopleReports SET FilterColumnsCsv = N'type,status,fromdate,todate'                       WHERE IsSystem = 1 AND DatasetKey = N'leaves'     AND FilterColumnsCsv IS NULL;
UPDATE PeopleReports SET FilterColumnsCsv = N'category,status,eventdate'                         WHERE IsSystem = 1 AND DatasetKey = N'violations' AND FilterColumnsCsv IS NULL;

IF NOT EXISTS (SELECT 1 FROM PeopleReports WHERE IsSystem = 1)
BEGIN
    INSERT INTO PeopleReports (Name, DatasetKey, FilterKey, ColumnsCsv, IsSystem, SortOrder)
    VALUES
        (N'أسماء الموظفين',                    N'employees',  NULL,          N'no,name', 1, 10),
        (N'معلومات الموظفين الشخصية',          N'employees',  NULL,          N'no,name,gender,birthdate,nationality,maritalstatus,nationalid', 1, 20),
        (N'معلومات العمل للموظفين',            N'employees',  NULL,          N'no,name,branch,department,position,hiredate,contracttype,status,active', 1, 30),
        (N'معلومات الاتصال الخاصة بالموظفين',  N'employees',  NULL,          N'no,name,phone,email', 1, 40),
        (N'الموظفون في فترة التجربة',          N'employees',  N'probation',  N'no,name,branch,department,hiredate,status', 1, 50),
        (N'تقرير الموظفين المنتهية خدماتهم',   N'employees',  N'terminated', N'no,name,branch,department,serviceend,serviceendtype', 1, 60),
        (N'عقود الموظفين وانتهاؤها',           N'employees',  N'contracts',  N'no,name,branch,contracttype,contractend', 1, 70),
        (N'شركاء الموظفين',                    N'dependents', N'spouse',     N'no,employee,name,birthdate,nationality,nationalid,phone', 1, 80),
        (N'أبناء الموظفين',                    N'dependents', N'children',   N'no,employee,name,relation,birthdate,maritalstatus', 1, 90),
        (N'أقرباء الموظفين',                   N'dependents', N'relative',   N'no,employee,name,birthdate,phone', 1, 100),
        (N'جهات الاتصال في حالات الطوارئ',     N'dependents', N'emergency',  N'no,employee,name,relation,phone', 1, 110),
        (N'المعالون',                          N'dependents', N'dependents', N'no,employee,name,relation,birthdate', 1, 120),
        (N'تعليم الموظفين',                    N'records',    N'Education',    N'no,employee,title,subtitle,country,todate', 1, 130),
        (N'خبرات الموظفين',                    N'records',    N'Experience',   N'no,employee,title,subtitle,country,fromdate,todate', 1, 140),
        (N'شهادات الموظفين',                   N'records',    N'Certificate',  N'no,employee,title,refno,fromdate,todate', 1, 150),
        (N'دورات الموظفين التدريبية',          N'records',    N'Training',     N'no,employee,title,fromdate,todate,amount', 1, 160),
        (N'ملفات الموظفين الطبية',             N'records',    N'Medical',      N'no,employee,title,fromdate,attachment', 1, 170),
        (N'عهد الموظفين',                      N'records',    N'Asset',        N'no,employee,title,refno,fromdate,todate,amount,iscurrent', 1, 180),
        (N'عناوين الموظفين',                   N'records',    N'Address',      N'no,employee,title,subtitle,country,iscurrent', 1, 190),
        (N'إقامات الموظفين',                   N'records',    N'Residency',    N'no,employee,title,refno,fromdate,todate', 1, 200),
        (N'وثائق الموظفين',                    N'documents',  NULL,          N'no,employee,doctype,filename,expirydate,uploadedat', 1, 210),
        (N'طلبات الإجازة',                     N'leaves',     NULL,          N'no,employee,type,status,fromdate,todate,days', 1, 220),
        (N'حالات المخالفات',                   N'violations', NULL,          N'refno,no,employee,category,title,eventdate,status,action', 1, 230);
END;

-- تقارير نظام الحضور (مصدر att_*): زرع مستقل idempotent كي تظهر على قواعد بيانات
-- قائمة سبق أن زُرعت بتقارير الأشخاص (كتلة IF NOT EXISTS أعلاه لا تعمل حينها).
IF NOT EXISTS (SELECT 1 FROM PeopleReports WHERE IsSystem = 1 AND DatasetKey IN (N'att_daily', N'att_summary'))
BEGIN
    INSERT INTO PeopleReports (Name, DatasetKey, FilterKey, ColumnsCsv, IsSystem, SortOrder, FilterColumnsCsv)
    VALUES
        (N'الحضور والغياب اليومي',            N'att_daily',   NULL, N'no,name,date,weekday,shift,status,checkin,checkout,late,early,worked', 1, 300, N'date,weekday,shift,status'),
        (N'تفاصيل التأخير والخروج المبكر',    N'att_daily',   NULL, N'no,name,date,shift,status,checkin,checkout,late,early',                1, 310, N'date,status'),
        (N'ملخص الحضور الشهري للموظف',        N'att_summary', NULL, N'no,name,workdays,presentdays,latedays,absentdays,incompletedays,leavedays,holidaydays,latehours,earlyhours,workedhours', 1, 320, N'no,name'),
        (N'ملخص الغياب الشهري',               N'att_summary', NULL, N'no,name,workdays,presentdays,absentdays,leavedays,holidaydays',        1, 330, N'no,name');
END;
""");
    }

    public static async Task<List<SavedReport>> LoadAllAsync(ApplicationDbContext db)
    {
        await EnsureSchemaAsync(db);

        return await HrmsDatabase.QueryAsync(
            db,
            """
SELECT Id, Name, Description, DatasetKey, FilterKey, ColumnsCsv, OwnerUser, IsSystem, IsShared, SharedWithCsv, FilterColumnsCsv, SortOrder
FROM PeopleReports
WHERE IsDeleted = 0
ORDER BY SortOrder, Id;
""",
            command => { },
            Map);
    }

    public static async Task<SavedReport?> GetAsync(ApplicationDbContext db, int id)
    {
        await EnsureSchemaAsync(db);

        var rows = await HrmsDatabase.QueryAsync(
            db,
            """
SELECT Id, Name, Description, DatasetKey, FilterKey, ColumnsCsv, OwnerUser, IsSystem, IsShared, SharedWithCsv, FilterColumnsCsv, SortOrder
FROM PeopleReports
WHERE Id = @Id AND IsDeleted = 0;
""",
            command => HrmsDatabase.AddParameter(command, "@Id", id),
            Map);

        return rows.FirstOrDefault();
    }

    public static async Task CreateAsync(
        ApplicationDbContext db, string name, string? description, string datasetKey, string columnsCsv, string ownerUser, bool isShared,
        string? sharedWithCsv = null, string? filterColumnsCsv = null)
    {
        await HrmsDatabase.ExecuteAsync(
            db,
            """
INSERT INTO PeopleReports (Name, Description, DatasetKey, FilterKey, ColumnsCsv, OwnerUser, IsSystem, IsShared, SharedWithCsv, FilterColumnsCsv, SortOrder)
VALUES (@Name, @Description, @DatasetKey, NULL, @ColumnsCsv, @OwnerUser, 0, @IsShared, @SharedWith, @FilterColumns, 1000);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Name", name);
                HrmsDatabase.AddParameter(command, "@Description", string.IsNullOrWhiteSpace(description) ? DBNull.Value : description.Trim());
                HrmsDatabase.AddParameter(command, "@DatasetKey", datasetKey);
                HrmsDatabase.AddParameter(command, "@ColumnsCsv", columnsCsv);
                HrmsDatabase.AddParameter(command, "@OwnerUser", ownerUser);
                HrmsDatabase.AddParameter(command, "@IsShared", isShared ? 1 : 0);
                HrmsDatabase.AddParameter(command, "@SharedWith", string.IsNullOrWhiteSpace(sharedWithCsv) ? DBNull.Value : sharedWithCsv);
                HrmsDatabase.AddParameter(command, "@FilterColumns", string.IsNullOrWhiteSpace(filterColumnsCsv) ? DBNull.Value : filterColumnsCsv);
            });
    }

    /// <summary>Owner-only update of a custom report (name/columns/sharing/filters).</summary>
    public static async Task UpdateOwnAsync(
        ApplicationDbContext db, int id, string name, string? description, string datasetKey, string columnsCsv, string ownerUser, bool isShared,
        string? sharedWithCsv, string? filterColumnsCsv)
    {
        await HrmsDatabase.ExecuteAsync(
            db,
            """
UPDATE PeopleReports
SET Name = @Name,
    Description = @Description,
    DatasetKey = @DatasetKey,
    ColumnsCsv = @ColumnsCsv,
    IsShared = @IsShared,
    SharedWithCsv = @SharedWith,
    FilterColumnsCsv = @FilterColumns
WHERE Id = @Id AND IsSystem = 0 AND OwnerUser = @Owner AND IsDeleted = 0;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", id);
                HrmsDatabase.AddParameter(command, "@Owner", ownerUser);
                HrmsDatabase.AddParameter(command, "@Name", name);
                HrmsDatabase.AddParameter(command, "@Description", string.IsNullOrWhiteSpace(description) ? DBNull.Value : description.Trim());
                HrmsDatabase.AddParameter(command, "@DatasetKey", datasetKey);
                HrmsDatabase.AddParameter(command, "@ColumnsCsv", columnsCsv);
                HrmsDatabase.AddParameter(command, "@IsShared", isShared ? 1 : 0);
                HrmsDatabase.AddParameter(command, "@SharedWith", string.IsNullOrWhiteSpace(sharedWithCsv) ? DBNull.Value : sharedWithCsv);
                HrmsDatabase.AddParameter(command, "@FilterColumns", string.IsNullOrWhiteSpace(filterColumnsCsv) ? DBNull.Value : filterColumnsCsv);
            });
    }

    public static async Task DeleteOwnAsync(ApplicationDbContext db, int id, string ownerUser)
    {
        await HrmsDatabase.ExecuteAsync(
            db,
            "UPDATE PeopleReports SET IsDeleted = 1 WHERE Id = @Id AND IsSystem = 0 AND OwnerUser = @Owner;",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", id);
                HrmsDatabase.AddParameter(command, "@Owner", ownerUser);
            });
    }

    public static async Task ToggleShareOwnAsync(ApplicationDbContext db, int id, string ownerUser)
    {
        await HrmsDatabase.ExecuteAsync(
            db,
            """
UPDATE PeopleReports
SET IsShared = CASE WHEN IsShared = 1 THEN 0 ELSE 1 END
WHERE Id = @Id AND IsSystem = 0 AND OwnerUser = @Owner;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", id);
                HrmsDatabase.AddParameter(command, "@Owner", ownerUser);
            });
    }

    private static SavedReport Map(DbDataReader reader) => new()
    {
        Id = HrmsDatabase.GetInt(reader, "Id"),
        Name = HrmsDatabase.GetString(reader, "Name"),
        Description = HrmsDatabase.GetString(reader, "Description"),
        DatasetKey = HrmsDatabase.GetString(reader, "DatasetKey"),
        FilterKey = HrmsDatabase.GetString(reader, "FilterKey"),
        ColumnsCsv = HrmsDatabase.GetString(reader, "ColumnsCsv"),
        OwnerUser = HrmsDatabase.GetString(reader, "OwnerUser"),
        IsSystem = HrmsDatabase.GetBool(reader, "IsSystem"),
        IsShared = HrmsDatabase.GetBool(reader, "IsShared"),
        SharedWithCsv = HrmsDatabase.GetString(reader, "SharedWithCsv"),
        FilterColumnsCsv = HrmsDatabase.GetString(reader, "FilterColumnsCsv"),
        SortOrder = HrmsDatabase.GetInt(reader, "SortOrder")
    };
}
