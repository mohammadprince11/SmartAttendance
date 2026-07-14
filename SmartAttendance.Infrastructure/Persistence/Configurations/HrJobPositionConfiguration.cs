using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Infrastructure.Persistence.Configurations;

public class HrJobPositionConfiguration : IEntityTypeConfiguration<HrJobPosition>
{
    public void Configure(EntityTypeBuilder<HrJobPosition> builder)
    {
        builder.ToTable("HrJobPositions", table => table.ExcludeFromMigrations());
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ArabicName).HasMaxLength(400).IsRequired();
        builder.Property(x => x.EnglishName).HasMaxLength(400);
        builder.Property(x => x.CompanyId).IsRequired();
        builder.Property(x => x.DepartmentId);
        builder.Property(x => x.IsActive).IsRequired();
    }
}
