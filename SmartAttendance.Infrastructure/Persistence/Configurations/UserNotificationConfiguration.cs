using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Infrastructure.Persistence.Configurations;

public class UserNotificationConfiguration : IEntityTypeConfiguration<UserNotification>
{
    public void Configure(EntityTypeBuilder<UserNotification> builder)
    {
        builder.ToTable("UserNotifications", table =>
        {
            table.HasCheckConstraint(
                "CK_UserNotifications_LanguageContent",
                "(((NULLIF(LTRIM(RTRIM([TitleAr])), N'') IS NULL AND NULLIF(LTRIM(RTRIM([MessageAr])), N'') IS NULL) OR " +
                "(NULLIF(LTRIM(RTRIM([TitleAr])), N'') IS NOT NULL AND NULLIF(LTRIM(RTRIM([MessageAr])), N'') IS NOT NULL)) AND " +
                "((NULLIF(LTRIM(RTRIM([TitleEn])), N'') IS NULL AND NULLIF(LTRIM(RTRIM([MessageEn])), N'') IS NULL) OR " +
                "(NULLIF(LTRIM(RTRIM([TitleEn])), N'') IS NOT NULL AND NULLIF(LTRIM(RTRIM([MessageEn])), N'') IS NOT NULL)) AND " +
                "((NULLIF(LTRIM(RTRIM([TitleAr])), N'') IS NOT NULL AND NULLIF(LTRIM(RTRIM([MessageAr])), N'') IS NOT NULL) OR " +
                "(NULLIF(LTRIM(RTRIM([TitleEn])), N'') IS NOT NULL AND NULLIF(LTRIM(RTRIM([MessageEn])), N'') IS NOT NULL)))");
        });
        builder.HasKey(x => x.Id);

        builder.Property(x => x.NotificationType).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(x => x.TitleAr).HasMaxLength(250);
        builder.Property(x => x.TitleEn).HasMaxLength(250);
        builder.Property(x => x.MessageAr).HasMaxLength(1000);
        builder.Property(x => x.MessageEn).HasMaxLength(1000);
        builder.Property(x => x.Url).HasMaxLength(500);

        builder.HasIndex(x => new { x.AnnouncementGroupId, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.AnnouncementGroupId, x.NotificationType })
            .HasDatabaseName("UX_UserNotifications_AnnouncementGroup_Type")
            .IsUnique()
            .HasFilter("[AnnouncementGroupId] IS NOT NULL AND [IsDeleted] = 0");

        builder.HasOne(x => x.AnnouncementGroup)
            .WithMany(x => x.Notifications)
            .HasForeignKey(x => x.AnnouncementGroupId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
