using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// Idempotently ensures the onboarding/offboarding checklist tables exist and
/// seeds a sensible default template set (runtime self-healing schema path).
/// </summary>
public static class EmployeeTasksSchema
{
    public static async Task EnsureAsync(ApplicationDbContext dbContext)
    {
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
IF OBJECT_ID('HrTaskTemplates', 'U') IS NULL
BEGIN
    CREATE TABLE HrTaskTemplates
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        ProcessType int NOT NULL,
        Title nvarchar(200) NOT NULL,
        Description nvarchar(500) NULL,
        AssigneeRole nvarchar(100) NULL,
        DueDays int NOT NULL DEFAULT(0),
        SortOrder int NOT NULL DEFAULT(0),
        IsActive bit NOT NULL DEFAULT(1),
        CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CreatedBy nvarchar(150) NULL,
        UpdatedAt datetime2 NULL,
        UpdatedBy nvarchar(150) NULL,
        IsDeleted bit NOT NULL DEFAULT(0)
    );
END;

IF OBJECT_ID('EmployeeTasks', 'U') IS NULL
BEGIN
    CREATE TABLE EmployeeTasks
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EmployeeId int NOT NULL,
        ProcessType int NOT NULL,
        Title nvarchar(200) NOT NULL,
        Description nvarchar(500) NULL,
        AssigneeRole nvarchar(100) NULL,
        DueDate date NULL,
        IsDone bit NOT NULL DEFAULT(0),
        CompletedAt datetime2 NULL,
        CompletedBy nvarchar(150) NULL,
        Note nvarchar(500) NULL,
        CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CreatedBy nvarchar(150) NULL,
        UpdatedAt datetime2 NULL,
        UpdatedBy nvarchar(150) NULL,
        IsDeleted bit NOT NULL DEFAULT(0)
    );
    CREATE INDEX IX_EmployeeTasks_Employee_Process ON EmployeeTasks (EmployeeId, ProcessType);
    CREATE INDEX IX_EmployeeTasks_IsDone ON EmployeeTasks (IsDone);
END;

IF NOT EXISTS (SELECT 1 FROM HrTaskTemplates)
BEGIN
    INSERT INTO HrTaskTemplates (ProcessType, Title, AssigneeRole, DueDays, SortOrder)
    VALUES
        (1, N'توقيع عقد العمل',                N'HR',            0, 10),
        (1, N'استلام الوثائق المطلوبة',        N'HR',            1, 20),
        (1, N'تسجيل بصمة الحضور',              N'HR',            0, 30),
        (1, N'إنشاء حساب البريد والنظام',      N'IT',            1, 40),
        (1, N'تسليم العهدة (جهاز/أدوات)',      N'IT',            2, 50),
        (1, N'التعريف بالفريق والمدير',        N'المدير المباشر', 0, 60),
        (2, N'استلام العهدة',                  N'IT',            0, 10),
        (2, N'إلغاء الحسابات والصلاحيات',      N'IT',            0, 20),
        (2, N'تصفية رصيد الإجازات',            N'HR',            1, 30),
        (2, N'براءة الذمة المالية',            N'المالية',       2, 40),
        (2, N'مقابلة الخروج',                  N'HR',            0, 50);
END;
""");
    }
}
