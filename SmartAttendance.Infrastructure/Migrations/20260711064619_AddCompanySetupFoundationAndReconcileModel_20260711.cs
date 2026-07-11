using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartAttendance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanySetupFoundationAndReconcileModel_20260711 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Departments_Branches_BranchId",
                table: "Departments");

            migrationBuilder.DropIndex(
                name: "IX_Departments_BranchId_Code",
                table: "Departments");

            migrationBuilder.AlterColumn<int>(
                name: "BranchId",
                table: "Departments",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<string>(
                name: "CountryCode",
                table: "Companies",
                type: "nvarchar(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                table: "Companies",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "Companies",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CompanyPayrollSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    PayrollFrequency = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PeriodStartDay = table.Column<int>(type: "int", nullable: false),
                    PeriodEndDay = table.Column<int>(type: "int", nullable: false),
                    PaymentDay = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyPayrollSettings", x => x.Id);
                    table.CheckConstraint("CK_CompanyPayrollSettings_PaymentDay", "[PaymentDay] IS NULL OR [PaymentDay] BETWEEN 1 AND 31");
                    table.CheckConstraint("CK_CompanyPayrollSettings_PeriodEndDay", "[PeriodEndDay] BETWEEN 1 AND 31");
                    table.CheckConstraint("CK_CompanyPayrollSettings_PeriodStartDay", "[PeriodStartDay] BETWEEN 1 AND 31");
                    table.ForeignKey(
                        name: "FK_CompanyPayrollSettings_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PayrollCutoffPolicies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    PolicyType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CutoffBasis = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DayOfMonth = table.Column<int>(type: "int", nullable: true),
                    OffsetDays = table.Column<int>(type: "int", nullable: true),
                    CutoffTime = table.Column<TimeOnly>(type: "time", nullable: true),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollCutoffPolicies", x => x.Id);
                    table.CheckConstraint("CK_PayrollCutoffPolicies_DayOfMonth", "[DayOfMonth] IS NULL OR [DayOfMonth] BETWEEN 1 AND 31");
                    table.CheckConstraint("CK_PayrollCutoffPolicies_EffectiveDates", "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]");
                    table.CheckConstraint("CK_PayrollCutoffPolicies_OffsetDays", "[OffsetDays] IS NULL OR [OffsetDays] >= 0");
                    table.ForeignKey(
                        name: "FK_PayrollCutoffPolicies_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Departments_BranchId",
                table: "Departments",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyPayrollSettings_CompanyId",
                table: "CompanyPayrollSettings",
                column: "CompanyId",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollCutoffPolicies_CompanyId_PolicyType_IsActive",
                table: "PayrollCutoffPolicies",
                columns: new[] { "CompanyId", "PolicyType", "IsActive" });

            migrationBuilder.AddForeignKey(
                name: "FK_Departments_Branches_BranchId",
                table: "Departments",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Departments_Branches_BranchId",
                table: "Departments");

            migrationBuilder.DropTable(
                name: "CompanyPayrollSettings");

            migrationBuilder.DropTable(
                name: "PayrollCutoffPolicies");

            migrationBuilder.DropIndex(
                name: "IX_Departments_BranchId",
                table: "Departments");

            migrationBuilder.DropColumn(
                name: "CountryCode",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "Companies");

            migrationBuilder.AlterColumn<int>(
                name: "BranchId",
                table: "Departments",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Departments_BranchId_Code",
                table: "Departments",
                columns: new[] { "BranchId", "Code" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Departments_Branches_BranchId",
                table: "Departments",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
