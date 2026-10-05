using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

public static class EmployeeEngagementSchema
{
    // يُستهلك حصراً من SqlSchemaMigrator. إبقاء تعريف الجداول في موضع واحد يمنع
    // اختلاف الهجرة المحكومة عن التحقق الذي تجريه مسارات القراءة والكتابة.
    public const string MigrationSql = """
IF OBJECT_ID('EmployeePortalAnnouncements', 'U') IS NULL
BEGIN
    CREATE TABLE EmployeePortalAnnouncements
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Title nvarchar(250) NOT NULL,
        Body nvarchar(max) NULL,
        Category nvarchar(80) NULL,
        TargetType nvarchar(50) NULL,
        TargetValue nvarchar(max) NULL,
        TemplateKey nvarchar(80) NULL,
        IsPublished bit NOT NULL DEFAULT(1),
        PublishDate datetime2 NOT NULL DEFAULT(SYSUTCDATETIME()),
        CreatedBy nvarchar(150) NULL,
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;

IF COL_LENGTH('EmployeePortalAnnouncements', 'TargetValue') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('EmployeePortalAnnouncements') AND name = 'TargetValue' AND max_length > 0 AND max_length < 1000)
        ALTER TABLE EmployeePortalAnnouncements ALTER COLUMN TargetValue nvarchar(max) NULL;
END;

IF COL_LENGTH('EmployeePortalAnnouncements', 'TemplateKey') IS NULL
BEGIN
    ALTER TABLE EmployeePortalAnnouncements ADD TemplateKey nvarchar(80) NULL;
END;

IF OBJECT_ID('EmployeeFeedbackItems', 'U') IS NULL
BEGIN
    CREATE TABLE EmployeeFeedbackItems
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EmployeeId int NOT NULL,
        Type nvarchar(50) NOT NULL,
        Title nvarchar(250) NOT NULL,
        Message nvarchar(max) NULL,
        Priority nvarchar(50) NULL,
        Status nvarchar(50) NOT NULL DEFAULT('Open'),
        AdminReply nvarchar(max) NULL,
        RepliedBy nvarchar(150) NULL,
        RepliedAt datetime2 NULL,
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;

IF OBJECT_ID('EmployeeCompensations', 'U') IS NULL
BEGIN
    CREATE TABLE EmployeeCompensations
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EmployeeId int NOT NULL,
        BasicSalary decimal(18,2) NULL,
        Allowances decimal(18,2) NULL,
        Deductions decimal(18,2) NULL,
        PaymentMethod nvarchar(80) NULL,
        BankName nvarchar(150) NULL,
        BankAccount nvarchar(150) NULL,
        Currency nvarchar(30) NULL,
        UpdatedAt datetime2 NULL
    );
END;

IF OBJECT_ID('EmployeePolls', 'U') IS NULL
BEGIN
    CREATE TABLE EmployeePolls
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Title nvarchar(250) NOT NULL,
        Question nvarchar(max) NULL,
        Category nvarchar(80) NULL,
        TargetType nvarchar(50) NULL,
        TargetValue nvarchar(max) NULL,
        IsPublished bit NOT NULL DEFAULT(1),
        PublishDate datetime2 NOT NULL DEFAULT(SYSUTCDATETIME()),
        CreatedBy nvarchar(150) NULL,
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME()),
        -- مطابقة لهجرة 20260811-01 لا عموداً جديداً بالشفاء الذاتي: تلك الهجرة
        -- تُضيف العمود للجداول **القائمة** فقط. لو أنشأ هذا التعريف الجدول بعدها
        -- (قاعدة بلا استطلاعات) لخرج بلا CompanyId وانهار كل استعلام محصور بـ
        -- «Invalid column name». التعريفان يجب أن يتطابقا.
        CompanyId int NULL
    );
END;

IF OBJECT_ID('EmployeePollOptions', 'U') IS NULL
BEGIN
    CREATE TABLE EmployeePollOptions
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        PollId int NOT NULL,
        OptionText nvarchar(300) NOT NULL,
        DisplayOrder int NOT NULL DEFAULT(1)
    );
END;

IF OBJECT_ID('EmployeePollVotes', 'U') IS NULL
BEGIN
    CREATE TABLE EmployeePollVotes
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        PollId int NOT NULL,
        OptionId int NOT NULL,
        EmployeeId int NOT NULL,
        VotedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_EmployeePollVotes_PollEmployee' AND object_id = OBJECT_ID('EmployeePollVotes'))
BEGIN
    CREATE UNIQUE INDEX UX_EmployeePollVotes_PollEmployee ON EmployeePollVotes(PollId, EmployeeId);
END;

""";

    /// <summary>
    /// يتحقق من أن الهجرة المحكومة طُبقت. لا ينشئ أو يعدل مخططاً أثناء الطلب.
    /// </summary>
    public static Task EnsureAsync(ApplicationDbContext dbContext) =>
        HrmsDatabase.ExecuteAsync(
            dbContext,
            """
IF OBJECT_ID('EmployeePortalAnnouncements', 'U') IS NULL
   OR OBJECT_ID('EmployeeFeedbackItems', 'U') IS NULL
   OR OBJECT_ID('EmployeeCompensations', 'U') IS NULL
   OR OBJECT_ID('EmployeePolls', 'U') IS NULL
   OR OBJECT_ID('EmployeePollOptions', 'U') IS NULL
   OR OBJECT_ID('EmployeePollVotes', 'U') IS NULL
    THROW 51000, 'Employee engagement schema is missing. Run the controlled database migrator.', 1;
""");
}
