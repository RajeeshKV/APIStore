namespace KromicCommerce.IntegrationTests.Infrastructure;

/// <summary>
/// xUnit collection definition that shares the DatabaseFixture across all
/// integration test classes tagged with [Collection("Database")].
/// </summary>
[CollectionDefinition("Database")]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    // Intentionally empty — just the marker.
}
