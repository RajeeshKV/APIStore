using KromicCommerce.Domain.Sms;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KromicCommerce.Infrastructure.Persistence.Configurations;

internal sealed class OtpSendClaimConfiguration : IEntityTypeConfiguration<OtpSendClaim>
{
    public void Configure(EntityTypeBuilder<OtpSendClaim> builder)
    {
        builder.ToTable("otp_send_claims");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.PhoneNumber).IsRequired().HasMaxLength(16);
        builder.Property(c => c.Purpose).IsRequired().HasConversion<string>().HasMaxLength(50);
        builder.Property(c => c.CreatedAt).IsRequired();

        // The authoritative concurrency gate. Two concurrent requests for the same number and
        // purpose both reach INSERT; exactly one can satisfy this index, and the other is
        // rejected by the database rather than by a check that a second request could race past.
        //
        // Deliberately NOT filtered on an "active" flag: the claim row is the active thing, and
        // partial-filter reasoning here would allow two rows for the same key.
        builder.HasIndex(c => new { c.PhoneNumber, c.Purpose })
            .IsUnique()
            .HasDatabaseName("ix_otp_send_claims_phone_purpose");
    }
}