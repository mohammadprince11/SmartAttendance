using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Infrastructure.Persistence.Configurations;

public class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> builder)
    {
        builder.ToTable("Devices");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.IpAddress)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.SerialNumber)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.Model)
            .HasMaxLength(100);

        builder.Property(x => x.FirmwareVersion)
            .HasMaxLength(50);

        builder.HasIndex(x => x.SerialNumber)
            .IsUnique();

        builder.HasOne(x => x.Branch)
            .WithMany(x => x.Devices)
            .HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}