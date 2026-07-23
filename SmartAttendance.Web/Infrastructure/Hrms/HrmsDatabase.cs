using System.Data;
using System.Data.Common;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

public static class HrmsDatabase
{
    public static async Task EnsureCreatedAsync(ApplicationDbContext dbContext)
    {
        var sql = """
IF COL_LENGTH('Employees', 'Position') IS NULL
    ALTER TABLE Employees ADD Position nvarchar(150) NULL;

IF COL_LENGTH('Employees', 'PhotoPath') IS NULL
    ALTER TABLE Employees ADD PhotoPath nvarchar(500) NULL;

IF COL_LENGTH('Employees', 'Gender') IS NULL
    ALTER TABLE Employees ADD Gender nvarchar(30) NULL;

IF COL_LENGTH('Employees', 'Nationality') IS NULL
    ALTER TABLE Employees ADD Nationality nvarchar(100) NULL;

IF COL_LENGTH('Employees', 'Country') IS NULL
    ALTER TABLE Employees ADD Country nvarchar(100) NULL;

IF COL_LENGTH('Employees', 'ContractType') IS NULL
    ALTER TABLE Employees ADD ContractType nvarchar(100) NULL;

IF COL_LENGTH('Employees', 'ContractEndDate') IS NULL
    ALTER TABLE Employees ADD ContractEndDate date NULL;

IF COL_LENGTH('Employees', 'EmploymentStatus') IS NULL
    ALTER TABLE Employees ADD EmploymentStatus nvarchar(100) NULL;

IF COL_LENGTH('Employees', 'DirectManagerId') IS NULL
    ALTER TABLE Employees ADD DirectManagerId int NULL;

IF COL_LENGTH('Employees', 'FirstName') IS NULL
    ALTER TABLE Employees ADD FirstName nvarchar(100) NULL;

IF COL_LENGTH('Employees', 'SecondName') IS NULL
    ALTER TABLE Employees ADD SecondName nvarchar(100) NULL;

IF COL_LENGTH('Employees', 'ThirdName') IS NULL
    ALTER TABLE Employees ADD ThirdName nvarchar(100) NULL;

IF COL_LENGTH('Employees', 'LastName') IS NULL
    ALTER TABLE Employees ADD LastName nvarchar(100) NULL;

IF COL_LENGTH('Employees', 'FirstNameEn') IS NULL
    ALTER TABLE Employees ADD FirstNameEn nvarchar(100) NULL;

IF COL_LENGTH('Employees', 'SecondNameEn') IS NULL
    ALTER TABLE Employees ADD SecondNameEn nvarchar(100) NULL;

IF COL_LENGTH('Employees', 'ThirdNameEn') IS NULL
    ALTER TABLE Employees ADD ThirdNameEn nvarchar(100) NULL;

IF COL_LENGTH('Employees', 'LastNameEn') IS NULL
    ALTER TABLE Employees ADD LastNameEn nvarchar(100) NULL;

IF COL_LENGTH('Employees', 'IsCitizen') IS NULL
    ALTER TABLE Employees ADD IsCitizen bit NOT NULL CONSTRAINT DF_Employees_IsCitizen DEFAULT(1);

IF COL_LENGTH('Employees', 'PassportNo') IS NULL
    ALTER TABLE Employees ADD PassportNo nvarchar(50) NULL;

IF COL_LENGTH('Employees', 'SponsorName') IS NULL
    ALTER TABLE Employees ADD SponsorName nvarchar(150) NULL;

IF COL_LENGTH('Employees', 'Religion') IS NULL
    ALTER TABLE Employees ADD Religion nvarchar(50) NULL;

IF COL_LENGTH('Employees', 'PersonalEmail') IS NULL
    ALTER TABLE Employees ADD PersonalEmail nvarchar(200) NULL;

IF COL_LENGTH('Employees', 'MotherCountry') IS NULL
    ALTER TABLE Employees ADD MotherCountry nvarchar(100) NULL;

IF COL_LENGTH('Employees', 'MotherCity') IS NULL
    ALTER TABLE Employees ADD MotherCity nvarchar(100) NULL;

IF COL_LENGTH('Employees', 'PhoneExtension') IS NULL
    ALTER TABLE Employees ADD PhoneExtension nvarchar(20) NULL;

IF COL_LENGTH('Employees', 'JoiningDate') IS NULL
    ALTER TABLE Employees ADD JoiningDate date NULL;

IF COL_LENGTH('Employees', 'WorkType') IS NULL
    ALTER TABLE Employees ADD WorkType nvarchar(50) NULL;

IF COL_LENGTH('Employees', 'JobGrade') IS NULL
    ALTER TABLE Employees ADD JobGrade nvarchar(100) NULL;

IF OBJECT_ID('SelfServiceRequests', 'U') IS NULL
BEGIN
    CREATE TABLE SelfServiceRequests
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EmployeeId int NOT NULL,
        RequestType nvarchar(50) NOT NULL,
        RequestDate date NULL,
        FromDate date NULL,
        ToDate date NULL,
        StartTime time NULL,
        EndTime time NULL,
        Reason nvarchar(max) NULL,
        Status nvarchar(30) NOT NULL DEFAULT('Pending'),
        CurrentStep nvarchar(80) NULL,
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME()),
        CreatedBy nvarchar(150) NULL,
        UpdatedAt datetime2 NULL,
        ReviewedBy nvarchar(150) NULL,
        ReviewNote nvarchar(max) NULL
    );
END;

IF COL_LENGTH('SelfServiceRequests', 'ManagerStatus') IS NULL
    ALTER TABLE SelfServiceRequests ADD ManagerStatus nvarchar(30) NULL;

IF COL_LENGTH('SelfServiceRequests', 'ManagerReviewedBy') IS NULL
    ALTER TABLE SelfServiceRequests ADD ManagerReviewedBy nvarchar(150) NULL;

IF COL_LENGTH('SelfServiceRequests', 'ManagerReviewedAt') IS NULL
    ALTER TABLE SelfServiceRequests ADD ManagerReviewedAt datetime2 NULL;

IF COL_LENGTH('SelfServiceRequests', 'ManagerNote') IS NULL
    ALTER TABLE SelfServiceRequests ADD ManagerNote nvarchar(max) NULL;

IF COL_LENGTH('SelfServiceRequests', 'HrStatus') IS NULL
    ALTER TABLE SelfServiceRequests ADD HrStatus nvarchar(30) NULL;

IF COL_LENGTH('SelfServiceRequests', 'HrReviewedBy') IS NULL
    ALTER TABLE SelfServiceRequests ADD HrReviewedBy nvarchar(150) NULL;

IF COL_LENGTH('SelfServiceRequests', 'HrReviewedAt') IS NULL
    ALTER TABLE SelfServiceRequests ADD HrReviewedAt datetime2 NULL;

IF COL_LENGTH('SelfServiceRequests', 'HrNote') IS NULL
    ALTER TABLE SelfServiceRequests ADD HrNote nvarchar(max) NULL;

-- دلالة زوج البصمة (نمط كيان — قسم 29.د بدراسة الحضور): كل صف في
-- AttendanceRecords هو زوج دخول/خروج، وكيان يصنّف الأزواج بنوع بصمة
-- (حضور · استراحة · صلاة · مهمة عمل...). NULL = حضور، فالبيانات القائمة
-- تبقى على معناها. الأزواج غير-الحضورية تُستثنى من اشتقاق اليومية حتى لا
-- تصبح استراحةٌ «آخر خروج».
IF COL_LENGTH('AttendanceRecords', 'PunchSemanticId') IS NULL
    ALTER TABLE AttendanceRecords ADD PunchSemanticId int NULL;

-- تطبيع الاصطلاح: زوج الحضور دلالته NULL دائماً، وما عداه بصمة أخرى. يسمح
-- للمسار القديم (AttendanceProcessingService بـEF) أن يفرّقها بشرط بسيط بلا
-- معرفة مُعرّف الدلالة النظامية. الشرط EXISTS يمنع تحديث الجدول بلا داعٍ.
IF OBJECT_ID('PunchSemantics', 'U') IS NOT NULL
   AND EXISTS (SELECT 1 FROM AttendanceRecords ar
               INNER JOIN PunchSemantics ps ON ps.Id = ar.PunchSemanticId
               WHERE ps.IsSystem = 1)
BEGIN
    UPDATE ar SET ar.PunchSemanticId = NULL
    FROM AttendanceRecords ar
    INNER JOIN PunchSemantics ps ON ps.Id = ar.PunchSemanticId
    WHERE ps.IsSystem = 1;
END;

IF OBJECT_ID('ApprovalHistories', 'U') IS NULL
BEGIN
    CREATE TABLE ApprovalHistories
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        RequestId int NOT NULL,
        StepName nvarchar(80) NOT NULL,
        Action nvarchar(30) NOT NULL,
        ActionBy nvarchar(150) NULL,
        ActionAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME()),
        Notes nvarchar(max) NULL
    );
END;

IF OBJECT_ID('EmployeeDocuments', 'U') IS NULL
BEGIN
    CREATE TABLE EmployeeDocuments
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EmployeeId int NOT NULL,
        DocumentType nvarchar(100) NOT NULL,
        FileName nvarchar(260) NOT NULL,
        StoredPath nvarchar(500) NOT NULL,
        ExpiryDate date NULL,
        Notes nvarchar(max) NULL,
        UploadedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME()),
        UploadedBy nvarchar(150) NULL
    );
END;

IF OBJECT_ID('AuditLogs', 'U') IS NULL
BEGIN
    CREATE TABLE AuditLogs
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EntityName nvarchar(120) NOT NULL,
        EntityId nvarchar(80) NULL,
        Action nvarchar(80) NOT NULL,
        OldValues nvarchar(max) NULL,
        NewValues nvarchar(max) NULL,
        UserName nvarchar(150) NULL,
        IpAddress nvarchar(80) NULL,
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;

IF OBJECT_ID('SystemNotifications', 'U') IS NULL
BEGIN
    CREATE TABLE SystemNotifications
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Title nvarchar(250) NOT NULL,
        Message nvarchar(max) NULL,
        TargetRole nvarchar(100) NULL,
        TargetUser nvarchar(150) NULL,
        Url nvarchar(500) NULL,
        IsRead bit NOT NULL DEFAULT(0),
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;
""";

        await ExecuteAsync(dbContext, sql);
    }

