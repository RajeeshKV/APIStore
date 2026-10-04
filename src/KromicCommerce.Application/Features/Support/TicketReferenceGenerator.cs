namespace KromicCommerce.Application.Features.Support;

/// <summary>
/// Builds the externally quoted reference for a ticket or invoice.
///
/// Numbers come from a PostgreSQL sequence rather than a row count — see
/// <c>AppDbContext.NextTicketReferenceSequenceAsync</c> for why a count-based allocator
/// loses under concurrency.
///
/// The sequence is global but the number is prefixed with the year. That keeps references
/// sortable by era and human-readable without reintroducing a per-day allocation race: two
/// tickets created in the same second still receive distinct sequence values, so the
/// (TicketNumber) unique index can never be hit by a legitimate request.
/// </summary>
public sealed class TicketReferenceGenerator(IApplicationDbContext db, IOptions<SupportPolicyOptions> options)
{
    public Task<string> NextTicketNumberAsync(CancellationToken ct = default) =>
        FormatAsync(options.Value.TicketNumberPrefix, ct);

    public Task<string> NextInvoiceNumberAsync(CancellationToken ct = default) =>
        FormatAsync(options.Value.InvoiceNumberPrefix, ct);

    private async Task<string> FormatAsync(string prefix, CancellationToken ct)
    {
        var sequence = await db.NextTicketReferenceSequenceAsync(ct);
        var sanitizedPrefix = Sanitize(prefix);

        // UTC year, so a reference generated at 00:30 UTC on 1 January is unambiguous about
        // which cycle it belongs to.
        return $"{sanitizedPrefix}-{DateTime.UtcNow.Year}-{sequence:D6}";
    }

    /// <summary>Strips anything that would make a reference awkward to quote or search for.</summary>
    private static string Sanitize(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix)) return "TKT";

        var cleaned = new string(prefix.Trim().ToUpperInvariant()
            .Where(c => char.IsAsciiLetterOrDigit(c) || c == '-')
            .ToArray());

        return cleaned.Length == 0 ? "TKT" : cleaned;
    }
}