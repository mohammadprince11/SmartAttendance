using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Infrastructure.Persistence.Configurations;

public class EmployeeDependentConfiguration : IEntityTypeConfiguration<EmployeeDependent>
{
    public void Configure(EntityTypeBuilder<EmployeeDependent> builder)
    {
        builder.ToTable("EmployeeDependents");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Relation).IsRequired();
        builder.Property(x => x.Name).IsRequired().HasMaxLength(200);
        builder.Property(x => x.NameOther).HasMaxLength(200);
        builder.Property(x => x.Religion).HasMaxLength(80);
        builder.Property(x => x.Nationality).HasMaxLength(100);
        builder.Property(x => x.NationalId).HasMaxLength(50);
        builder.Property(x => x.PassportNo).HasMaxLength(50);
        builder.Property(x => x.ResidencyNo).HasMaxLength(50);
        builder.Property(x => x.Gender).HasMaxLength(20);
        builder.Property(x => x.MaritalStatus).HasMaxLength(50);
        builder.Property(x => x.MobilePhone).HasMaxLength(50);
        builder.Property(x => x.CompanyName).HasMaxLength(200);
        builder.Property(x => x.Note).HasMaxLength(500);

        builder.HasOne(x => x.Employee)
            .WithMany()
            .HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.EmployeeId);

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
