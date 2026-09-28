using KromicCommerce.Domain.Store;

namespace KromicCommerce.Contracts.Store;

public sealed record StorePolicyResponse(
    Guid Id,
    PolicyType PolicyType,
    string Title,
    string Content,
    bool IsPublished,
    DateTime UpdatedAtUtc);
