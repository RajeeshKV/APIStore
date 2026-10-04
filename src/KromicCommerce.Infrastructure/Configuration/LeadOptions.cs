using System.ComponentModel.DataAnnotations;

namespace KromicCommerce.Infrastructure.Configuration;

/// <summary>
/// Lead-capture configuration.
///
/// Bound without <c>ValidateOnStart</c> on purpose. The ingest endpoint must stay available even
/// when no notification address is configured: the lead is still stored, and the gap is logged
/// per submission. Refusing to boot would take the marketing page offline over a mail setting.
/// </summary>
public sealed class LeadOptions
{
    public const string SectionName = "Leads";

    /// <summary>
    /// Where lead notifications are delivered. Environment variable:
    /// <c>Leads__NotificationEmail</c>. Never sourced from a request body.
    /// </summary>
    [EmailAddress]
    public string? NotificationEmail { get; set; }

    /// <summary>Display name for the notification recipient.</summary>
    [StringLength(120)]
    public string? NotificationName { get; set; }
}