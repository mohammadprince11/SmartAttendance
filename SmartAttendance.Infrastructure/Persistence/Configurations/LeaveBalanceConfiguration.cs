using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Infrastructure.Persistence.Configurations;

public class LeaveBalanceConfiguration : IEntityTypeConfiguration<LeaveBalance>
{
    public void Configure(EntityTypeBuilder<LeaveBalance> builder)
    {
        builder.ToTable("LeaveBalances");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Year)
            .IsRequired();

        builder.Property(x => x.LeaveType)
            .IsRequired();

        builder.Property(x => x.EntitledDays)
            .HasColumnType("decimal(5,1)")
            .IsRequired();

        builder.Property(x => x.CarriedOverDays)
            .HasColumnType("decimal(5,1)")
            .IsRequired();

        builder.Property(x => x.Note)
            .HasMaxLength(500);

        builder.HasOne(x => x.Employee)
            .WithMany()
            .HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        // One grant row per employee/year/type.
        builder.HasIndex(x => new { x.EmployeeId, x.Year, x.LeaveType })
            .IsUnique();

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
