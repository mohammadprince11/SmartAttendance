using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace SmartAttendance.Infrastructure.Migrations
{
    public partial class AddDynamicAnnouncementTemplates : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AnnouncementContents_LanguageCode",
                table: "AnnouncementContents");

            migrationBuilder.AlterColumn<string>(
                name: "LanguageCode",
                table: "AnnouncementContents",
                type: "nvarchar(35)",
                maxLength: 35,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(5)",
                oldMaxLength: 5);

            migrationBuilder.AddColumn<string>(
                name: "PresentationJson",
                table: "AnnouncementContents",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AnnouncementStudioDesigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Data = table.Column<byte[]>(type: "varbinary(max)", maxLength: 5242880, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnnouncementStudioDesigns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AnnouncementStudioDesigns_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AnnouncementStudioProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    DefinitionJson = table.Column<string>(type: "nvarchar(max)", maxLength: 100000, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Revision = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnnouncementStudioProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AnnouncementStudioProfiles_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnnouncementStudioDesigns_CompanyId_IsActive",
                table: "AnnouncementStudioDesigns",
                columns: new[] { "CompanyId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_AnnouncementStudioProfiles_CompanyId_Key",
                table: "AnnouncementStudioProfiles",
                columns: new[] { "CompanyId", "Key" },
                unique: true);
        }
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AnnouncementStudioDesigns");
            migrationBuilder.DropTable(name: "AnnouncementStudioProfiles");
            migrationBuilder.DropColumn(name: "PresentationJson", table: "AnnouncementContents");
            // Keep the expanded language column to protect new-language content.
        }
    }
}
