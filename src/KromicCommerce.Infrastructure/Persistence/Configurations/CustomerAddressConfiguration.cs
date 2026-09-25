using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KromicCommerce.Infrastructure.Persistence.Configurations;

internal sealed class CustomerAddressConfiguration : IEntityTypeConfiguration<CustomerAddress>
{
    public void Configure(EntityTypeBuilder<CustomerAddress> builder)
    {
        builder.ToTable("customer_addresses");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Label).HasMaxLength(50);
        builder.Property(a => a.FirstName).IsRequired().HasMaxLength(100);
        builder.Property(a => a.LastName).IsRequired().HasMaxLength(100);
        builder.Property(a => a.Company).HasMaxLength(200);
        builder.Property(a => a.AddressLine1).IsRequired().HasMaxLength(300);
        builder.Property(a => a.AddressLine2).HasMaxLength(300);
        builder.Property(a => a.City).IsRequired().HasMaxLength(100);
        builder.Property(a => a.State).IsRequired().HasMaxLength(100);
        builder.Property(a => a.PostalCode).IsRequired().HasMaxLength(20);
        builder.Property(a => a.CountryCode).IsRequired().HasMaxLength(2);
        builder.Property(a => a.Phone).HasMaxLength(20);
        builder.Property(a => a.IsDefault).IsRequired().HasDefaultValue(false);
        builder.Property(a => a.CreatedAtUtc).IsRequired();
        builder.Property(a => a.UpdatedAtUtc).IsRequired();

        builder.HasIndex(a => a.CustomerId).HasDatabaseName("ix_customer_addresses_customer");
        builder.HasIndex(a => new { a.CustomerId, a.IsDefault })
            .HasDatabaseName("ix_customer_addresses_customer_default");
        builder.HasIndex(a => new { a.CustomerId, a.CreatedAtUtc })
            .HasDatabaseName("ix_customer_addresses_customer_created");
    }
}