    public static async Task ExecuteAsync(ApplicationDbContext dbContext, string sql, Action<DbCommand>? configure = null)
    {
        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;

        if (shouldClose)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
            configure?.Invoke(command);
            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            if (shouldClose && dbContext.Database.CurrentTransaction == null)
            {
                await connection.CloseAsync();
            }
        }
    }

    public static async Task<List<T>> QueryAsync<T>(
        ApplicationDbContext dbContext,
        string sql,
        Action<DbCommand>? configure,
        Func<DbDataReader, T> map)
    {
        var result = new List<T>();
        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;

        if (shouldClose)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
            configure?.Invoke(command);

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                result.Add(map(reader));
            }

            return result;
        }
        finally
        {
            if (shouldClose && dbContext.Database.CurrentTransaction == null)
            {
                await connection.CloseAsync();
            }
        }
    }

    public static async Task<T?> ScalarAsync<T>(ApplicationDbContext dbContext, string sql, Action<DbCommand>? configure = null)
    {
        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;

        if (shouldClose)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
            configure?.Invoke(command);
            var value = await command.ExecuteScalarAsync();

            if (value == null || value == DBNull.Value)
            {
                return default;
            }

            return (T)Convert.ChangeType(value, typeof(T));
        }
        finally
        {
            if (shouldClose && dbContext.Database.CurrentTransaction == null)
            {
                await connection.CloseAsync();
            }
        }
    }

    public static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    public static int GetInt(DbDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal));
    }

    public static int? GetNullableInt(DbDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : Convert.ToInt32(reader.GetValue(ordinal));
    }

    public static string GetString(DbDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? string.Empty : Convert.ToString(reader.GetValue(ordinal)) ?? string.Empty;
    }

    public static DateTime? GetDateTime(DbDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : Convert.ToDateTime(reader.GetValue(ordinal));
    }

    public static DateOnly? GetDateOnly(DbDataReader reader, string name)
    {
        var value = GetDateTime(reader, name);
        return value.HasValue ? DateOnly.FromDateTime(value.Value) : null;
    }

    public static bool GetBool(DbDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return !reader.IsDBNull(ordinal) && Convert.ToBoolean(reader.GetValue(ordinal));
    }

    public static string JsonLine(params (string Name, object? Value)[] values)
    {
        var builder = new StringBuilder();

        foreach (var (name, value) in values)
        {
            if (builder.Length > 0)
            {
                builder.Append(" | ");
            }

            builder.Append(name).Append(": ").Append(value ?? "");
        }

        return builder.ToString();
    }
}
