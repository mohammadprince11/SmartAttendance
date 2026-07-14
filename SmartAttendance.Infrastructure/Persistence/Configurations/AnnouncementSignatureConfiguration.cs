using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Infrastructure.Persistence.Configurations;

public class AnnouncementSignatureConfiguration : IEntityTypeConfiguration<AnnouncementSignature>
{
    public void Configure(EntityTypeBuilder<AnnouncementSignature> builder)
    {
        builder.ToTable("AnnouncementSignatures", table =>
        {
            table.HasCheckConstraint(
                "CK_AnnouncementSignatures_LanguageCode",
                "[LanguageCode] IN (N'ar', N'en')");
        });
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).HasMaxLength(150).IsRequired();
        builder.Property(x => x.LanguageCode).HasMaxLength(5).IsRequired();
        builder.Property(x => x.Text).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => new { x.Name, x.LanguageCode }).IsUnique();
        builder.HasIndex(x => new { x.LanguageCode, x.IsActive, x.IsDefault });
        builder.HasIndex(x => x.LanguageCode)
            .HasDatabaseName("UX_AnnouncementSignatures_DefaultPerLanguage")
            .IsUnique()
            .HasFilter("[IsDefault] = 1 AND [IsActive] = 1 AND [IsDeleted] = 0");
    }
}
