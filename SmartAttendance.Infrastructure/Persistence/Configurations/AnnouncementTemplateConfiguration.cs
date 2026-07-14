using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Infrastructure.Persistence.Configurations;

public class AnnouncementTemplateConfiguration : IEntityTypeConfiguration<AnnouncementTemplate>
{
    public void Configure(EntityTypeBuilder<AnnouncementTemplate> builder)
    {
        builder.ToTable("AnnouncementTemplates", table =>
        {
            table.HasCheckConstraint(
                "CK_AnnouncementTemplates_LanguageCode",
                "[LanguageCode] IN (N'ar', N'en')");
        });
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Key).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(150).IsRequired();
        builder.Property(x => x.LanguageCode).HasMaxLength(5).IsRequired();
        builder.Property(x => x.TitleTemplate).HasMaxLength(250).IsRequired();
        builder.Property(x => x.BodyTemplate).IsRequired();
        builder.Property(x => x.Category).HasMaxLength(100);
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => new { x.Key, x.LanguageCode }).IsUnique();
        builder.HasIndex(x => new { x.LanguageCode, x.IsActive });
    }
}
