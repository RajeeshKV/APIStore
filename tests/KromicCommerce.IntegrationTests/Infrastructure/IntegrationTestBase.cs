namespace KromicCommerce.IntegrationTests.Infrastructure;

/// <summary>
/// Base class for all integration tests that require the database fixture.
/// Calls Skip.If (Xunit.SkippableFact) when Docker is not available so
/// tests are marked Skipped rather than Failed in environments without Docker.
/// </summary>
public abstract class IntegrationTestBase
{
    protected readonly DatabaseFixture Db;

    protected IntegrationTestBase(DatabaseFixture db)
    {
        Db = db;

        // Skip.If throws SkipException which xunit interprets as a skipped test.
        // This must run in the constructor so that the test body never executes.
        Skip.If(!db.IsAvailable, db.UnavailableReason ?? "Docker not available.");
    }
}
