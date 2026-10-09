using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Infrastructure.Persistence.Configurations;

public class AnnouncementStudioProfileConfiguration : IEntityTypeConfiguration<AnnouncementStudioProfile>
{
    public void Configure(EntityTypeBuilder<AnnouncementStudioProfile> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Key).HasMaxLength(80);
        b.Property(x => x.DefinitionJson).HasMaxLength(100000);
        b.Property(x => x.Revision).IsConcurrencyToken();
        b.HasIndex(x => new { x.CompanyId, x.Key }).IsUnique();
        b.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
    }
}
public class AnnouncementStudioDesignConfiguration : IEntityTypeConfiguration<AnnouncementStudioDesign>
{
    public void Configure(EntityTypeBuilder<AnnouncementStudioDesign> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(150);
        b.Property(x => x.ContentType).HasMaxLength(30);
        b.Property(x => x.Data).HasMaxLength(5242880);
        b.HasIndex(x => new { x.CompanyId, x.IsActive });
        b.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
    }
}
