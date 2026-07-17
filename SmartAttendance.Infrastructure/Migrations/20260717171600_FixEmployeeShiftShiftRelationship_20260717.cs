using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartAttendance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixEmployeeShiftShiftRelationship_20260717 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeShifts_Shifts_ShiftId1",
                table: "EmployeeShifts");

            migrationBuilder.DropIndex(
                name: "IX_EmployeeShifts_ShiftId1",
                table: "EmployeeShifts");

            migrationBuilder.DropColumn(
                name: "ShiftId1",
                table: "EmployeeShifts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ShiftId1",
                table: "EmployeeShifts",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeShifts_ShiftId1",
                table: "EmployeeShifts",
                column: "ShiftId1");

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeShifts_Shifts_ShiftId1",
                table: "EmployeeShifts",
                column: "ShiftId1",
                principalTable: "Shifts",
                principalColumn: "Id");
        }
    }
}
