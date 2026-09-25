namespace KromicCommerce.Domain.Store;

/// <summary>
/// An admin-managed store policy page (Terms, Privacy, Refund, etc.).
/// Policies are public when IsPublished = true.
/// The storefront renders policy content from the database — never from code.
/// Each PolicyType has at most one active policy (enforced at the application layer).
/// </summary>
public sealed class StorePolicy : AuditableEntity
{
    private StorePolicy() { } // EF constructor

    public static StorePolicy Create(PolicyType type, string title, string content)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Policy title is required.", nameof(title));
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Policy content is required.", nameof(content));

        return new StorePolicy
        {
            PolicyType = type,
            Title = title.Trim(),
            Content = content.Trim(),
            IsPublished = false
        };
    }

    public PolicyType PolicyType { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Content { get; private set; } = string.Empty;
    public bool IsPublished { get; private set; }

    // -----------------------------------------------------------------------
    // Behaviour
    // -----------------------------------------------------------------------

    public void Update(string title, string content)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Title required.", nameof(title));
        if (string.IsNullOrWhiteSpace(content)) throw new ArgumentException("Content required.", nameof(content));
        Title = title.Trim();
        Content = content.Trim();
    }

    public void Publish() => IsPublished = true;
    public void Unpublish() => IsPublished = false;
}
