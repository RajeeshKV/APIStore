namespace KromicCommerce.Domain.Support;

/// <summary>Immutable input describing a piece of media to attach to a comment.</summary>
public sealed record TicketAttachmentRequest(
    TicketAttachmentKind Kind,
    string PublicId,
    string SecureUrl,
    string? Format,
    string? ContentType,
    int? Width,
    int? Height,
    int? DurationSeconds,
    long SizeBytes,
    string? AltText = null);