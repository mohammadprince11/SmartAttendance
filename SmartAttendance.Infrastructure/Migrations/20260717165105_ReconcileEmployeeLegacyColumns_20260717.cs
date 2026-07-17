using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartAttendance.Infrastructure.Migrations
{
    /// <summary>
    /// Reconciles the EF model with legacy Employees columns that were added
    /// at runtime by guarded ALTER scripts (HrmsDatabase / EmployeeLifecycleSchema).
    /// On databases where those scripts already ran this migration is a no-op;
    /// on a fresh database it creates the same columns.
    /// </summary>
    public partial class ReconcileEmployeeLegacyColumns_20260717 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
IF COL_LENGTH('Employees', 'PhotoPath') IS NULL
    ALTER TABLE Employees ADD PhotoPath nvarchar(500) NULL;

IF COL_LENGTH('Employees', 'ContractType') IS NULL
    ALTER TABLE Employees ADD ContractType nvarchar(100) NULL;

IF COL_LENGTH('Employees', 'ContractEndDate') IS NULL
    ALTER TABLE Employees ADD ContractEndDate date NULL;

IF COL_LENGTH('Employees', 'EmploymentStatus') IS NULL
    ALTER TABLE Employees ADD EmploymentStatus nvarchar(100) NULL;

IF COL_LENGTH('Employees', 'DirectManagerId') IS NULL
    ALTER TABLE Employees ADD DirectManagerId int NULL;

IF COL_LENGTH('Employees', 'ServiceEndDate') IS NULL
    ALTER TABLE Employees ADD ServiceEndDate date NULL;

IF COL_LENGTH('Employees', 'ServiceEndType') IS NULL
    ALTER TABLE Employees ADD ServiceEndType nvarchar(80) NULL;

IF COL_LENGTH('Employees', 'ServiceEndReason') IS NULL
    ALTER TABLE Employees ADD ServiceEndReason nvarchar(1000) NULL;

IF COL_LENGTH('Employees', 'ServiceEndNotes') IS NULL
    ALTER TABLE Employees ADD ServiceEndNotes nvarchar(2000) NULL;

IF COL_LENGTH('Employees', 'ClearanceStatus') IS NULL
    ALTER TABLE Employees ADD ClearanceStatus nvarchar(80) NULL;

IF COL_LENGTH('Employees', 'LastRehireDate') IS NULL
    ALTER TABLE Employees ADD LastRehireDate date NULL;

IF COL_LENGTH('Employees', 'RehireReason') IS NULL
    ALTER TABLE Employees ADD RehireReason nvarchar(1000) NULL;

IF COL_LENGTH('Employees', 'RehireNotes') IS NULL
    ALTER TABLE Employees ADD RehireNotes nvarchar(2000) NULL;

IF COL_LENGTH('Employees', 'RehireCount') IS NULL
    ALTER TABLE Employees ADD RehireCount int NOT NULL CONSTRAINT DF_Employees_RehireCount DEFAULT(0);
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally left empty: these columns are shared with legacy
            // runtime scripts that would recreate them on next startup anyway.
        }
    }
}
