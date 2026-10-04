using System.Security.Cryptography;
using System.Text.Json;
using KromicCommerce.Application.Abstractions.Support;
using KromicCommerce.Application.Features.Support;
using KromicCommerce.Infrastructure.Configuration;
using KromicCommerce.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KromicCommerce.Infrastructure.BackgroundServices;

/// <summary>
/// Renders queued invoice revisions to PDF, then queues the customer email.
///
/// WHY A POLLING WORKER. The alternative — rendering inside the resolve request — makes an
/// administrative action's latency depend on a PDF writer, and makes a renderer fault look to
/// the caller like a failed resolution. Queueing a Pending row instead means resolving a ticket
/// is a database write with a bounded cost, and the document is produced afterwards whether or
/// not anybody is watching.
///
/// WHY THE TABLE IS THE QUEUE. Work is claimed by selecting Pending rows rather than by
/// publishing to an in-memory channel. That survives restarts, needs no broker, and means a
/// crash between "invoice queued" and "invoice rendered" costs nothing: the row is still Pending
/// and the next cycle picks it up. It also means a multi-instance deployment needs no coordination
/// beyond the per-row attempt counter.
///
/// THE ADMIN-EDIT WINDOW IS REAL BUT SHORT. The worker calls TryBeginGeneration before doing any
/// work, which increments GenerationAttempts. An administrator overriding a queued invoice wins
/// only if they beat this worker, and the API returns 409 once rendering has started — so the
/// behaviour is explicit rather than racy-and-sometimes-silently-ignored.
/// </summary>
internal sealed class TicketInvoiceWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<BackgroundWorkerOptions> workerOptions,
    ILogger<TicketInvoiceWorker> logger) : BackgroundService
{
    /// <summary>
    /// Rows claimed per cycle. Deliberately small: rendering is CPU-bound and synchronous inside
    /// the request, so a large batch would hold a connection and starve the pool that interactive
    /// traffic needs.
    /// </summary>
    private const int BatchSize = 10;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "TicketInvoiceWorker started. Interval: {Interval}s",
            workerOptions.Value.TicketInvoiceIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var rendered = await RenderBatchAsync(stoppingToken);
                if (rendered > 0)
                    logger.LogInformation("TicketInvoiceWorker rendered {Count} invoice(s).", rendered);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A transient fault must not stop the loop. Invoices would otherwise silently
                // stop being produced with nothing in the logs but one error line.
                logger.LogError(ex, "TicketInvoiceWorker cycle failed; retrying next cycle.");
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(workerOptions.Value.TicketInvoiceIntervalSeconds),
                    stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("TicketInvoiceWorker stopped.");
    }

    private async Task<int> RenderBatchAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var renderer = scope.ServiceProvider.GetRequiredService<ITicketInvoiceRenderer>();

        var pending = await db.TicketInvoices
            .Where(i => i.Status == TicketInvoiceStatus.Pending)
            .OrderBy(i => i.QueuedAtUtc)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (pending.Count == 0) return 0;

        var rendered = 0;
        var claimed = false;

        foreach (var invoice in pending)
        {
            if (ct.IsCancellationRequested) break;

            // Claim the attempt before doing the work. Once this returns false another instance
            // owns the row, or the attempt budget is spent — rendering twice would produce two
            // documents for one revision.
            if (!invoice.TryBeginGeneration()) continue;
            claimed = true;

            try
            {
                var request = await BuildRenderRequest(db, invoice, ct);
                var result = await renderer.RenderAsync(request, ct);

                if (!result.Success || result.PdfBytes is null || result.PdfBytes.Length == 0)
                {
                    invoice.MarkFailed(result.ErrorMessage ?? "The renderer produced no document.");

                    logger.LogWarning(
                        "Invoice {InvoiceNumber} render failed (attempt {Attempt}/{Max}): {Reason}",
                        invoice.InvoiceNumber,
                        invoice.GenerationAttempts,
                        TicketInvoice.MaxGenerationAttempts,
                        invoice.FailureReason);

                    continue;
                }

                invoice.MarkGenerated(result.PdfBytes, result.FileName, Checksum(result.PdfBytes));
                rendered++;

                await QueueMailAsync(db, invoice, ct);
            }
            catch (Exception ex)
            {
                invoice.MarkFailed(ex.Message);
                logger.LogError(
                    ex, "Unexpected failure rendering invoice {InvoiceNumber}.", invoice.InvoiceNumber);
            }
        }

        // Persisted in one transaction per batch. A crash here loses the rendered bytes and the
        // attempt counter together, so the revision is retried from Pending rather than being
        // left claiming an attempt it never used.
        if (claimed)
            await db.SaveChangesAsync(ct);

        return rendered;
    }

    /// <summary>
    /// Enqueues the invoice email rather than sending it.
    ///
    /// Whether the mail actually goes out is the merchant's decision, and that decision lives in
    /// SupportSettings. Keeping the check in the outbox dispatcher means the toggle can be flipped
    /// between rendering and sending — which is exactly when a merchant would flip it — and the
    /// worker stays out of the policy question entirely.
    /// </summary>
    private static async Task QueueMailAsync(AppDbContext db, TicketInvoice invoice, CancellationToken ct)
    {
        var ticket = await db.Tickets
            .Include(t => t.Customer)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == invoice.TicketId, ct);

        if (ticket is null)
        {
            // The invoice row cascades with its ticket, so this only happens if the ticket went
            // away between the claim query and here. There is nobody to mail.
            return;
        }

        db.OutboxEvents.Add(OutboxEvent.Create(
            TicketOutbox.InvoiceGenerated,
            JsonSerializer.Serialize(new TicketInvoiceGeneratedPayload(
                ticket.Id,
                invoice.Id,
                ticket.CustomerId,
                ticket.Customer?.FullName ?? string.Empty,
                ticket.Customer?.Email ?? string.Empty,
                invoice.InvoiceNumber,
                ticket.TicketNumber,
                invoice.Content.OrderNumber,
                invoice.Content.CurrencyCode,
                invoice.Content.GrandTotal,
                invoice.FileName,
                invoice.EmailedToCustomer,
                DateTime.UtcNow))));
    }

    /// <summary>
    /// Builds the render request from the frozen content plus the template's presentation flags.
    ///
    /// The flags are read at render time rather than copied onto the invoice row. A template edit
    /// landing between queueing and rendering is a sub-second window, and honouring the newer
    /// template is the less surprising outcome — the figures, which are what matter, were frozen
    /// at queue time either way.
    /// </summary>
    private static async Task<InvoiceRenderRequest> BuildRenderRequest(
        AppDbContext db, TicketInvoice invoice, CancellationToken ct)
    {
        var template = invoice.TemplateId is { } templateId
            ? await db.InvoiceTemplates
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == templateId, ct)
            : await db.InvoiceTemplates
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.IsDefault, ct);

        return new InvoiceRenderRequest(
            invoice.Content,
            invoice.InvoiceNumber,
            template?.Name ?? "Standard invoice",
            template?.ShowLineItemTable ?? true,
            template?.ShowIssuerIdentity ?? true,
            template?.ShowNotes ?? true,
            template?.ShowTerms ?? true);
    }

    internal static string Checksum(byte[] pdf) =>
        Convert.ToHexString(SHA256.HashData(pdf)).ToLowerInvariant();
}