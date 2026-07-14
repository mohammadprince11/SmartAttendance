using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Infrastructure.Persistence.Configurations;

public class AnnouncementReactionConfiguration : IEntityTypeConfiguration<AnnouncementReaction>
{
    public void Configure(EntityTypeBuilder<AnnouncementReaction> builder)
    {
        builder.ToTable("AnnouncementReactions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ReactionType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.HasIndex(x => new { x.AnnouncementGroupId, x.EmployeeId }).IsUnique();
        builder.HasIndex(x => new { x.AnnouncementGroupId, x.ReactionType });

        builder.HasOne(x => x.AnnouncementGroup)
            .WithMany(x => x.Reactions)
            .HasForeignKey(x => x.AnnouncementGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Employee)
            .WithMany()
            .HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

    }
}
