using KromicCommerce.Infrastructure.Configuration;

namespace KromicCommerce.UnitTests.Infrastructure;

/// <summary>
/// Verifies migration-related AppOptions behavior.
/// </summary>
public sealed class AppOptionsMigrateTests
{
    [Fact]
    public void Migrate_defaults_to_false()
    {
        // Default-constructed options must have Migrate = false
        // so local development does not auto-migrate accidentally.
        var opts = new AppOptions
        {
            ApiBaseUrl   = "http://localhost:5000",
            FrontendUrl  = "http://localhost:3000",
            Environment  = "Development",
            BootstrapSecret = "secret"
        };

        opts.Migrate.Should().BeFalse(
            "local development should not auto-migrate unless explicitly enabled");
    }

    [Fact]
    public void Migrate_true_when_explicitly_set()
    {
        var opts = new AppOptions
        {
            ApiBaseUrl   = "http://localhost:5000",
            FrontendUrl  = "http://localhost:3000",
            Environment  = "Production",
            BootstrapSecret = "secret",
            Migrate = true
        };

        opts.Migrate.Should().BeTrue();
    }

    [Fact]
    public void Migrate_is_independent_of_IsProduction()
    {
        // Production app with migration disabled (e.g. manual control window)
        var opts = new AppOptions
        {
            ApiBaseUrl   = "https://api.example.com",
            FrontendUrl  = "https://example.com",
            Environment  = "Production",
            BootstrapSecret = "secret",
            Migrate = false
        };

        opts.IsProduction.Should().BeTrue();
        opts.Migrate.Should().BeFalse();
    }

    [Fact]
    public void Development_app_can_enable_migration()
    {
        // A developer may opt in for local integration testing
        var opts = new AppOptions
        {
            ApiBaseUrl   = "http://localhost:5000",
            FrontendUrl  = "http://localhost:3000",
            Environment  = "Development",
            BootstrapSecret = "secret",
            Migrate = true
        };

        opts.IsDevelopment.Should().BeTrue();
        opts.Migrate.Should().BeTrue();
    }
}
