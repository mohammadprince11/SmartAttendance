using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Infrastructure.Persistence.Configurations;

public sealed class CompanyLanguageConfiguration : IEntityTypeConfiguration<CompanyLanguage>
{
    public void Configure(EntityTypeBuilder<CompanyLanguage> builder)
    {
        // Created by the explicit, narrowly-scoped localization migration. The
        // legacy model snapshot is intentionally not reconciled by this feature.
        builder.ToTable("CompanyLanguages", table => table.ExcludeFromMigrations());
        builder.HasKey(x => x.Id);
        builder.Property(x => x.CultureCode).HasMaxLength(35).IsRequired();
        builder.Property(x => x.NativeName).HasMaxLength(120).IsRequired();
        builder.Property(x => x.EnglishName).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Direction).HasMaxLength(3).IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.CultureCode }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.IsActive, x.IsDefault });
        builder.HasOne(x => x.Company)
            .WithMany(x => x.Languages)
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
