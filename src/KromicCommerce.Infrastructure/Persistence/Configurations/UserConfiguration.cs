using KromicCommerce.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KromicCommerce.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Email).IsRequired().HasMaxLength(256);
        builder.Property(u => u.NormalizedEmail).IsRequired().HasMaxLength(256);
        builder.Property(u => u.PasswordHash).HasMaxLength(512);
        builder.Property(u => u.FirstName).HasMaxLength(100);
        builder.Property(u => u.LastName).HasMaxLength(100);
        builder.Property(u => u.PhoneNumber).HasMaxLength(20);
        builder.Property(u => u.Role)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(u => u.Username).HasMaxLength(50);
        builder.Property(u => u.NormalizedUsername).HasMaxLength(50);
        builder.Property(u => u.PasswordResetTokenHash).HasMaxLength(64);
        builder.Property(u => u.PasswordResetTokenExpiresAt);

        builder.Property(u => u.TokenVersion).IsRequired().HasDefaultValue(1);
        builder.Property(u => u.IsActive).IsRequired().HasDefaultValue(true);

        builder.Property(u => u.CreatedAtUtc).IsRequired();
        builder.Property(u => u.UpdatedAtUtc).IsRequired();

        // Unique indexes
        builder.HasIndex(u => u.NormalizedEmail).IsUnique().HasDatabaseName("ix_users_normalized_email");
        builder.HasIndex(u => u.NormalizedUsername)
            .IsUnique()
            .HasFilter("normalized_username IS NOT NULL")
            .HasDatabaseName("ix_users_normalized_username");
        builder.HasIndex(u => u.PhoneNumber)
            .IsUnique()
            .HasFilter("phone_number IS NOT NULL")
            .HasDatabaseName("ix_users_phone_number");

        // Navigation
        builder.HasMany(u => u.RefreshTokens)
            .WithOne(rt => rt.User)
            .HasForeignKey(rt => rt.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(u => u.ExternalLogins)
            .WithOne(el => el.User)
            .HasForeignKey(el => el.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(u => u.CustomerProfile)
            .WithOne(cp => cp.User)
            .HasForeignKey<CustomerProfile>(cp => cp.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Private backing field for navigation collections
        builder.Navigation(u => u.RefreshTokens).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(u => u.ExternalLogins).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Domain events are transient — never persisted
        builder.Ignore("_domainEvents");
    }
}
