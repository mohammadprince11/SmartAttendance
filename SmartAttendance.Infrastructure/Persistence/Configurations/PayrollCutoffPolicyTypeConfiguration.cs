using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Infrastructure.Persistence.Configurations;

public class PayrollCutoffPolicyTypeConfiguration : IEntityTypeConfiguration<PayrollCutoffPolicyType>
{
    public void Configure(EntityTypeBuilder<PayrollCutoffPolicyType> builder)
    {
        builder.ToTable("PayrollCutoffPolicyTypes");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.PolicyType)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.HasIndex(x => new
        {
            x.PayrollCutoffPolicyId,
            x.PolicyType
        })
        .IsUnique();

        builder.HasOne(x => x.PayrollCutoffPolicy)
            .WithMany(x => x.PolicyTypes)
            .HasForeignKey(x => x.PayrollCutoffPolicyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}