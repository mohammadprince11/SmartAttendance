using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartAttendance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAnnouncementStudioFinalFoundation_20260714 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnnouncementAuditLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AnnouncementGroupId = table.Column<int>(type: "int", nullable: true),
                    TranslationGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EntityName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    EntityId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Action = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    OldValuesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewValuesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SystemUserId = table.Column<int>(type: "int", nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnnouncementAuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AnnouncementGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TranslationGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LegacyAnnouncementId = table.Column<int>(type: "int", nullable: true),
                    CreatedBySystemUserId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PublishDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ExpireDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ExpirationBehavior = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    CommentsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    ReactionsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AudienceCapturedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PublishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ArchivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnnouncementGroups", x => x.Id);
                    table.CheckConstraint("CK_AnnouncementGroups_DateOrder", "[PublishDate] IS NULL OR [ExpireDate] IS NULL OR [ExpireDate] >= [PublishDate]");
                    table.ForeignKey(
                        name: "FK_AnnouncementGroups_SystemUsers_CreatedBySystemUserId",
                        column: x => x.CreatedBySystemUserId,
                        principalTable: "SystemUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "AnnouncementSignatures",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    LanguageCode = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnnouncementSignatures", x => x.Id);
                    table.CheckConstraint("CK_AnnouncementSignatures_LanguageCode", "[LanguageCode] IN (N'ar', N'en')");
                });

            migrationBuilder.CreateTable(
                name: "AnnouncementTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    LanguageCode = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    TitleTemplate = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    BodyTemplate = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsSystem = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnnouncementTemplates", x => x.Id);
                    table.CheckConstraint("CK_AnnouncementTemplates_LanguageCode", "[LanguageCode] IN (N'ar', N'en')");
                });

            migrationBuilder.CreateTable(
                name: "AnnouncementAudienceRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AnnouncementGroupId = table.Column<int>(type: "int", nullable: false),
                    AudienceType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    IsExcluded = table.Column<bool>(type: "bit", nullable: false),
                    CompanyId = table.Column<int>(type: "int", nullable: true),
                    BranchId = table.Column<int>(type: "int", nullable: true),
                    DepartmentId = table.Column<int>(type: "int", nullable: true),
                    PositionId = table.Column<int>(type: "int", nullable: true),
                    EmployeeId = table.Column<int>(type: "int", nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnnouncementAudienceRules", x => x.Id);
                    table.CheckConstraint("CK_AnnouncementAudienceRules_Target", "(([AudienceType] = N'All' AND [CompanyId] IS NULL AND [BranchId] IS NULL AND [DepartmentId] IS NULL AND [PositionId] IS NULL AND [EmployeeId] IS NULL) OR ([AudienceType] = N'Company' AND [CompanyId] IS NOT NULL AND [BranchId] IS NULL AND [DepartmentId] IS NULL AND [PositionId] IS NULL AND [EmployeeId] IS NULL) OR ([AudienceType] = N'Branch' AND [CompanyId] IS NULL AND [BranchId] IS NOT NULL AND [DepartmentId] IS NULL AND [PositionId] IS NULL AND [EmployeeId] IS NULL) OR ([AudienceType] = N'Department' AND [CompanyId] IS NULL AND [BranchId] IS NULL AND [DepartmentId] IS NOT NULL AND [PositionId] IS NULL AND [EmployeeId] IS NULL) OR ([AudienceType] = N'Position' AND [CompanyId] IS NULL AND [BranchId] IS NULL AND [DepartmentId] IS NULL AND [PositionId] IS NOT NULL AND [EmployeeId] IS NULL) OR ([AudienceType] = N'Employee' AND [CompanyId] IS NULL AND [BranchId] IS NULL AND [DepartmentId] IS NULL AND [PositionId] IS NULL AND [EmployeeId] IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_AnnouncementAudienceRules_AnnouncementGroups_AnnouncementGroupId",
                        column: x => x.AnnouncementGroupId,
                        principalTable: "AnnouncementGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AnnouncementAudienceRules_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AnnouncementAudienceRules_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AnnouncementAudienceRules_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AnnouncementAudienceRules_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AnnouncementAudienceRules_HrJobPositions_PositionId",
                        column: x => x.PositionId,
                        principalTable: "HrJobPositions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AnnouncementChannels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AnnouncementGroupId = table.Column<int>(type: "int", nullable: false),
                    ChannelType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    ActivatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnnouncementChannels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AnnouncementChannels_AnnouncementGroups_AnnouncementGroupId",
                        column: x => x.AnnouncementGroupId,
                        principalTable: "AnnouncementGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AnnouncementComments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AnnouncementGroupId = table.Column<int>(type: "int", nullable: false),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    HiddenAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    HiddenBy = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnnouncementComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AnnouncementComments_AnnouncementGroups_AnnouncementGroupId",
                        column: x => x.AnnouncementGroupId,
                        principalTable: "AnnouncementGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AnnouncementComments_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AnnouncementReactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AnnouncementGroupId = table.Column<int>(type: "int", nullable: false),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    ReactionType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnnouncementReactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AnnouncementReactions_AnnouncementGroups_AnnouncementGroupId",
                        column: x => x.AnnouncementGroupId,
                        principalTable: "AnnouncementGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AnnouncementReactions_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AnnouncementReadReceipts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AnnouncementGroupId = table.Column<int>(type: "int", nullable: false),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    FirstReadAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastOpenedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    NotificationReadAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OpenCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnnouncementReadReceipts", x => x.Id);
                    table.CheckConstraint("CK_AnnouncementReadReceipts_OpenCount", "[OpenCount] >= 1");
                    table.ForeignKey(
                        name: "FK_AnnouncementReadReceipts_AnnouncementGroups_AnnouncementGroupId",
                        column: x => x.AnnouncementGroupId,
                        principalTable: "AnnouncementGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AnnouncementReadReceipts_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AnnouncementRecipients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AnnouncementGroupId = table.Column<int>(type: "int", nullable: false),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SourceSummary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnnouncementRecipients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AnnouncementRecipients_AnnouncementGroups_AnnouncementGroupId",
                        column: x => x.AnnouncementGroupId,
                        principalTable: "AnnouncementGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AnnouncementRecipients_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserNotifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AnnouncementGroupId = table.Column<int>(type: "int", nullable: true),
                    NotificationType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    TitleAr = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    TitleEn = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    MessageAr = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    MessageEn = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserNotifications", x => x.Id);
                    table.CheckConstraint("CK_UserNotifications_LanguageContent", "(((NULLIF(LTRIM(RTRIM([TitleAr])), N'') IS NULL AND NULLIF(LTRIM(RTRIM([MessageAr])), N'') IS NULL) OR (NULLIF(LTRIM(RTRIM([TitleAr])), N'') IS NOT NULL AND NULLIF(LTRIM(RTRIM([MessageAr])), N'') IS NOT NULL)) AND ((NULLIF(LTRIM(RTRIM([TitleEn])), N'') IS NULL AND NULLIF(LTRIM(RTRIM([MessageEn])), N'') IS NULL) OR (NULLIF(LTRIM(RTRIM([TitleEn])), N'') IS NOT NULL AND NULLIF(LTRIM(RTRIM([MessageEn])), N'') IS NOT NULL)) AND ((NULLIF(LTRIM(RTRIM([TitleAr])), N'') IS NOT NULL AND NULLIF(LTRIM(RTRIM([MessageAr])), N'') IS NOT NULL) OR (NULLIF(LTRIM(RTRIM([TitleEn])), N'') IS NOT NULL AND NULLIF(LTRIM(RTRIM([MessageEn])), N'') IS NOT NULL)))");
                    table.ForeignKey(
                        name: "FK_UserNotifications_AnnouncementGroups_AnnouncementGroupId",
                        column: x => x.AnnouncementGroupId,
                        principalTable: "AnnouncementGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AnnouncementContents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AnnouncementGroupId = table.Column<int>(type: "int", nullable: false),
                    LanguageCode = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    AnnouncementTemplateId = table.Column<int>(type: "int", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SignatureType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    AnnouncementSignatureId = table.Column<int>(type: "int", nullable: true),
                    SignatureTextSnapshot = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnnouncementContents", x => x.Id);
                    table.CheckConstraint("CK_AnnouncementContents_LanguageCode", "[LanguageCode] IN (N'ar', N'en')");
                    table.ForeignKey(
                        name: "FK_AnnouncementContents_AnnouncementGroups_AnnouncementGroupId",
                        column: x => x.AnnouncementGroupId,
                        principalTable: "AnnouncementGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AnnouncementContents_AnnouncementSignatures_AnnouncementSignatureId",
                        column: x => x.AnnouncementSignatureId,
                        principalTable: "AnnouncementSignatures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AnnouncementContents_AnnouncementTemplates_AnnouncementTemplateId",
                        column: x => x.AnnouncementTemplateId,
                        principalTable: "AnnouncementTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "UserNotificationRecipients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserNotificationId = table.Column<int>(type: "int", nullable: false),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    IsRead = table.Column<bool>(type: "bit", nullable: false),
                    ReadAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserNotificationRecipients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserNotificationRecipients_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserNotificationRecipients_UserNotifications_UserNotificationId",
                        column: x => x.UserNotificationId,
                        principalTable: "UserNotifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AnnouncementAttachments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AnnouncementContentId = table.Column<int>(type: "int", nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    StoredFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsInlinePreview = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnnouncementAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AnnouncementAttachments_AnnouncementContents_AnnouncementContentId",
                        column: x => x.AnnouncementContentId,
                        principalTable: "AnnouncementContents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnnouncementAttachments_AnnouncementContentId_DisplayOrder",
                table: "AnnouncementAttachments",
                columns: new[] { "AnnouncementContentId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_AnnouncementAttachments_StorageKey",
                table: "AnnouncementAttachments",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AnnouncementAudienceRules_AnnouncementGroupId_IsExcluded_AudienceType",
                table: "AnnouncementAudienceRules",
                columns: new[] { "AnnouncementGroupId", "IsExcluded", "AudienceType" });

            migrationBuilder.CreateIndex(
                name: "IX_AnnouncementAudienceRules_BranchId",
                table: "AnnouncementAudienceRules",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_AnnouncementAudienceRules_CompanyId",
                table: "AnnouncementAudienceRules",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_AnnouncementAudienceRules_DepartmentId",
                table: "AnnouncementAudienceRules",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_AnnouncementAudienceRules_EmployeeId",
                table: "AnnouncementAudienceRules",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_AnnouncementAudienceRules_PositionId",
                table: "AnnouncementAudienceRules",
                column: "PositionId");

            migrationBuilder.CreateIndex(
                name: "IX_AnnouncementAuditLogs_AnnouncementGroupId_OccurredAtUtc",
                table: "AnnouncementAuditLogs",
                columns: new[] { "AnnouncementGroupId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AnnouncementAuditLogs_EntityName_EntityId_OccurredAtUtc",
                table: "AnnouncementAuditLogs",
                columns: new[] { "EntityName", "EntityId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AnnouncementAuditLogs_TranslationGroupId_OccurredAtUtc",
                table: "AnnouncementAuditLogs",
                columns: new[] { "TranslationGroupId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AnnouncementChannels_AnnouncementGroupId_ChannelType",
                table: "AnnouncementChannels",
                columns: new[] { "AnnouncementGroupId", "ChannelType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AnnouncementComments_AnnouncementGroupId_CreatedAt",
                table: "AnnouncementComments",
                columns: new[] { "AnnouncementGroupId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AnnouncementComments_EmployeeId_CreatedAt",
                table: "AnnouncementComments",
                columns: new[] { "EmployeeId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AnnouncementContents_AnnouncementGroupId_LanguageCode",
                table: "AnnouncementContents",
                columns: new[] { "AnnouncementGroupId", "LanguageCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AnnouncementContents_AnnouncementSignatureId",
                table: "AnnouncementContents",
                column: "AnnouncementSignatureId");

            migrationBuilder.CreateIndex(
                name: "IX_AnnouncementContents_AnnouncementTemplateId",
                table: "AnnouncementContents",
                column: "AnnouncementTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_AnnouncementGroups_CreatedBySystemUserId",
                table: "AnnouncementGroups",
                column: "CreatedBySystemUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AnnouncementGroups_LegacyAnnouncementId",
                table: "AnnouncementGroups",
                column: "LegacyAnnouncementId",
                unique: true,
                filter: "[LegacyAnnouncementId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AnnouncementGroups_Status_ExpireDate",
                table: "AnnouncementGroups",
                columns: new[] { "Status", "ExpireDate" });

            migrationBuilder.CreateIndex(
                name: "IX_AnnouncementGroups_Status_PublishDate",
                table: "AnnouncementGroups",
                columns: new[] { "Status", "PublishDate" });

            migrationBuilder.CreateIndex(
                name: "IX_AnnouncementGroups_TranslationGroupId",
                table: "AnnouncementGroups",
                column: "TranslationGroupId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AnnouncementReactions_AnnouncementGroupId_EmployeeId",
                table: "AnnouncementReactions",
                columns: new[] { "AnnouncementGroupId", "EmployeeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AnnouncementReactions_AnnouncementGroupId_ReactionType",
                table: "AnnouncementReactions",
                columns: new[] { "AnnouncementGroupId", "ReactionType" });

            migrationBuilder.CreateIndex(
                name: "IX_AnnouncementReactions_EmployeeId",
                table: "AnnouncementReactions",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_AnnouncementReadReceipts_AnnouncementGroupId_EmployeeId",
                table: "AnnouncementReadReceipts",
                columns: new[] { "AnnouncementGroupId", "EmployeeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AnnouncementReadReceipts_EmployeeId_FirstReadAtUtc",
                table: "AnnouncementReadReceipts",
                columns: new[] { "EmployeeId", "FirstReadAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AnnouncementRecipients_AnnouncementGroupId_EmployeeId",
                table: "AnnouncementRecipients",
                columns: new[] { "AnnouncementGroupId", "EmployeeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AnnouncementRecipients_EmployeeId_ResolvedAtUtc",
                table: "AnnouncementRecipients",
                columns: new[] { "EmployeeId", "ResolvedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AnnouncementSignatures_LanguageCode_IsActive_IsDefault",
                table: "AnnouncementSignatures",
                columns: new[] { "LanguageCode", "IsActive", "IsDefault" });

            migrationBuilder.CreateIndex(
                name: "IX_AnnouncementSignatures_Name_LanguageCode",
                table: "AnnouncementSignatures",
                columns: new[] { "Name", "LanguageCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_AnnouncementSignatures_DefaultPerLanguage",
                table: "AnnouncementSignatures",
                column: "LanguageCode",
                unique: true,
                filter: "[IsDefault] = 1 AND [IsActive] = 1 AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_AnnouncementTemplates_Key_LanguageCode",
                table: "AnnouncementTemplates",
                columns: new[] { "Key", "LanguageCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AnnouncementTemplates_LanguageCode_IsActive",
                table: "AnnouncementTemplates",
                columns: new[] { "LanguageCode", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_UserNotificationRecipients_EmployeeId_IsRead_CreatedAt",
                table: "UserNotificationRecipients",
                columns: new[] { "EmployeeId", "IsRead", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_UserNotificationRecipients_UserNotificationId_EmployeeId",
                table: "UserNotificationRecipients",
                columns: new[] { "UserNotificationId", "EmployeeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserNotifications_AnnouncementGroupId_CreatedAtUtc",
                table: "UserNotifications",
                columns: new[] { "AnnouncementGroupId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_UserNotifications_AnnouncementGroup_Type",
                table: "UserNotifications",
                columns: new[] { "AnnouncementGroupId", "NotificationType" },
                unique: true,
                filter: "[AnnouncementGroupId] IS NOT NULL AND [IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnnouncementAttachments");

            migrationBuilder.DropTable(
                name: "AnnouncementAudienceRules");

            migrationBuilder.DropTable(
                name: "AnnouncementAuditLogs");

            migrationBuilder.DropTable(
                name: "AnnouncementChannels");

            migrationBuilder.DropTable(
                name: "AnnouncementComments");

            migrationBuilder.DropTable(
                name: "AnnouncementReactions");

            migrationBuilder.DropTable(
                name: "AnnouncementReadReceipts");

            migrationBuilder.DropTable(
                name: "AnnouncementRecipients");

            migrationBuilder.DropTable(
                name: "UserNotificationRecipients");

            migrationBuilder.DropTable(
                name: "AnnouncementContents");

            migrationBuilder.DropTable(
                name: "UserNotifications");

            migrationBuilder.DropTable(
                name: "AnnouncementSignatures");

            migrationBuilder.DropTable(
                name: "AnnouncementTemplates");

            migrationBuilder.DropTable(
                name: "AnnouncementGroups");
        }
    }
}
