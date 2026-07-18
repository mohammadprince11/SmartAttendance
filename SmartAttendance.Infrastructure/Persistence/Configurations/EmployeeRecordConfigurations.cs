using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Infrastructure.Persistence.Configurations;

public class EmployeeEducationConfiguration : IEntityTypeConfiguration<EmployeeEducation>
{
    public void Configure(EntityTypeBuilder<EmployeeEducation> builder)
    {
        builder.ToTable("EmployeeEducations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Country).HasMaxLength(100);
        builder.Property(x => x.University).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Degree).HasMaxLength(120);
        builder.Property(x => x.Major).HasMaxLength(150);
        builder.Property(x => x.Note).HasMaxLength(500);
        builder.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.EmployeeId);
        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}

public class EmployeeExperienceConfiguration : IEntityTypeConfiguration<EmployeeExperience>
{
    public void Configure(EntityTypeBuilder<EmployeeExperience> builder)
    {
        builder.ToTable("EmployeeExperiences");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.CompanyName).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Country).HasMaxLength(100);
        builder.Property(x => x.JobTitle).HasMaxLength(150);
        builder.Property(x => x.Note).HasMaxLength(500);
        builder.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.EmployeeId);
        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}

public class EmployeeCertificateConfiguration : IEntityTypeConfiguration<EmployeeCertificate>
{
    public void Configure(EntityTypeBuilder<EmployeeCertificate> builder)
    {
        builder.ToTable("EmployeeCertificates");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(200);
        builder.Property(x => x.ReferenceNo).HasMaxLength(100);
        builder.Property(x => x.Note).HasMaxLength(500);
        builder.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.EmployeeId);
        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}

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
        builder.Property(x => x.Note).HasMaxLength(500);
        builder.Property(x => x.AttachmentName).HasMaxLength(260);
        builder.Property(x => x.AttachmentPath).HasMaxLength(500);
        builder.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.EmployeeId, x.RecordType });
        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
