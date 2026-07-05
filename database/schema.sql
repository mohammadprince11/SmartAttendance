-- NEXORA SmartAttendance clean database objects for Employee Engagement
-- Safe schema only. No real employee data.

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
        IsPublished bit NOT NULL DEFAULT(1),
        PublishDate datetime2 NOT NULL DEFAULT(SYSUTCDATETIME()),
        CreatedBy nvarchar(150) NULL,
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
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
