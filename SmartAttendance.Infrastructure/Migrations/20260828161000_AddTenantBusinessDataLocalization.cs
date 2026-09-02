using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SmartAttendance.Infrastructure.Persistence;

#nullable disable

namespace SmartAttendance.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260828161000_AddTenantBusinessDataLocalization")]
public sealed class AddTenantBusinessDataLocalization : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "CompanyLanguages",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                CompanyId = table.Column<int>(type: "int", nullable: false),
                CultureCode = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: false),
                NativeName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                EnglishName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                Direction = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                IsDefault = table.Column<bool>(type: "bit", nullable: false),
                IsRequired = table.Column<bool>(type: "bit", nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CompanyLanguages", x => x.Id);
                table.ForeignKey(
                    name: "FK_CompanyLanguages_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.CheckConstraint(
                    name: "CK_CompanyLanguages_Direction",
                    sql: "[Direction] IN ('rtl', 'ltr')");
            });

        migrationBuilder.CreateTable(
            name: "LocalizedEntityValues",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                CompanyId = table.Column<int>(type: "int", nullable: false),
                EntityType = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                EntityId = table.Column<int>(type: "int", nullable: false),
                FieldName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                CultureCode = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: false),
                Value = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                TranslationStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LocalizedEntityValues", x => x.Id);
                table.ForeignKey(
                    name: "FK_LocalizedEntityValues_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.CheckConstraint(
                    name: "CK_LocalizedEntityValues_Status",
                    sql: "[TranslationStatus] IN ('Manual', 'Machine', 'Reviewed')");
            });

        migrationBuilder.CreateIndex(
            name: "UX_CompanyLanguages_Company_Culture",
            table: "CompanyLanguages",
            columns: new[] { "CompanyId", "CultureCode" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "UX_CompanyLanguages_OneDefault",
            table: "CompanyLanguages",
            column: "CompanyId",
            unique: true,
            filter: "[IsDefault] = 1 AND [IsActive] = 1 AND [IsDeleted] = 0");

        migrationBuilder.CreateIndex(
            name: "IX_LocalizedEntityValues_EntityCulture",
            table: "LocalizedEntityValues",
            columns: new[] { "CompanyId", "EntityType", "EntityId", "CultureCode" });

        migrationBuilder.CreateIndex(
            name: "UX_LocalizedEntityValues_FieldCulture",
            table: "LocalizedEntityValues",
            columns: new[] { "CompanyId", "EntityType", "EntityId", "FieldName", "CultureCode" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "LocalizedEntityValues");
        migrationBuilder.DropTable(name: "CompanyLanguages");
    }
}
