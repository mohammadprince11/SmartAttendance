using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Infrastructure.Persistence.Configurations;

public class AnnouncementAuditLogConfiguration : IEntityTypeConfiguration<AnnouncementAuditLog>
{
    public void Configure(EntityTypeBuilder<AnnouncementAuditLog> builder)
    {
        builder.ToTable("AnnouncementAuditLogs");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.EntityName).HasMaxLength(120).IsRequired();
        builder.Property(x => x.EntityId).HasMaxLength(100);
        builder.Property(x => x.Action).HasMaxLength(100).IsRequired();
        builder.Property(x => x.UserName).HasMaxLength(150);
        builder.Property(x => x.IpAddress).HasMaxLength(80);

        builder.HasIndex(x => new { x.AnnouncementGroupId, x.OccurredAtUtc });
        builder.HasIndex(x => new { x.TranslationGroupId, x.OccurredAtUtc });
        builder.HasIndex(x => new { x.EntityName, x.EntityId, x.OccurredAtUtc });
    }
}
