using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartAttendance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RedesignPayrollCutoffPolicies_20260711_122556 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PayrollCutoffPolicies_CompanyId_PolicyType_IsActive",
                table: "PayrollCutoffPolicies");

            migrationBuilder.AddColumn<int>(
                name: "FromDay",
                table: "PayrollCutoffPolicies",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "ToDay",
                table: "PayrollCutoffPolicies",
                type: "int",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.CreateTable(
                name: "PayrollCutoffPolicyTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PayrollCutoffPolicyId = table.Column<int>(type: "int", nullable: false),
                    PolicyType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollCutoffPolicyTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollCutoffPolicyTypes_PayrollCutoffPolicies_PayrollCutoffPolicyId",
                        column: x => x.PayrollCutoffPolicyId,
                        principalTable: "PayrollCutoffPolicies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollCutoffPolicies_CompanyId_IsActive_Name",
                table: "PayrollCutoffPolicies",
                columns: new[] { "CompanyId", "IsActive", "Name" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_PayrollCutoffPolicies_FromDay",
                table: "PayrollCutoffPolicies",
                sql: "[FromDay] BETWEEN 1 AND 31");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PayrollCutoffPolicies_ToDay",
                table: "PayrollCutoffPolicies",
                sql: "[ToDay] BETWEEN 1 AND 31");

            // NEXORA_CUTOFF_REDESIGN_DATA_MIGRATION
            migrationBuilder.Sql(
                """
                UPDATE policy
                SET policy.FromDay = COALESCE(settings.PeriodStartDay, 1),
                    policy.ToDay = COALESCE(settings.PeriodEndDay, 30)
                FROM PayrollCutoffPolicies AS policy
                LEFT JOIN CompanyPayrollSettings AS settings
                    ON settings.CompanyId = policy.CompanyId
                    AND settings.IsDeleted = 0;

                INSERT INTO PayrollCutoffPolicyTypes
                    (PayrollCutoffPolicyId, PolicyType, CreatedAt, CreatedBy, IsDeleted, UpdatedAt, UpdatedBy)
                SELECT
                    policy.Id,
                    policy.PolicyType,
                    SYSUTCDATETIME(),
                    NULL,
                    0,
                    NULL,
                    NULL
                FROM PayrollCutoffPolicies AS policy
                WHERE NOT EXISTS
                (
                    SELECT 1
                    FROM PayrollCutoffPolicyTypes AS policyType
                    WHERE policyType.PayrollCutoffPolicyId = policy.Id
                      AND policyType.PolicyType = policy.PolicyType
                );

                IF OBJECT_ID('tempdb..#NexoraPolicyMerge') IS NOT NULL
                    DROP TABLE #NexoraPolicyMerge;

                ;WITH RankedPolicies AS
                (
                    SELECT
                        policy.Id,
                        MIN(policy.Id) OVER
                        (
                            PARTITION BY
                                policy.CompanyId,
                                policy.Name,
                                policy.FromDay,
                                policy.ToDay,
                                ISNULL(policy.Notes, N''),
                                policy.IsActive,
                                policy.IsDeleted
                        ) AS KeeperId
                    FROM PayrollCutoffPolicies AS policy
                )
                SELECT Id, KeeperId
                INTO #NexoraPolicyMerge
                FROM RankedPolicies;

                UPDATE policyType
                SET policyType.PayrollCutoffPolicyId = mergeMap.KeeperId
                FROM PayrollCutoffPolicyTypes AS policyType
                INNER JOIN #NexoraPolicyMerge AS mergeMap
                    ON mergeMap.Id = policyType.PayrollCutoffPolicyId
                WHERE mergeMap.Id <> mergeMap.KeeperId;

                ;WITH DuplicatePolicyTypes AS
                (
                    SELECT
                        Id,
                        ROW_NUMBER() OVER
                        (
                            PARTITION BY PayrollCutoffPolicyId, PolicyType
                            ORDER BY Id
                        ) AS RowNumber
                    FROM PayrollCutoffPolicyTypes
                )
                DELETE FROM DuplicatePolicyTypes
                WHERE RowNumber > 1;

                DELETE policy
                FROM PayrollCutoffPolicies AS policy
                INNER JOIN #NexoraPolicyMerge AS mergeMap
                    ON mergeMap.Id = policy.Id
                WHERE mergeMap.Id <> mergeMap.KeeperId;

                DROP TABLE #NexoraPolicyMerge;
                """);            migrationBuilder.CreateIndex(
                name: "IX_PayrollCutoffPolicyTypes_PayrollCutoffPolicyId_PolicyType",
                table: "PayrollCutoffPolicyTypes",
                columns: new[] { "PayrollCutoffPolicyId", "PolicyType" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PayrollCutoffPolicyTypes");

            migrationBuilder.DropIndex(
                name: "IX_PayrollCutoffPolicies_CompanyId_IsActive_Name",
                table: "PayrollCutoffPolicies");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PayrollCutoffPolicies_FromDay",
                table: "PayrollCutoffPolicies");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PayrollCutoffPolicies_ToDay",
                table: "PayrollCutoffPolicies");

            migrationBuilder.DropColumn(
                name: "FromDay",
                table: "PayrollCutoffPolicies");

            migrationBuilder.DropColumn(
                name: "ToDay",
                table: "PayrollCutoffPolicies");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollCutoffPolicies_CompanyId_PolicyType_IsActive",
                table: "PayrollCutoffPolicies",
                columns: new[] { "CompanyId", "PolicyType", "IsActive" });
        }
    }
}
