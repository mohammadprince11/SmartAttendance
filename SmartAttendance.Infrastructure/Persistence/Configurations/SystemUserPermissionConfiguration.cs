using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Infrastructure.Persistence.Configurations;

public class SystemUserPermissionConfiguration : IEntityTypeConfiguration<SystemUserPermission>
{
    public void Configure(EntityTypeBuilder<SystemUserPermission> builder)
    {
        builder.ToTable("SystemUserPermissions");

        builder.HasKey(x => x.Id);

        builder.HasOne(x => x.SystemUser)
            .WithMany(x => x.UserPermissions)
            .HasForeignKey(x => x.SystemUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Permission)
            .WithMany()
            .HasForeignKey(x => x.PermissionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.SystemUserId);
        builder.HasIndex(x => x.PermissionId);

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
