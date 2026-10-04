using System.Text.Json;
using KromicCommerce.Application.Abstractions.Email;
using KromicCommerce.Application.Features.Support;
using KromicCommerce.Infrastructure.Configuration;
using KromicCommerce.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KromicCommerce.Infrastructure.BackgroundServices;

/// <summary>
/// Closes resolved tickets that the customer never came back to.
///
/// WHY A PERSISTED DEADLINE. Candidates are not found by loading Resolved tickets and doing
/// date arithmetic on LastUserActivityAtUtc. Instead the resolve handler stamps
/// <c>AutoCloseAtUtc</c> onto the ticket, and this worker asks one question:
/// <c>Status = 'Resolved' AND AutoCloseAtUtc &lt;= now</c>. That is a range scan on a partial
/// index over a non-nullable timestamp, so a cycle costs the same whether the deployment holds
/// ten tickets or a hundred thousand — and no rows are loaded to be discarded.
///
/// WHY CUSTOMER SILENCE ONLY. The deadline is re-stamped when the customer posts, not when an
/// administrator posts. An admin note eight hours after resolution keeps the thread visible on
/// screen but does not reset the clock; otherwise a busy support queue would keep abandoned
/// conversations open indefinitely, which is exactly what the rule is meant to prevent.
///
/// MULTI-INSTANCE SAFETY. Two API instances run this loop. Both may select the same row, and
/// both will call Close(). The second one throws from Ticket.Close because the status is no
/// longer Resolved — so every failure is swallowed per row and the first writer wins. An
/// in-memory lock would not help across processes; a compare-and-set UPDATE would, and is the
/// natural upgrade if the duplicate-notification volume ever becomes a problem.
/// </summary>
internal sealed class TicketAutoCloseWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<BackgroundWorkerOptions> workerOptions,
    ILogger<TicketAutoCloseWorker> logger) : BackgroundService
{
    /// <summary>
    /// Rows claimed per cycle. Bounded so one pass cannot hold a transaction open long enough
    /// to collide with interactive traffic on the same rows.
    /// </summary>
    private const int BatchSize = 50;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "TicketAutoCloseWorker started. Interval: {Interval}s",
            workerOptions.Value.TicketAutoCloseIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var closed = await CloseIdleAsync(stoppingToken);
                if (closed > 0)
                    logger.LogInformation("TicketAutoCloseWorker closed {Count} idle ticket(s).", closed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A failing cycle must not kill the worker. A support inbox that silently stops
                // auto-closing would look identical to "no tickets need closing" for ever.
                logger.LogError(ex, "TicketAutoCloseWorker cycle failed; retrying next cycle.");
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(workerOptions.Value.TicketAutoCloseIntervalSeconds),
                    stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("TicketAutoCloseWorker stopped.");
    }

    private async Task<int> CloseIdleAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTime.UtcNow;

        var due = await db.Tickets
            .Where(t => t.Status == TicketStatus.Resolved
                        && t.AutoCloseAtUtc != null
                        && t.AutoCloseAtUtc <= now)
            .OrderBy(t => t.AutoCloseAtUtc)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (due.Count == 0) return 0;

        var closedCount = 0;

        foreach (var ticket in due)
        {
            // Re-check under the loaded row. A customer reply that landed between the SELECT
            // and here will have pushed the deadline out, and closing it now would be exactly
            // the bug this worker exists to avoid.
            if (!ticket.IsAutoCloseDue(now)) continue;

            try
            {
                var customer = await db.Users
                    .AsNoTracking()
                    .FirstAsync(u => u.Id == ticket.CustomerId, ct);

                // The appended history row is tracked explicitly. Left implicit, it would be discovered as an
                // existing row and the save would fail with a concurrency exception — which the
                // catch below would swallow, leaving the ticket stuck Resolved forever.
                db.TicketStatusHistory.Add(
                    ticket.Close(
                        TicketTransitionActor.System,
                        actorId: null,
                        note: "Automatically closed after the inactivity window elapsed."));

                // The customer is told their conversation was closed automatically, so an
                // unexpected silence is not mistaken for being ignored.
                db.OutboxEvents.Add(OutboxEvent.Create(TicketOutbox.Closed, JsonSerializer.Serialize(
                    new TicketClosedPayload(
                        ticket.Id,
                        customer.FullName,
                        customer.Email,
                        ticket.TicketNumber,
                        ticket.Subject,
                        TicketTransitionActor.System.ToString(),
                        "Automatically closed after the inactivity window elapsed.",
                        now))));

                closedCount++;
            }
            catch (InvalidOperationException ex)
            {
                // Another instance closed it between the query and here. Expected under
                // multi-instance deployment, not an error.
                logger.LogDebug(
                    "Ticket {TicketNumber} was closed concurrently: {Reason}",
                    ticket.TicketNumber, ex.Message);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex, "Failed to auto-close ticket {TicketNumber}; it will be retried next cycle.",
                    ticket.TicketNumber);
            }
        }

        if (closedCount > 0)
            await db.SaveChangesAsync(ct);

        return closedCount;
    }
}