using FluentValidation;
using KromicCommerce.Application.Abstractions.Data;
using KromicCommerce.Contracts.Common;
using KromicCommerce.Contracts.Leads;
using KromicCommerce.Domain.Leads;
using Microsoft.EntityFrameworkCore;

namespace KromicCommerce.Application.Features.Leads;

/// <summary>
/// Lists captured leads. Admin only — every field here is personal data, and the ingest endpoint
/// that produces these rows is public.
/// </summary>
public sealed record GetLeadsQuery(
    string? Status,
    string? Search,
    bool IncludeArchived,
    int Page,
    int PageSize) : IQuery<PagedResponse<LeadResponse>>;

/// <summary>Outbox event type names for the lead pipeline.</summary>
internal sealed class GetLeadsValidator : AbstractValidator<GetLeadsQuery>
{
    public GetLeadsValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);

        RuleFor(x => x.Status)
            .Must(s => string.IsNullOrWhiteSpace(s) || Enum.TryParse<LeadStatus>(s, true, out _))
            .WithMessage("Not a valid lead status.");

        RuleFor(x => x.Search).MaximumLength(200);
    }
}

internal sealed class GetLeadsHandler(IApplicationDbContext db)
    : IQueryHandler<GetLeadsQuery, PagedResponse<LeadResponse>>
{
    public async Task<Result<PagedResponse<LeadResponse>>> Handle(
        GetLeadsQuery query, CancellationToken ct)
    {
        var status = string.IsNullOrWhiteSpace(query.Status)
            ? (LeadStatus?)null
            : Enum.Parse<LeadStatus>(query.Status, ignoreCase: true);

        var q = db.Leads.AsNoTracking();

        if (status is not null) q = q.Where(l => l.Status == status.Value);

        if (!query.IncludeArchived) q = q.Where(l => !l.IsArchived);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // Trim before use so a stray space cannot silently narrow the match to nothing.
            //
            // ToLower + Contains rather than the provider's ILike: the Application layer must not
            // depend on a specific EF provider, and this translates to the same SQL. It cannot use
            // an index on the column, which is the correct trade for a table holding leads.
            var term = query.Search.Trim().ToLower();
            q = q.Where(l =>
                l.Name.ToLower().Contains(term)
                || l.Email.ToLower().Contains(term)
                || l.Phone.ToLower().Contains(term)
                || l.Business.ToLower().Contains(term));
        }

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(l => l.CreatedAtUtc)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(l => new LeadResponse(
                l.Id, l.Name, l.Phone, l.PhoneRaw, l.Email, l.Business,
                l.Status.ToString(), l.Source, l.IsArchived, l.CreatedAtUtc))
            .ToListAsync(ct);

        return Result.Success(new PagedResponse<LeadResponse>(items, query.Page, query.PageSize, total));
    }
}