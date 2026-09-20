using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// Kayan-style auto-reference schema for the employee code (Ù‚Ø³Ù… 18.2 Ø¨Ø§Ù„Ø¯Ø±Ø§Ø³Ø©):
/// segments = [fixed prefix] + [sequential number padded to N digits].
/// Single active schema row; generation atomically increments LastNumber so two
/// concurrent creates never get the same code. Self-healing table, seeded disabled
/// so existing numbering habits are not hijacked until HR opts in.
/// </summary>
public static class EmployeeCodeSchema
{
    public sealed class SchemaRow
    {
        public int Id { get; set; }
        public string Prefix { get; set; } = string.Empty;
        public int Digits { get; set; }
        public int LastNumber { get; set; }
        public bool IsActive { get; set; }
    }

    public static async Task EnsureAsync(ApplicationDbContext dbContext)
    {
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
IF OBJECT_ID('dbo.EmployeeCodeSchemas', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.EmployeeCodeSchemas
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Prefix nvarchar(20) NOT NULL DEFAULT(N''),
        Digits int NOT NULL DEFAULT(5),
        LastNumber int NOT NULL DEFAULT(0),
        IsActive bit NOT NULL DEFAULT(0)
    );
END;

-- Self-heal an existing-but-empty table. Some deployments can have
-- the table present without the original seed row.
IF NOT EXISTS (SELECT 1 FROM dbo.EmployeeCodeSchemas)
BEGIN
    INSERT INTO dbo.EmployeeCodeSchemas
        (Prefix, Digits, LastNumber, IsActive)
    VALUES
        (N'EMP-', 5, 0, 0);
END;
""");
    }

    public static async Task<SchemaRow?> GetAsync(ApplicationDbContext dbContext)
    {
        await EnsureAsync(dbContext);
        var rows = await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT TOP 1 Id, Prefix, Digits, LastNumber, IsActive FROM dbo.EmployeeCodeSchemas ORDER BY Id;",
            command => { },
            reader => new SchemaRow
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                Prefix = HrmsDatabase.GetString(reader, "Prefix") ?? string.Empty,
                Digits = HrmsDatabase.GetInt(reader, "Digits"),
                LastNumber = HrmsDatabase.GetInt(reader, "LastNumber"),
                IsActive = HrmsDatabase.GetBool(reader, "IsActive")
            });
        return rows.FirstOrDefault();
    }

    public static async Task SaveAsync(
        ApplicationDbContext dbContext,
        string prefix,
        int digits,
        int lastNumber,
        bool isActive)
    {
        await EnsureAsync(dbContext);

        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
DECLARE @SchemaId int =
(
    SELECT MIN(Id)
    FROM dbo.EmployeeCodeSchemas
);

IF @SchemaId IS NULL
BEGIN
    INSERT INTO dbo.EmployeeCodeSchemas
        (Prefix, Digits, LastNumber, IsActive)
    VALUES
        (@Prefix, @Digits, @LastNumber, @IsActive);
END
ELSE
BEGIN
    UPDATE dbo.EmployeeCodeSchemas
    SET Prefix = @Prefix,
        Digits = @Digits,
        LastNumber = @LastNumber,
        IsActive = @IsActive
    WHERE Id = @SchemaId;

    -- This feature intentionally uses one canonical schema.
    -- Any accidental extra active rows must not generate codes.
    UPDATE dbo.EmployeeCodeSchemas
    SET IsActive = 0
    WHERE Id <> @SchemaId
      AND IsActive = 1;
END;
""",
            command =>
            {
                HrmsDatabase.AddParameter(
                    command,
                    "@Prefix",
                    prefix ?? string.Empty);
                HrmsDatabase.AddParameter(
                    command,
                    "@Digits",
                    Math.Clamp(digits, 1, 12));
                HrmsDatabase.AddParameter(
                    command,
                    "@LastNumber",
                    Math.Max(0, lastNumber));
                HrmsDatabase.AddParameter(
                    command,
                    "@IsActive",
                    isActive ? 1 : 0);
            });
    }

    /// <summary>ÙŠÙˆÙ„Ù‘Ø¯ Ø§Ù„Ø±Ù…Ø² Ø§Ù„ØªØ§Ù„ÙŠ ÙˆÙŠØ²ÙŠØ¯ Ø§Ù„ØªØ³Ù„Ø³Ù„ Ø°Ø±Ù‘ÙŠØ§Ù‹Ø› null Ø¥Ø°Ø§ Ø§Ù„Ù…Ø®Ø·Ø· ØºÙŠØ± Ù…ÙØ¹Ù‘Ù„.</summary>
    public static async Task<string?> GenerateNextAsync(
        ApplicationDbContext dbContext)
    {
        await EnsureAsync(dbContext);

        var rows = await HrmsDatabase.QueryAsync(
            dbContext,
            """
DECLARE @SchemaId int =
(
    SELECT MIN(Id)
    FROM dbo.EmployeeCodeSchemas
    WHERE IsActive = 1
);

UPDATE dbo.EmployeeCodeSchemas
SET LastNumber = LastNumber + 1
OUTPUT inserted.Prefix, inserted.Digits, inserted.LastNumber
WHERE Id = @SchemaId
  AND IsActive = 1;
""",
            command => { },
            reader => new
            {
                Prefix =
                    HrmsDatabase.GetString(reader, "Prefix") ??
                    string.Empty,
                Digits =
                    HrmsDatabase.GetInt(reader, "Digits"),
                Number =
                    HrmsDatabase.GetInt(reader, "LastNumber")
            });

        var row = rows.FirstOrDefault();

        return row == null
            ? null
            : row.Prefix +
              row.Number.ToString(
                  new string(
                      '0',
                      Math.Clamp(row.Digits, 1, 12)));
    }
}
