using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

public static class EmployeeEngagementSchema
{
    public static async Task EnsureAsync(ApplicationDbContext dbContext)
    {
        await HrmsDatabase.EnsureCreatedAsync(dbContext);

        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
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
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
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

IF OBJECT_ID('SelfServiceRequests', 'U') IS NULL
BEGIN
    CREATE TABLE SelfServiceRequests
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EmployeeId int NOT NULL,
        RequestType nvarchar(80) NOT NULL,
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME()),
        FromDate datetime2 NULL,
        ToDate datetime2 NULL,
        Reason nvarchar(max) NULL,
        Status nvarchar(50) NOT NULL DEFAULT('Pending')
    );
END;

IF OBJECT_ID('AttendanceRecords', 'U') IS NULL
BEGIN
    CREATE TABLE AttendanceRecords
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EmployeeId int NOT NULL,
        AttendanceDate datetime2 NOT NULL,
        CheckIn datetime2 NULL,
        CheckOut datetime2 NULL,
        Status nvarchar(50) NULL,
        Source nvarchar(50) NULL,
        Notes nvarchar(max) NULL
    );
END;
""");
    }
}