using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Infrastructure.Persistence.Configurations;

public class UserNotificationRecipientConfiguration : IEntityTypeConfiguration<UserNotificationRecipient>
{
    public void Configure(EntityTypeBuilder<UserNotificationRecipient> builder)
    {
        builder.ToTable("UserNotificationRecipients");
        builder.HasKey(x => x.Id);

        builder.HasIndex(x => new { x.UserNotificationId, x.EmployeeId }).IsUnique();
        builder.HasIndex(x => new { x.EmployeeId, x.IsRead, x.CreatedAt });

        builder.HasOne(x => x.UserNotification)
            .WithMany(x => x.Recipients)
            .HasForeignKey(x => x.UserNotificationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Employee)
            .WithMany()
            .HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

    }
}
