using Asp.Versioning;
using KromicCommerce.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KromicCommerce.Api.Controllers.V1;

/// <summary>
/// Lightweight health endpoint used by Render and Supabase keep-alive pings.
/// Both GET and HEAD are supported — HEAD is preferred for ping-only checks
/// since it avoids transmitting a response body.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/health")]
public sealed class HealthController(AppDbContext db, ILogger<HealthController> logger)
    : ControllerBase
{
    /// <summary>
    /// Returns application and database health.
    /// Executes SELECT 1 against PostgreSQL to confirm the connection is live.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var (dbOk, dbError) = await CheckDatabaseAsync(cancellationToken);

        var response = new HealthResponse(
            Status: dbOk ? "healthy" : "degraded",
            Database: dbOk ? "ok" : "unavailable",
            Error: dbError);

        return dbOk ? Ok(response) : StatusCode(StatusCodes.Status503ServiceUnavailable, response);
    }

    /// <summary>
    /// HEAD variant — identical check but no response body.
    /// Returns 200 when healthy, 503 when the database is unreachable.
    /// </summary>
    [HttpHead]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Head(CancellationToken cancellationToken)
    {
        var (dbOk, _) = await CheckDatabaseAsync(cancellationToken);
        return dbOk ? Ok() : StatusCode(StatusCodes.Status503ServiceUnavailable);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private async Task<(bool Ok, string? Error)> CheckDatabaseAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            // Raw SELECT 1 — minimal round-trip, no ORM overhead
            await db.Database
                .ExecuteSqlRawAsync("SELECT 1", cancellationToken);

            return (true, null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Health check: database connectivity failed");
            return (false, ex.Message);
        }
    }
}

/// <summary>Response body for the GET health endpoint.</summary>
/// <param name="Status">Overall status: "healthy" or "degraded".</param>
/// <param name="Database">Database probe result: "ok" or "unavailable".</param>
/// <param name="Error">Error message when the database probe fails, otherwise null.</param>
internal sealed record HealthResponse(string Status, string Database, string? Error);
