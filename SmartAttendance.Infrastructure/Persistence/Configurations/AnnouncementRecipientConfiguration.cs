using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Infrastructure.Persistence.Configurations;

public class AnnouncementRecipientConfiguration : IEntityTypeConfiguration<AnnouncementRecipient>
{
    public void Configure(EntityTypeBuilder<AnnouncementRecipient> builder)
    {
        builder.ToTable("AnnouncementRecipients");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.SourceSummary).HasMaxLength(1000);
        builder.HasIndex(x => new { x.AnnouncementGroupId, x.EmployeeId }).IsUnique();
        builder.HasIndex(x => new { x.EmployeeId, x.ResolvedAtUtc });

        builder.HasOne(x => x.AnnouncementGroup)
            .WithMany(x => x.Recipients)
            .HasForeignKey(x => x.AnnouncementGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Employee)
            .WithMany()
            .HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

    }
}
