using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KromicCommerce.Infrastructure.Persistence.Configurations;

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");
        builder.HasKey(rt => rt.Id);

        builder.Property(rt => rt.TokenHash).IsRequired().HasMaxLength(128);
        builder.Property(rt => rt.DeviceHint).HasMaxLength(512);
        builder.Property(rt => rt.CreatedAt).IsRequired();
        builder.Property(rt => rt.ExpiresAt).IsRequired();

        // Lookup by hash is the primary access pattern
        builder.HasIndex(rt => rt.TokenHash).IsUnique().HasDatabaseName("ix_refresh_tokens_hash");
        builder.HasIndex(rt => new { rt.UserId, rt.RevokedAt }).HasDatabaseName("ix_refresh_tokens_user_revoked");
    }
}
