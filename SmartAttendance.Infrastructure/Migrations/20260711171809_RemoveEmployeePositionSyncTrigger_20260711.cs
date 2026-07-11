using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartAttendance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveEmployeePositionSyncTrigger_20260711 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
IF OBJECT_ID(N'dbo.TR_Employees_SyncPositionId', N'TR') IS NOT NULL
    DROP TRIGGER dbo.TR_Employees_SyncPositionId;
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
EXEC(N'
CREATE OR ALTER TRIGGER dbo.TR_Employees_SyncPositionId
ON dbo.Employees
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF TRIGGER_NESTLEVEL() > 1
        RETURN;

    UPDATE employee
    SET PositionId = matchedPosition.Id
    FROM dbo.Employees employee
    INNER JOIN inserted source
        ON source.Id = employee.Id
    INNER JOIN dbo.Branches branch
        ON branch.Id = source.BranchId
    OUTER APPLY
    (
        SELECT TOP (1) position.Id
        FROM dbo.HrJobPositions position
        WHERE position.CompanyId = branch.CompanyId
          AND position.IsActive = 1
          AND LTRIM(RTRIM(position.ArabicName)) =
              LTRIM(RTRIM(CONVERT(NVARCHAR(MAX), source.Position)))
        ORDER BY position.Id
    ) matchedPosition
    WHERE employee.Id = source.Id;
END;
');
""");
        }
    }
}
