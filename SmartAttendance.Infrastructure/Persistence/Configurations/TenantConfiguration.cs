using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Infrastructure.Persistence.Configurations;

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("Tenants", table =>
            table.HasCheckConstraint(
                "CK_Tenants_Code_FourDigits",
                "LEN([Code]) = 4 AND [Code] NOT LIKE '%[^0-9]%'"));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code)
            .HasMaxLength(4)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.IsActive)
            .HasDefaultValue(true);

        builder.HasIndex(x => x.Code)
            .IsUnique();

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
