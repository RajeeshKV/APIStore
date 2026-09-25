using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KromicCommerce.Infrastructure.Persistence.Configurations;

internal sealed class OtpRequestConfiguration : IEntityTypeConfiguration<OtpRequest>
{
    public void Configure(EntityTypeBuilder<OtpRequest> builder)
    {
        builder.ToTable("otp_requests");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.PhoneNumber).IsRequired().HasMaxLength(20);
        builder.Property(o => o.OtpHash).IsRequired().HasMaxLength(128);
        builder.Property(o => o.Purpose)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50);
        builder.Property(o => o.CreatedAt).IsRequired();
        builder.Property(o => o.ExpiresAt).IsRequired();
        builder.Property(o => o.Attempts).IsRequired().HasDefaultValue(0);

        // Lookup: latest OTP for a phone+purpose combination
        builder.HasIndex(o => new { o.PhoneNumber, o.Purpose, o.CreatedAt })
            .HasDatabaseName("ix_otp_requests_phone_purpose_created");
    }
}
