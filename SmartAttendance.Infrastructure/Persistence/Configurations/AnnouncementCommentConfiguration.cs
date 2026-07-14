using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Infrastructure.Persistence.Configurations;

public class AnnouncementCommentConfiguration : IEntityTypeConfiguration<AnnouncementComment>
{
    public void Configure(EntityTypeBuilder<AnnouncementComment> builder)
    {
        builder.ToTable("AnnouncementComments");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Body).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.HiddenBy).HasMaxLength(150);
        builder.Property(x => x.DeletedBy).HasMaxLength(150);
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => new { x.AnnouncementGroupId, x.CreatedAt });
        builder.HasIndex(x => new { x.EmployeeId, x.CreatedAt });

        builder.HasOne(x => x.AnnouncementGroup)
            .WithMany(x => x.Comments)
            .HasForeignKey(x => x.AnnouncementGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Employee)
            .WithMany()
            .HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

    }
}
