namespace KromicCommerce.Domain.Support;

/// <summary>Media attached to a ticket comment (screenshot of an error, photo of a fault, short clip).</summary>
public sealed class TicketAttachment : AuditableEntity
{
    public const int UrlMaxLength = 1024;
    public const int PublicIdMaxLength = 512;
    public const int AltTextMaxLength = 500;

    /// <summary>Matches the product-image ceiling so a ticket thread cannot become a file dump.</summary>
    public const long MaxImageBytes = 10 * 1024 * 1024;

    /// <summary>Videos are capped lower. They are a convenience, not the primary evidence path.</summary>
    public const long MaxVideoBytes = 25 * 1024 * 1024;

    private TicketAttachment() { } // EF constructor

    public static TicketAttachment Create(
        Guid ticketCommentId,
        TicketAttachmentRequest request,
        int sortOrder)
    {
        ArgumentNullException.ThrowIfNull(request);

        var kind = request.Kind;

        var maxBytes = kind == TicketAttachmentKind.Video ? MaxVideoBytes : MaxImageBytes;
        if (request.SizeBytes > maxBytes)
            throw new ArgumentException(
                $"A {kind.ToString().ToLowerInvariant()} attachment may not exceed {maxBytes} bytes.",
                nameof(request));

        return new TicketAttachment
        {
            TicketCommentId = ticketCommentId,
            Kind = kind,
            PublicId = request.PublicId.Trim(),
            SecureUrl = request.SecureUrl.Trim(),
            Format = request.Format?.Trim(),
            ContentType = request.ContentType?.Trim(),
            Width = request.Width,
            Height = request.Height,
            DurationSeconds = request.DurationSeconds,
            SizeBytes = request.SizeBytes,
            AltText = request.AltText?.Trim(),
            SortOrder = sortOrder
        };
    }

    public Guid TicketCommentId { get; private set; }

    public TicketAttachmentKind Kind { get; private set; }

    /// <summary>Cloudinary public_id — needed to delete or transform the asset later.</summary>
    public string PublicId { get; private set; } = string.Empty;

    /// <summary>Delivery URL. Videos render through this directly; images use Cloudinary transforms.</summary>
    public string SecureUrl { get; private set; } = string.Empty;

    public string? Format { get; private set; }
    public string? ContentType { get; private set; }
    public int? Width { get; private set; }
    public int? Height { get; private set; }

    /// <summary>Video length in whole seconds. Null for images.</summary>
    public int? DurationSeconds { get; private set; }

    public long SizeBytes { get; private set; }
    public string? AltText { get; private set; }
    public int SortOrder { get; private set; }

    // Navigation
    public TicketComment Comment { get; private set; } = null!;
}