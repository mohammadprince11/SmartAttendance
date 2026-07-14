using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SmartAttendance.Infrastructure.Persistence;

#nullable disable

namespace SmartAttendance.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260714214500_MigrateLegacyAnnouncementsToAnnouncementStudio_20260714")]
public partial class MigrateLegacyAnnouncementsToAnnouncementStudio_20260714 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            SET NOCOUNT ON;
            SET XACT_ABORT ON;

            IF OBJECT_ID(N'[dbo].[EmployeePortalAnnouncements]', N'U') IS NULL
            BEGIN
                THROW 51000, 'EmployeePortalAnnouncements was not found.', 1;
            END;

            IF EXISTS
            (
                SELECT 1
                FROM [dbo].[EmployeePortalAnnouncements]
                WHERE LOWER(ISNULL(NULLIF(LTRIM(RTRIM([TargetType])), N''), N'All')) <> N'all'
            )
            BEGIN
                THROW 51001, 'This reviewed legacy migration supports All audience announcements only.', 1;
            END;

            IF EXISTS
            (
                SELECT 1
                FROM [dbo].[EmployeePortalAnnouncements]
                WHERE NULLIF(LTRIM(RTRIM([Title])), N'') IS NULL
                   OR NULLIF(LTRIM(RTRIM(ISNULL([Body], N''))), N'') IS NULL
            )
            BEGIN
                THROW 51002, 'Legacy announcement title or body is empty.', 1;
            END;

            DECLARE @MigratedAtUtc datetime2 = SYSUTCDATETIME();

            INSERT INTO [dbo].[AnnouncementGroups]
            (
                [TranslationGroupId],
                [LegacyAnnouncementId],
                [CreatedBySystemUserId],
                [Status],
                [PublishDate],
                [ExpireDate],
                [ExpirationBehavior],
                [CommentsEnabled],
                [ReactionsEnabled],
                [AudienceCapturedAtUtc],
                [PublishedAtUtc],
                [ExpiresAtUtc],
                [ArchivedAtUtc],
                [CreatedAt],
                [UpdatedAt],
                [IsDeleted],
                [CreatedBy],
                [UpdatedBy]
            )
            SELECT
                NEWID(),
                legacy.[Id],
                creator.[Id],
                CASE WHEN legacy.[IsPublished] = 1 THEN N'Published' ELSE N'Pending' END,
                CASE WHEN legacy.[IsPublished] = 1 THEN CAST(legacy.[PublishDate] AS date) ELSE NULL END,
                NULL,
                NULL,
                CAST(0 AS bit),
                CAST(0 AS bit),
                @MigratedAtUtc,
                CASE WHEN legacy.[IsPublished] = 1 THEN legacy.[PublishDate] ELSE NULL END,
                NULL,
                NULL,
                legacy.[CreatedAt],
                NULL,
                CAST(0 AS bit),
                legacy.[CreatedBy],
                NULL
            FROM [dbo].[EmployeePortalAnnouncements] legacy
            OUTER APPLY
            (
                SELECT TOP (1) systemUser.[Id]
                FROM [dbo].[SystemUsers] systemUser
                WHERE systemUser.[IsDeleted] = 0
                  AND
                  (
                      systemUser.[UserName] = legacy.[CreatedBy]
                      OR systemUser.[FullName] = legacy.[CreatedBy]
                  )
                ORDER BY
                    CASE WHEN systemUser.[UserName] = legacy.[CreatedBy] THEN 0 ELSE 1 END,
                    systemUser.[Id]
            ) creator
            WHERE NOT EXISTS
            (
                SELECT 1
                FROM [dbo].[AnnouncementGroups] existingGroup
                WHERE existingGroup.[LegacyAnnouncementId] = legacy.[Id]
            );

            INSERT INTO [dbo].[AnnouncementContents]
            (
                [AnnouncementGroupId],
                [LanguageCode],
                [AnnouncementTemplateId],
                [Title],
                [Body],
                [Category],
                [SignatureType],
                [AnnouncementSignatureId],
                [SignatureTextSnapshot],
                [CreatedAt],
                [UpdatedAt],
                [IsDeleted],
                [CreatedBy],
                [UpdatedBy]
            )
            SELECT
                announcementGroup.[Id],
                N'ar',
                NULL,
                legacy.[Title],
                ISNULL(legacy.[Body], N''),
                NULLIF(LTRIM(RTRIM(legacy.[Category])), N''),
                N'None',
                NULL,
                NULL,
                legacy.[CreatedAt],
                NULL,
                CAST(0 AS bit),
                legacy.[CreatedBy],
                NULL
            FROM [dbo].[EmployeePortalAnnouncements] legacy
            INNER JOIN [dbo].[AnnouncementGroups] announcementGroup
                ON announcementGroup.[LegacyAnnouncementId] = legacy.[Id]
            WHERE NOT EXISTS
            (
                SELECT 1
                FROM [dbo].[AnnouncementContents] existingContent
                WHERE existingContent.[AnnouncementGroupId] = announcementGroup.[Id]
                  AND existingContent.[LanguageCode] = N'ar'
            );

            INSERT INTO [dbo].[AnnouncementAudienceRules]
            (
                [AnnouncementGroupId],
                [AudienceType],
                [IsExcluded],
                [CompanyId],
                [BranchId],
                [DepartmentId],
                [PositionId],
                [EmployeeId],
                [DisplayOrder],
                [CreatedAt],
                [UpdatedAt],
                [IsDeleted]
            )
            SELECT
                announcementGroup.[Id],
                N'All',
                CAST(0 AS bit),
                NULL,
                NULL,
                NULL,
                NULL,
                NULL,
                10,
                @MigratedAtUtc,
                NULL,
                CAST(0 AS bit)
            FROM [dbo].[EmployeePortalAnnouncements] legacy
            INNER JOIN [dbo].[AnnouncementGroups] announcementGroup
                ON announcementGroup.[LegacyAnnouncementId] = legacy.[Id]
            WHERE NOT EXISTS
            (
                SELECT 1
                FROM [dbo].[AnnouncementAudienceRules] existingRule
                WHERE existingRule.[AnnouncementGroupId] = announcementGroup.[Id]
                  AND existingRule.[AudienceType] = N'All'
                  AND existingRule.[IsExcluded] = 0
            );

            INSERT INTO [dbo].[AnnouncementRecipients]
            (
                [AnnouncementGroupId],
                [EmployeeId],
                [ResolvedAtUtc],
                [SourceSummary],
                [CreatedAt],
                [UpdatedAt],
                [IsDeleted]
            )
            SELECT
                announcementGroup.[Id],
                employee.[Id],
                @MigratedAtUtc,
                N'Legacy:All',
                @MigratedAtUtc,
                NULL,
                CAST(0 AS bit)
            FROM [dbo].[EmployeePortalAnnouncements] legacy
            INNER JOIN [dbo].[AnnouncementGroups] announcementGroup
                ON announcementGroup.[LegacyAnnouncementId] = legacy.[Id]
            CROSS JOIN [dbo].[Employees] employee
            WHERE employee.[IsActive] = 1
              AND employee.[IsDeleted] = 0
              AND NOT EXISTS
              (
                  SELECT 1
                  FROM [dbo].[AnnouncementRecipients] existingRecipient
                  WHERE existingRecipient.[AnnouncementGroupId] = announcementGroup.[Id]
                    AND existingRecipient.[EmployeeId] = employee.[Id]
              );

            INSERT INTO [dbo].[AnnouncementChannels]
            (
                [AnnouncementGroupId],
                [ChannelType],
                [IsEnabled],
                [ActivatedAtUtc],
                [CreatedAt],
                [UpdatedAt],
                [IsDeleted]
            )
            SELECT
                announcementGroup.[Id],
                N'EmployeeWall',
                CAST(1 AS bit),
                CASE WHEN legacy.[IsPublished] = 1 THEN legacy.[PublishDate] ELSE NULL END,
                @MigratedAtUtc,
                NULL,
                CAST(0 AS bit)
            FROM [dbo].[EmployeePortalAnnouncements] legacy
            INNER JOIN [dbo].[AnnouncementGroups] announcementGroup
                ON announcementGroup.[LegacyAnnouncementId] = legacy.[Id]
            WHERE NOT EXISTS
            (
                SELECT 1
                FROM [dbo].[AnnouncementChannels] existingChannel
                WHERE existingChannel.[AnnouncementGroupId] = announcementGroup.[Id]
                  AND existingChannel.[ChannelType] = N'EmployeeWall'
            );

            INSERT INTO [dbo].[AnnouncementAuditLogs]
            (
                [AnnouncementGroupId],
                [TranslationGroupId],
                [EntityName],
                [EntityId],
                [Action],
                [OldValuesJson],
                [NewValuesJson],
                [SystemUserId],
                [UserName],
                [IpAddress],
                [OccurredAtUtc],
                [CreatedAt],
                [UpdatedAt],
                [IsDeleted]
            )
            SELECT
                announcementGroup.[Id],
                announcementGroup.[TranslationGroupId],
                N'EmployeePortalAnnouncements',
                CONVERT(nvarchar(100), legacy.[Id]),
                N'MigratedFromLegacy',
                NULL,
                CONCAT(N'{"LegacyAnnouncementId":', CONVERT(nvarchar(20), legacy.[Id]), N',"NotificationCreated":false}'),
                announcementGroup.[CreatedBySystemUserId],
                legacy.[CreatedBy],
                NULL,
                @MigratedAtUtc,
                @MigratedAtUtc,
                NULL,
                CAST(0 AS bit)
            FROM [dbo].[EmployeePortalAnnouncements] legacy
            INNER JOIN [dbo].[AnnouncementGroups] announcementGroup
                ON announcementGroup.[LegacyAnnouncementId] = legacy.[Id]
            WHERE NOT EXISTS
            (
                SELECT 1
                FROM [dbo].[AnnouncementAuditLogs] existingAudit
                WHERE existingAudit.[EntityName] = N'EmployeePortalAnnouncements'
                  AND existingAudit.[EntityId] = CONVERT(nvarchar(100), legacy.[Id])
                  AND existingAudit.[Action] = N'MigratedFromLegacy'
            );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DELETE FROM [dbo].[AnnouncementAuditLogs]
            WHERE [EntityName] = N'EmployeePortalAnnouncements'
              AND [Action] = N'MigratedFromLegacy';

            DELETE FROM [dbo].[AnnouncementGroups]
            WHERE [LegacyAnnouncementId] IS NOT NULL;
            """);
    }
}
