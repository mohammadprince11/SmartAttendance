using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Infrastructure.Persistence.Configurations;

public class AnnouncementAudienceRuleConfiguration : IEntityTypeConfiguration<AnnouncementAudienceRule>
{
    public void Configure(EntityTypeBuilder<AnnouncementAudienceRule> builder)
    {
        builder.ToTable("AnnouncementAudienceRules", table =>
        {
            table.HasCheckConstraint(
                "CK_AnnouncementAudienceRules_Target",
                "(([AudienceType] = N'All' AND [CompanyId] IS NULL AND [BranchId] IS NULL AND [DepartmentId] IS NULL AND [PositionId] IS NULL AND [EmployeeId] IS NULL) OR " +
                "([AudienceType] = N'Company' AND [CompanyId] IS NOT NULL AND [BranchId] IS NULL AND [DepartmentId] IS NULL AND [PositionId] IS NULL AND [EmployeeId] IS NULL) OR " +
                "([AudienceType] = N'Branch' AND [CompanyId] IS NULL AND [BranchId] IS NOT NULL AND [DepartmentId] IS NULL AND [PositionId] IS NULL AND [EmployeeId] IS NULL) OR " +
                "([AudienceType] = N'Department' AND [CompanyId] IS NULL AND [BranchId] IS NULL AND [DepartmentId] IS NOT NULL AND [PositionId] IS NULL AND [EmployeeId] IS NULL) OR " +
                "([AudienceType] = N'Position' AND [CompanyId] IS NULL AND [BranchId] IS NULL AND [DepartmentId] IS NULL AND [PositionId] IS NOT NULL AND [EmployeeId] IS NULL) OR " +
                "([AudienceType] = N'Employee' AND [CompanyId] IS NULL AND [BranchId] IS NULL AND [DepartmentId] IS NULL AND [PositionId] IS NULL AND [EmployeeId] IS NOT NULL))");
        });
        builder.HasKey(x => x.Id);

        builder.Property(x => x.AudienceType).HasConversion<string>().HasMaxLength(30).IsRequired();

        builder.HasIndex(x => new { x.AnnouncementGroupId, x.IsExcluded, x.AudienceType });
        builder.HasIndex(x => x.CompanyId);
        builder.HasIndex(x => x.BranchId);
        builder.HasIndex(x => x.DepartmentId);
        builder.HasIndex(x => x.PositionId);
        builder.HasIndex(x => x.EmployeeId);

        builder.HasOne(x => x.AnnouncementGroup)
            .WithMany(x => x.AudienceRules)
            .HasForeignKey(x => x.AnnouncementGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Branch)
            .WithMany()
            .HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Department)
            .WithMany()
            .HasForeignKey(x => x.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Position)
            .WithMany()
            .HasForeignKey(x => x.PositionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Employee)
            .WithMany()
            .HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
