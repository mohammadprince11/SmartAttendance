using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartAttendance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPeoplePermissionRulesAndScopes_20260715 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Effect",
                table: "SystemUserPermissions",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int?>(
                name: "ScopeBranchId",
                table: "SystemUserPermissions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int?>(
                name: "ScopeCompanyId",
                table: "SystemUserPermissions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int?>(
                name: "ScopeDepartmentId",
                table: "SystemUserPermissions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int?>(
                name: "ScopeEmployeeId",
                table: "SystemUserPermissions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ScopeType",
                table: "SystemUserPermissions",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTime>(
                name: "ValidFromUtc",
                table: "SystemUserPermissions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ValidToUtc",
                table: "SystemUserPermissions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_SystemUserPermissions_Effect",
                table: "SystemUserPermissions",
                sql: "[Effect] IN (1, 2)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SystemUserPermissions_Scope",
                table: "SystemUserPermissions",
                sql: "(([ScopeType] = 1 AND [ScopeCompanyId] IS NULL AND [ScopeBranchId] IS NULL AND [ScopeDepartmentId] IS NULL AND [ScopeEmployeeId] IS NULL) OR ([ScopeType] = 2 AND [ScopeCompanyId] IS NOT NULL AND [ScopeBranchId] IS NULL AND [ScopeDepartmentId] IS NULL AND [ScopeEmployeeId] IS NULL) OR ([ScopeType] = 3 AND [ScopeCompanyId] IS NULL AND [ScopeBranchId] IS NOT NULL AND [ScopeDepartmentId] IS NULL AND [ScopeEmployeeId] IS NULL) OR ([ScopeType] = 4 AND [ScopeCompanyId] IS NULL AND [ScopeBranchId] IS NULL AND [ScopeDepartmentId] IS NOT NULL AND [ScopeEmployeeId] IS NULL) OR ([ScopeType] = 5 AND [ScopeCompanyId] IS NULL AND [ScopeBranchId] IS NULL AND [ScopeDepartmentId] IS NULL AND [ScopeEmployeeId] IS NOT NULL) OR ([ScopeType] = 6 AND [ScopeCompanyId] IS NULL AND [ScopeBranchId] IS NULL AND [ScopeDepartmentId] IS NULL AND [ScopeEmployeeId] IS NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SystemUserPermissions_Validity",
                table: "SystemUserPermissions",
                sql: "[ValidToUtc] IS NULL OR [ValidFromUtc] IS NULL OR [ValidToUtc] > [ValidFromUtc]");

            migrationBuilder.CreateIndex(
                name: "IX_SystemUserPermissions_ScopeBranchId",
                table: "SystemUserPermissions",
                column: "ScopeBranchId");

            migrationBuilder.CreateIndex(
                name: "IX_SystemUserPermissions_ScopeCompanyId",
                table: "SystemUserPermissions",
                column: "ScopeCompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_SystemUserPermissions_ScopeDepartmentId",
                table: "SystemUserPermissions",
                column: "ScopeDepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_SystemUserPermissions_ScopeEmployeeId",
                table: "SystemUserPermissions",
                column: "ScopeEmployeeId");

            migrationBuilder.CreateIndex(
                name: "UX_SystemUserPermissions_EffectiveRule",
                table: "SystemUserPermissions",
                columns: new[]
                {
                    "SystemUserId",
                    "PermissionId",
                    "Effect",
                    "ScopeType",
                    "ScopeCompanyId",
                    "ScopeBranchId",
                    "ScopeDepartmentId",
                    "ScopeEmployeeId",
                    "ValidFromUtc",
                    "ValidToUtc"
                },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.AddForeignKey(
                name: "FK_SystemUserPermissions_Branches_ScopeBranchId",
                table: "SystemUserPermissions",
                column: "ScopeBranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SystemUserPermissions_Companies_ScopeCompanyId",
                table: "SystemUserPermissions",
                column: "ScopeCompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SystemUserPermissions_Departments_ScopeDepartmentId",
                table: "SystemUserPermissions",
                column: "ScopeDepartmentId",
                principalTable: "Departments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SystemUserPermissions_Employees_ScopeEmployeeId",
                table: "SystemUserPermissions",
                column: "ScopeEmployeeId",
                principalTable: "Employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SystemUserPermissions_Branches_ScopeBranchId",
                table: "SystemUserPermissions");

            migrationBuilder.DropForeignKey(
                name: "FK_SystemUserPermissions_Companies_ScopeCompanyId",
                table: "SystemUserPermissions");

            migrationBuilder.DropForeignKey(
                name: "FK_SystemUserPermissions_Departments_ScopeDepartmentId",
                table: "SystemUserPermissions");

            migrationBuilder.DropForeignKey(
                name: "FK_SystemUserPermissions_Employees_ScopeEmployeeId",
                table: "SystemUserPermissions");

            migrationBuilder.DropIndex(
                name: "IX_SystemUserPermissions_ScopeBranchId",
                table: "SystemUserPermissions");

            migrationBuilder.DropIndex(
                name: "IX_SystemUserPermissions_ScopeCompanyId",
                table: "SystemUserPermissions");

            migrationBuilder.DropIndex(
                name: "IX_SystemUserPermissions_ScopeDepartmentId",
                table: "SystemUserPermissions");

            migrationBuilder.DropIndex(
                name: "IX_SystemUserPermissions_ScopeEmployeeId",
                table: "SystemUserPermissions");

            migrationBuilder.DropIndex(
                name: "UX_SystemUserPermissions_EffectiveRule",
                table: "SystemUserPermissions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SystemUserPermissions_Effect",
                table: "SystemUserPermissions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SystemUserPermissions_Scope",
                table: "SystemUserPermissions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SystemUserPermissions_Validity",
                table: "SystemUserPermissions");

            migrationBuilder.DropColumn(
                name: "Effect",
                table: "SystemUserPermissions");

            migrationBuilder.DropColumn(
                name: "ScopeBranchId",
                table: "SystemUserPermissions");

            migrationBuilder.DropColumn(
                name: "ScopeCompanyId",
                table: "SystemUserPermissions");

            migrationBuilder.DropColumn(
                name: "ScopeDepartmentId",
                table: "SystemUserPermissions");

            migrationBuilder.DropColumn(
                name: "ScopeEmployeeId",
                table: "SystemUserPermissions");

            migrationBuilder.DropColumn(
                name: "ScopeType",
                table: "SystemUserPermissions");

            migrationBuilder.DropColumn(
                name: "ValidFromUtc",
                table: "SystemUserPermissions");

            migrationBuilder.DropColumn(
                name: "ValidToUtc",
                table: "SystemUserPermissions");
        }
    }
}
