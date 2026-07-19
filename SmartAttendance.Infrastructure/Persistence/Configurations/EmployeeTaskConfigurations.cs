using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Infrastructure.Persistence.Configurations;

public class HrTaskTemplateConfiguration : IEntityTypeConfiguration<HrTaskTemplate>
{
    public void Configure(EntityTypeBuilder<HrTaskTemplate> builder)
    {
        builder.ToTable("HrTaskTemplates");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.AssigneeRole).HasMaxLength(100);
        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}

public class EmployeeTaskConfiguration : IEntityTypeConfiguration<EmployeeTask>
{
    public void Configure(EntityTypeBuilder<EmployeeTask> builder)
    {
        builder.ToTable("EmployeeTasks");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.AssigneeRole).HasMaxLength(100);
        builder.Property(x => x.CompletedBy).HasMaxLength(150);
        builder.Property(x => x.Note).HasMaxLength(500);
        builder.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.EmployeeId, x.ProcessType });
        builder.HasIndex(x => x.IsDone);
        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
