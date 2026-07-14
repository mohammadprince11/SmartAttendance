using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Infrastructure.Persistence.Configurations;

public class AnnouncementGroupConfiguration : IEntityTypeConfiguration<AnnouncementGroup>
{
    public void Configure(EntityTypeBuilder<AnnouncementGroup> builder)
    {
        builder.ToTable("AnnouncementGroups", table =>
        {
            table.HasCheckConstraint(
                "CK_AnnouncementGroups_DateOrder",
                "[PublishDate] IS NULL OR [ExpireDate] IS NULL OR [ExpireDate] >= [PublishDate]");
        });
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TranslationGroupId).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.ExpirationBehavior).HasConversion<string>().HasMaxLength(40);
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => x.TranslationGroupId).IsUnique();
        builder.HasIndex(x => x.LegacyAnnouncementId).IsUnique().HasFilter("[LegacyAnnouncementId] IS NOT NULL");
        builder.HasIndex(x => x.CreatedBySystemUserId);
        builder.HasIndex(x => new { x.Status, x.PublishDate });
        builder.HasIndex(x => new { x.Status, x.ExpireDate });

        builder.HasOne(x => x.CreatedBySystemUser)
            .WithMany()
            .HasForeignKey(x => x.CreatedBySystemUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
