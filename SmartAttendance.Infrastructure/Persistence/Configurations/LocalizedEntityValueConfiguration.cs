using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Infrastructure.Persistence.Configurations;

public sealed class LocalizedEntityValueConfiguration : IEntityTypeConfiguration<LocalizedEntityValue>
{
    public void Configure(EntityTypeBuilder<LocalizedEntityValue> builder)
    {
        // Created by the explicit, narrowly-scoped localization migration. The
        // legacy model snapshot is intentionally not reconciled by this feature.
        builder.ToTable("LocalizedEntityValues", table => table.ExcludeFromMigrations());
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EntityType).HasMaxLength(80).IsRequired();
        builder.Property(x => x.FieldName).HasMaxLength(80).IsRequired();
        builder.Property(x => x.CultureCode).HasMaxLength(35).IsRequired();
        builder.Property(x => x.Value).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.TranslationStatus).HasMaxLength(20).IsRequired();
        builder.HasIndex(x => new
        {
            x.CompanyId,
            x.EntityType,
            x.EntityId,
            x.FieldName,
            x.CultureCode
        }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.EntityType, x.EntityId, x.CultureCode });
        builder.HasOne(x => x.Company)
            .WithMany(x => x.LocalizedValues)
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
