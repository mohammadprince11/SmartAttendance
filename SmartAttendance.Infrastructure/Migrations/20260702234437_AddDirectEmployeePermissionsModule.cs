using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartAttendance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDirectEmployeePermissionsModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EmployeeId",
                table: "SystemUsers",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SystemUsers_EmployeeId",
                table: "SystemUsers",
                column: "EmployeeId",
                unique: true,
                filter: "[EmployeeId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_SystemUsers_Employees_EmployeeId",
                table: "SystemUsers",
                column: "EmployeeId",
                principalTable: "Employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SystemUsers_Employees_EmployeeId",
                table: "SystemUsers");

            migrationBuilder.DropIndex(
                name: "IX_SystemUsers_EmployeeId",
                table: "SystemUsers");

            migrationBuilder.DropColumn(
                name: "EmployeeId",
                table: "SystemUsers");
        }
    }
}
