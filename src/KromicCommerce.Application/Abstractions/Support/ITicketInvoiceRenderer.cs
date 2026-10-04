namespace KromicCommerce.Application.Abstractions.Support;

/// <summary>
/// Renders a <see cref="InvoiceContent"/> snapshot into a PDF.
///
/// Lives behind an interface because rendering is deliberately asynchronous and is done by a
/// background worker: a slow font stack, a cold JIT or a hung renderer must not make the
/// admin's "Resolve" request time out. The abstraction is what lets that move to a worker
/// without the resolve handler knowing anything about it.
///
/// SDK types must not leak past this boundary — only the bytes do.
/// </summary>
public interface ITicketInvoiceRenderer
{
    Task<InvoiceRenderResult> RenderAsync(InvoiceRenderRequest request, CancellationToken ct = default);
}

/// <summary>
/// Frozen render input. <see cref="InvoiceContent"/> supplies the figures and the presentation
/// wording; the flags come from the template revision applied at queue time and are passed
/// alongside because they are presentation, not content.
///
/// <see cref="InvoiceNumber"/> lives here rather than on <see cref="InvoiceContent"/> because it
/// is the revision's external reference — the same content can be rendered under two invoice
/// numbers across two revisions, and the document must say which.
/// </summary>
public sealed record InvoiceRenderRequest(
    InvoiceContent Content,
    string InvoiceNumber,
    string TemplateName,
    bool ShowLineItemTable = true,
    bool ShowIssuerIdentity = true,
    bool ShowNotes = true,
    bool ShowTerms = true);

/// <summary>
/// Outcome of a render attempt. Failure is data, not an exception, because the worker must
/// record the reason on the invoice row and retry rather than crash its polling loop.
/// </summary>
public sealed record InvoiceRenderResult(
    bool Success,
    byte[]? PdfBytes,
    string FileName,
    string? ErrorMessage)
{
    public static InvoiceRenderResult Ok(byte[] pdf, string fileName) =>
        new(true, pdf, fileName, null);

    public static InvoiceRenderResult Failed(string error) =>
        new(false, null, string.Empty, error);
}