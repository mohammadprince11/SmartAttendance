using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Domain.Enums;

namespace SmartAttendance.Infrastructure.Persistence.Configurations;

public class SystemUserPermissionConfiguration : IEntityTypeConfiguration<SystemUserPermission>
{
    public void Configure(EntityTypeBuilder<SystemUserPermission> builder)
    {
        builder.ToTable("SystemUserPermissions", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint(
                "CK_SystemUserPermissions_Effect",
                "[Effect] IN (1, 2)");

            tableBuilder.HasCheckConstraint(
                "CK_SystemUserPermissions_Validity",
                "[ValidToUtc] IS NULL OR [ValidFromUtc] IS NULL OR [ValidToUtc] > [ValidFromUtc]");

            tableBuilder.HasCheckConstraint(
                "CK_SystemUserPermissions_Scope",
                "(([ScopeType] = 1 AND [ScopeCompanyId] IS NULL AND [ScopeBranchId] IS NULL AND [ScopeDepartmentId] IS NULL AND [ScopeEmployeeId] IS NULL) OR " +
                "([ScopeType] = 2 AND [ScopeCompanyId] IS NOT NULL AND [ScopeBranchId] IS NULL AND [ScopeDepartmentId] IS NULL AND [ScopeEmployeeId] IS NULL) OR " +
                "([ScopeType] = 3 AND [ScopeCompanyId] IS NULL AND [ScopeBranchId] IS NOT NULL AND [ScopeDepartmentId] IS NULL AND [ScopeEmployeeId] IS NULL) OR " +
                "([ScopeType] = 4 AND [ScopeCompanyId] IS NULL AND [ScopeBranchId] IS NULL AND [ScopeDepartmentId] IS NOT NULL AND [ScopeEmployeeId] IS NULL) OR " +
                "([ScopeType] = 5 AND [ScopeCompanyId] IS NULL AND [ScopeBranchId] IS NULL AND [ScopeDepartmentId] IS NULL AND [ScopeEmployeeId] IS NOT NULL) OR " +
                "([ScopeType] = 6 AND [ScopeCompanyId] IS NULL AND [ScopeBranchId] IS NULL AND [ScopeDepartmentId] IS NULL AND [ScopeEmployeeId] IS NULL))");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Effect)
            .HasConversion<int>()
            .HasSentinel((PermissionEffect)0)
            .HasDefaultValue(PermissionEffect.Allow)
            .IsRequired();

        builder.Property(x => x.ScopeType)
            .HasConversion<int>()
            .HasSentinel((PeopleDataScopeType)0)
            .HasDefaultValue(PeopleDataScopeType.All)
            .IsRequired();

        builder.HasOne(x => x.SystemUser)
            .WithMany(x => x.UserPermissions)
            .HasForeignKey(x => x.SystemUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Permission)
            .WithMany()
            .HasForeignKey(x => x.PermissionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(x => x.ScopeCompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Branch>()
            .WithMany()
            .HasForeignKey(x => x.ScopeBranchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Department>()
            .WithMany()
            .HasForeignKey(x => x.ScopeDepartmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(x => x.ScopeEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.SystemUserId);
        builder.HasIndex(x => x.PermissionId);
        builder.HasIndex(x => x.ScopeCompanyId);
        builder.HasIndex(x => x.ScopeBranchId);
        builder.HasIndex(x => x.ScopeDepartmentId);
        builder.HasIndex(x => x.ScopeEmployeeId);

        builder.HasIndex(x => new
            {
                x.SystemUserId,
                x.PermissionId,
                x.Effect,
                x.ScopeType,
                x.ScopeCompanyId,
                x.ScopeBranchId,
                x.ScopeDepartmentId,
                x.ScopeEmployeeId,
                x.ValidFromUtc,
                x.ValidToUtc
            })
            .IsUnique()
            .HasDatabaseName("UX_SystemUserPermissions_EffectiveRule")
            .HasFilter("[IsDeleted] = 0");

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
