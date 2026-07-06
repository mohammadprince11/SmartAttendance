using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Infrastructure.Persistence.Configurations;

public class EmployeeViolationCaseConfiguration : IEntityTypeConfiguration<EmployeeViolationCase>
{
    public void Configure(EntityTypeBuilder<EmployeeViolationCase> builder)
    {
        builder.ToTable("EmployeeViolationCases");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ReferenceNo)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.ViolationCategory)
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(x => x.ViolationTitle)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(x => x.EventDate)
            .HasColumnType("date");

        builder.Property(x => x.Source)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.ActionStatus)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.ProposedAction)
            .HasMaxLength(300);

        builder.Property(x => x.FinalAction)
            .HasMaxLength(300);

        builder.Property(x => x.Notes)
            .HasMaxLength(1000);

        builder.HasIndex(x => x.ReferenceNo)
            .IsUnique();

        builder.HasIndex(x => x.EmployeeId);

        builder.HasOne(x => x.Employee)
            .WithMany()
            .HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
