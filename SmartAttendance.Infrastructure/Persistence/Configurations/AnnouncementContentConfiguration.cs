using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Infrastructure.Persistence.Configurations;

public class AnnouncementContentConfiguration : IEntityTypeConfiguration<AnnouncementContent>
{
    public void Configure(EntityTypeBuilder<AnnouncementContent> builder)
    {
        builder.ToTable("AnnouncementContents", table =>
        {
            table.HasCheckConstraint(
                "CK_AnnouncementContents_LanguageCode",
                "[LanguageCode] IN (N'ar', N'en')");
        });
        builder.HasKey(x => x.Id);

        builder.Property(x => x.LanguageCode).HasMaxLength(5).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(250).IsRequired();
        builder.Property(x => x.Body).IsRequired();
        builder.Property(x => x.Category).HasMaxLength(100);
        builder.Property(x => x.SignatureType).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(x => x.SignatureTextSnapshot).HasMaxLength(1000);
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => new { x.AnnouncementGroupId, x.LanguageCode }).IsUnique();
        builder.HasIndex(x => x.AnnouncementTemplateId);
        builder.HasIndex(x => x.AnnouncementSignatureId);

        builder.HasOne(x => x.AnnouncementGroup)
            .WithMany(x => x.Contents)
            .HasForeignKey(x => x.AnnouncementGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Template)
            .WithMany(x => x.Contents)
            .HasForeignKey(x => x.AnnouncementTemplateId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.Signature)
            .WithMany(x => x.Contents)
            .HasForeignKey(x => x.AnnouncementSignatureId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
