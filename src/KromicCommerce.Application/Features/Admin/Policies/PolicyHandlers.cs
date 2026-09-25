using Microsoft.Extensions.Caching.Memory;

namespace KromicCommerce.Application.Features.Admin.Policies;

public sealed record GetPublicPoliciesQuery : IQuery<IReadOnlyList<StorePolicyResponse>>;
public sealed record GetAdminPoliciesQuery : IQuery<IReadOnlyList<StorePolicyResponse>>;
public sealed record UpsertStorePolicyCommand(
    string PolicyType, string Title, string Content, bool IsPublished) : ICommand<StorePolicyResponse>;
public sealed record DeleteStorePolicyCommand(Guid PolicyId) : ICommand;
public sealed record PublishStorePolicyCommand(Guid PolicyId) : ICommand;
public sealed record UnpublishStorePolicyCommand(Guid PolicyId) : ICommand;

// -------------------------------------------------------------------------

internal static class PolicyMapper
{
    internal static StorePolicyResponse Map(StorePolicy p) =>
        new(p.Id, p.PolicyType.ToString(), p.Title, p.Content, p.IsPublished, p.UpdatedAtUtc);
}

internal sealed class GetPublicPoliciesHandler(IApplicationDbContext db, IMemoryCache cache)
    : IQueryHandler<GetPublicPoliciesQuery, IReadOnlyList<StorePolicyResponse>>
{
    private const string CacheKey = "store:policies:public";

    public async Task<Result<IReadOnlyList<StorePolicyResponse>>> Handle(
        GetPublicPoliciesQuery query, CancellationToken ct)
    {
        if (cache.TryGetValue(CacheKey, out IReadOnlyList<StorePolicyResponse>? cached) && cached is not null)
            return Result.Success(cached);

        var policies = await db.StorePolicies.AsNoTracking()
            .Where(p => p.IsPublished)
            .OrderBy(p => p.PolicyType)
            .ToListAsync(ct);

        IReadOnlyList<StorePolicyResponse> result = policies.Select(PolicyMapper.Map).ToList();
        cache.Set(CacheKey, result, TimeSpan.FromMinutes(30));
        return Result.Success(result);
    }
}

internal sealed class GetAdminPoliciesHandler(IApplicationDbContext db)
    : IQueryHandler<GetAdminPoliciesQuery, IReadOnlyList<StorePolicyResponse>>
{
    public async Task<Result<IReadOnlyList<StorePolicyResponse>>> Handle(
        GetAdminPoliciesQuery query, CancellationToken ct)
    {
        var policies = await db.StorePolicies.AsNoTracking()
            .OrderBy(p => p.PolicyType).ToListAsync(ct);

        return Result.Success<IReadOnlyList<StorePolicyResponse>>(
            policies.Select(PolicyMapper.Map).ToList());
    }
}

internal sealed class UpsertStorePolicyHandler(IApplicationDbContext db, IMemoryCache cache)
    : ICommandHandler<UpsertStorePolicyCommand, StorePolicyResponse>
{
    public async Task<Result<StorePolicyResponse>> Handle(
        UpsertStorePolicyCommand cmd, CancellationToken ct)
    {
        if (!Enum.TryParse<PolicyType>(cmd.PolicyType, out var type))
            return Result.Failure<StorePolicyResponse>(
                Error.Validation("INVALID_POLICY_TYPE", $"Unknown policy type: {cmd.PolicyType}."));

        var policy = await db.StorePolicies
            .FirstOrDefaultAsync(p => p.PolicyType == type, ct);

        if (policy is null)
        {
            policy = StorePolicy.Create(type, cmd.Title, cmd.Content);
            db.StorePolicies.Add(policy);
        }
        else
        {
            policy.Update(cmd.Title, cmd.Content);
        }

        if (cmd.IsPublished) policy.Publish(); else policy.Unpublish();

        await db.SaveChangesAsync(ct);
        cache.Remove("store:policies:public");
        return Result.Success(PolicyMapper.Map(policy));
    }
}

internal sealed class DeleteStorePolicyHandler(IApplicationDbContext db, IMemoryCache cache)
    : ICommandHandler<DeleteStorePolicyCommand>
{
    public async Task<Result> Handle(DeleteStorePolicyCommand cmd, CancellationToken ct)
    {
        var policy = await db.StorePolicies.FindAsync([cmd.PolicyId], ct);
        if (policy is null)
            return Result.Failure(Error.NotFound("POLICY_NOT_FOUND", "Policy not found."));

        db.StorePolicies.Remove(policy);
        await db.SaveChangesAsync(ct);
        cache.Remove("store:policies:public");
        return Result.Success();
    }
}
