using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Infrastructure.Persistence.Configurations;

public class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.ToTable("Employees");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.EmployeeNo)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.FullName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.NationalId)
            .HasMaxLength(50);

        builder.Property(x => x.Phone)
            .HasMaxLength(50);

        builder.Property(x => x.Email)
            .HasMaxLength(200);

        builder.Property(x => x.PhotoPath)
            .HasMaxLength(500);

        builder.Property(x => x.ContractType)
            .HasMaxLength(100);

        builder.Property(x => x.EmploymentStatus)
            .HasMaxLength(100);

        builder.Property(x => x.ServiceEndType)
            .HasMaxLength(80);

        builder.Property(x => x.ServiceEndReason)
            .HasMaxLength(1000);

        builder.Property(x => x.ServiceEndNotes)
            .HasMaxLength(2000);

        builder.Property(x => x.ClearanceStatus)
            .HasMaxLength(80);

        builder.Property(x => x.RehireReason)
            .HasMaxLength(1000);

        builder.Property(x => x.RehireNotes)
            .HasMaxLength(2000);

        builder.Property(x => x.RehireCount)
            .HasDefaultValue(0);

        builder.HasIndex(x => x.EmployeeNo)
            .IsUnique();

        builder.HasIndex(x => x.PositionId);

        builder.HasOne(x => x.Branch)
            .WithMany(x => x.Employees)
            .HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Department)
            .WithMany(x => x.Employees)
            .HasForeignKey(x => x.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
