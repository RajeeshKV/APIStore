using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KromicCommerce.Infrastructure.Persistence.Configurations;

internal sealed class ExternalLoginConfiguration : IEntityTypeConfiguration<ExternalLogin>
{
    public void Configure(EntityTypeBuilder<ExternalLogin> builder)
    {
        builder.ToTable("external_logins");
        builder.HasKey(el => el.Id);

        builder.Property(el => el.Provider).IsRequired().HasMaxLength(50);
        builder.Property(el => el.ProviderSubject).IsRequired().HasMaxLength(256);
        builder.Property(el => el.ProviderEmail).HasMaxLength(256);
        builder.Property(el => el.LinkedAt).IsRequired();

        // Stable identity key: provider + subject (not email)
        builder.HasIndex(el => new { el.Provider, el.ProviderSubject })
            .IsUnique()
            .HasDatabaseName("ix_external_logins_provider_subject");
    }
}
