using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Infrastructure.Persistence.Configurations;

public class EmployeeFinancialInfoConfiguration : IEntityTypeConfiguration<EmployeeFinancialInfo>
{
    public void Configure(EntityTypeBuilder<EmployeeFinancialInfo> builder)
    {
        builder.ToTable("EmployeeFinancialInfos");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Currency).HasMaxLength(10);
        builder.Property(x => x.SalaryScale).HasMaxLength(150);
        builder.Property(x => x.SocialSecurityType).HasMaxLength(100);
        builder.Property(x => x.SocialSecurityNo).HasMaxLength(50);
        builder.Property(x => x.TaxFile).HasMaxLength(150);
        builder.Property(x => x.TaxNo).HasMaxLength(50);
        builder.Property(x => x.EndOfServiceSetup).HasMaxLength(150);
        builder.Property(x => x.PaymentMethod).HasMaxLength(50);
        builder.Property(x => x.BankName).HasMaxLength(200);
        builder.Property(x => x.BankBranch).HasMaxLength(200);
        builder.Property(x => x.UnitNo).HasMaxLength(50);
        builder.Property(x => x.Iban).HasMaxLength(50);
        builder.Property(x => x.CardNo).HasMaxLength(50);
        builder.Property(x => x.MxpAccount).HasMaxLength(50);
        builder.Property(x => x.BankCommitmentFileName).HasMaxLength(260);
        builder.Property(x => x.BankCommitmentFilePath).HasMaxLength(500);
        builder.Property(x => x.AttachmentName).HasMaxLength(260);
        builder.Property(x => x.AttachmentPath).HasMaxLength(500);

        foreach (var money in new[]
        {
            nameof(EmployeeFinancialInfo.BasicSalary), nameof(EmployeeFinancialInfo.DailySalary),
            nameof(EmployeeFinancialInfo.HourlyRate), nameof(EmployeeFinancialInfo.SocialSecuritySalary),
            nameof(EmployeeFinancialInfo.PreviousTaxSalary), nameof(EmployeeFinancialInfo.PreviousTaxExemption),
            nameof(EmployeeFinancialInfo.PreviousTaxAmount), nameof(EmployeeFinancialInfo.PreviousMinSalary),
            nameof(EmployeeFinancialInfo.PreviousMinTaxAmount)
        })
        {
            builder.Property(money).HasColumnType("decimal(18,4)");
        }

        // One financial row per employee.
        builder.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.EmployeeId).IsUnique();
        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
