using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Infrastructure.Persistence.Configurations;

public class AnnouncementAttachmentConfiguration : IEntityTypeConfiguration<AnnouncementAttachment>
{
    public void Configure(EntityTypeBuilder<AnnouncementAttachment> builder)
    {
        builder.ToTable("AnnouncementAttachments");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.OriginalFileName).HasMaxLength(260).IsRequired();
        builder.Property(x => x.StoredFileName).HasMaxLength(260).IsRequired();
        builder.Property(x => x.StorageKey).HasMaxLength(500).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Sha256).HasMaxLength(64).IsRequired();

        builder.HasIndex(x => new { x.AnnouncementContentId, x.DisplayOrder });
        builder.HasIndex(x => x.StorageKey).IsUnique();

        builder.HasOne(x => x.AnnouncementContent)
            .WithMany(x => x.Attachments)
            .HasForeignKey(x => x.AnnouncementContentId)
            .OnDelete(DeleteBehavior.Cascade);

    }
}
