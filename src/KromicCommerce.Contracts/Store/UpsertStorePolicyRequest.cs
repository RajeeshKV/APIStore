namespace KromicCommerce.Contracts.Store;

public sealed record UpsertStorePolicyRequest(
    string PolicyType,
    string Title,
    string Content,
    bool IsPublished = false);
