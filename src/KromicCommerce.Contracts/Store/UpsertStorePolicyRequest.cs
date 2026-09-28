using KromicCommerce.Domain.Store;

namespace KromicCommerce.Contracts.Store;

public sealed record UpsertStorePolicyRequest(
    PolicyType PolicyType,
    string Title,
    string Content,
    bool IsPublished = false);
