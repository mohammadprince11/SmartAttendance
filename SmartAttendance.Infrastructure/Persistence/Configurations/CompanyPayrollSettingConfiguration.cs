using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Infrastructure.Persistence.Configurations;

public class CompanyPayrollSettingConfiguration : IEntityTypeConfiguration<CompanyPayrollSetting>
{
    public void Configure(EntityTypeBuilder<CompanyPayrollSetting> builder)
    {
        builder.ToTable("CompanyPayrollSettings", table =>
        {
            table.HasCheckConstraint(
                "CK_CompanyPayrollSettings_PeriodStartDay",
                "[PeriodStartDay] BETWEEN 1 AND 31");

            table.HasCheckConstraint(
                "CK_CompanyPayrollSettings_PeriodEndDay",
                "[PeriodEndDay] BETWEEN 1 AND 31");

            table.HasCheckConstraint(
                "CK_CompanyPayrollSettings_PaymentDay",
                "[PaymentDay] IS NULL OR [PaymentDay] BETWEEN 1 AND 31");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.PayrollFrequency)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.PeriodStartDay)
            .IsRequired();

        builder.Property(x => x.PeriodEndDay)
            .IsRequired();

        builder.Property(x => x.IsActive)
            .IsRequired();

        builder.HasIndex(x => x.CompanyId)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        builder.HasOne(x => x.Company)
            .WithOne(x => x.PayrollSettings)
            .HasForeignKey<CompanyPayrollSetting>(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}