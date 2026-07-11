using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Infrastructure.Persistence.Configurations;

public class PayrollCutoffPolicyConfiguration : IEntityTypeConfiguration<PayrollCutoffPolicy>
{
    public void Configure(EntityTypeBuilder<PayrollCutoffPolicy> builder)
    {
        builder.ToTable("PayrollCutoffPolicies", table =>
        {
            table.HasCheckConstraint(
                "CK_PayrollCutoffPolicies_FromDay",
                "[FromDay] BETWEEN 1 AND 31");

            table.HasCheckConstraint(
                "CK_PayrollCutoffPolicies_ToDay",
                "[ToDay] BETWEEN 1 AND 31");

            table.HasCheckConstraint(
                "CK_PayrollCutoffPolicies_DayOfMonth",
                "[DayOfMonth] IS NULL OR [DayOfMonth] BETWEEN 1 AND 31");

            table.HasCheckConstraint(
                "CK_PayrollCutoffPolicies_OffsetDays",
                "[OffsetDays] IS NULL OR [OffsetDays] >= 0");

            table.HasCheckConstraint(
                "CK_PayrollCutoffPolicies_EffectiveDates",
                "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(x => x.FromDay)
            .HasDefaultValue(1)
            .IsRequired();

        builder.Property(x => x.ToDay)
            .HasDefaultValue(30)
            .IsRequired();

        builder.Property(x => x.PolicyType)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.CutoffBasis)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.CutoffTime)
            .HasColumnType("time");

        builder.Property(x => x.EffectiveFrom)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(x => x.EffectiveTo)
            .HasColumnType("date");

        builder.Property(x => x.Notes)
            .HasMaxLength(1000);

        builder.Property(x => x.IsActive)
            .IsRequired();

        builder.HasIndex(x => new
        {
            x.CompanyId,
            x.IsActive,
            x.Name
        });

        builder.HasOne(x => x.Company)
            .WithMany(x => x.PayrollCutoffPolicies)
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}