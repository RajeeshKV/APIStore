namespace KromicCommerce.Contracts.Store;

public sealed record StorePolicyResponse(
    Guid Id,
    string PolicyType,
    string Title,
    string Content,
    bool IsPublished,
    DateTime UpdatedAtUtc);
