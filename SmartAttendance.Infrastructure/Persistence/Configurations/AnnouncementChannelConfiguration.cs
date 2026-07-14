using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Infrastructure.Persistence.Configurations;

public class AnnouncementChannelConfiguration : IEntityTypeConfiguration<AnnouncementChannel>
{
    public void Configure(EntityTypeBuilder<AnnouncementChannel> builder)
    {
        builder.ToTable("AnnouncementChannels");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ChannelType).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.HasIndex(x => new { x.AnnouncementGroupId, x.ChannelType }).IsUnique();

        builder.HasOne(x => x.AnnouncementGroup)
            .WithMany(x => x.Channels)
            .HasForeignKey(x => x.AnnouncementGroupId)
            .OnDelete(DeleteBehavior.Cascade);

    }
}
