using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Infrastructure.Persistence.Configurations;

public class EmployeeFileRecordConfiguration : IEntityTypeConfiguration<EmployeeFileRecord>
{
    public void Configure(EntityTypeBuilder<EmployeeFileRecord> builder)
    {
        builder.ToTable("EmployeeFileRecords");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.RecordType).IsRequired();
        builder.Property(x => x.Title).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Subtitle).HasMaxLength(200);
        builder.Property(x => x.Country).HasMaxLength(100);
        builder.Property(x => x.RefNo).HasMaxLength(100);
        builder.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.Gpa).HasMaxLength(20);
        builder.Property(x => x.RefContactName).HasMaxLength(200);
        builder.Property(x => x.RefContactPosition).HasMaxLength(200);
        builder.Property(x => x.RefContactPhone).HasMaxLength(50);
        builder.Property(x => x.RefContactNote).HasMaxLength(500);
        builder.Property(x => x.Note).HasMaxLength(500);
        builder.Property(x => x.AttachmentName).HasMaxLength(260);
        builder.Property(x => x.AttachmentPath).HasMaxLength(500);
        builder.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.EmployeeId, x.RecordType });
        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
