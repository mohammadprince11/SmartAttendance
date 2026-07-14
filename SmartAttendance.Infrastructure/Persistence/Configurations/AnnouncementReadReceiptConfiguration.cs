using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Infrastructure.Persistence.Configurations;

public class AnnouncementReadReceiptConfiguration : IEntityTypeConfiguration<AnnouncementReadReceipt>
{
    public void Configure(EntityTypeBuilder<AnnouncementReadReceipt> builder)
    {
        builder.ToTable("AnnouncementReadReceipts", table =>
        {
            table.HasCheckConstraint(
                "CK_AnnouncementReadReceipts_OpenCount",
                "[OpenCount] >= 1");
        });
        builder.HasKey(x => x.Id);

        builder.HasIndex(x => new { x.AnnouncementGroupId, x.EmployeeId }).IsUnique();
        builder.HasIndex(x => new { x.EmployeeId, x.FirstReadAtUtc });

        builder.HasOne(x => x.AnnouncementGroup)
            .WithMany(x => x.ReadReceipts)
            .HasForeignKey(x => x.AnnouncementGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Employee)
            .WithMany()
            .HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
