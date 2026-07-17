using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Infrastructure.Persistence.Configurations;

public class EmployeeShiftConfiguration : IEntityTypeConfiguration<EmployeeShift>
{
    public void Configure(EntityTypeBuilder<EmployeeShift> builder)
    {
        builder.ToTable("EmployeeShifts");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.EffectiveFrom)
            .IsRequired();

        builder.HasOne(x => x.Employee)
            .WithMany()
            .HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Shift)
            .WithMany(x => x.EmployeeShifts)
            .HasForeignKey(x => x.ShiftId)
            .OnDelete(DeleteBehavior.Restrict);

        // فلتر مطابق لفلتر Shift حتى لا تظهر إسنادات تشير إلى شفت محذوف ناعماً.
        builder.HasQueryFilter(x => !x.IsDeleted && !x.Shift.IsDeleted);

        builder.HasIndex(x => new
        {
            x.EmployeeId,
            x.EffectiveFrom
        });
    }
}