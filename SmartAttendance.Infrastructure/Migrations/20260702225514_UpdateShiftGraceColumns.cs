using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartAttendance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdateShiftGraceColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Shifts_Name",
                table: "Shifts");

            migrationBuilder.RenameColumn(
                name: "GracePeriodMinutes",
                table: "Shifts",
                newName: "GraceOutMinutes");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Shifts",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "Shifts",
                type: "bit",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "bit");

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Shifts",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "GraceInMinutes",
                table: "Shifts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsNightShift",
                table: "Shifts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "WorkingHours",
                table: "Shifts",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "ShiftId1",
                table: "EmployeeShifts",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Shifts_Code",
                table: "Shifts",
                column: "Code",
                unique: true);

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeShifts_Shifts_ShiftId1",
                table: "EmployeeShifts");

            migrationBuilder.DropIndex(
                name: "IX_Shifts_Code",
                table: "Shifts");

            migrationBuilder.DropIndex(
                name: "IX_EmployeeShifts_ShiftId1",
                table: "EmployeeShifts");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "GraceInMinutes",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "IsNightShift",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "WorkingHours",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "ShiftId1",
                table: "EmployeeShifts");

            migrationBuilder.RenameColumn(
                name: "GraceOutMinutes",
                table: "Shifts",
                newName: "GracePeriodMinutes");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Shifts",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(150)",
                oldMaxLength: 150);

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "Shifts",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: true);

            migrationBuilder.CreateIndex(
                name: "IX_Shifts_Name",
                table: "Shifts",
                column: "Name");
        }
    }
}
