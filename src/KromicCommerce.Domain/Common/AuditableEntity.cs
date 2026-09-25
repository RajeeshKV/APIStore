namespace KromicCommerce.Domain.Common;

/// <summary>
/// Extends Entity with auditing timestamps and actor tracking.
/// All timestamps are stored in UTC.
/// </summary>
public abstract class AuditableEntity : Entity
{
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>User ID or system actor that created this record.</summary>
    public string? CreatedBy { get; set; }

    /// <summary>User ID or system actor that last modified this record.</summary>
    public string? UpdatedBy { get; set; }
}
