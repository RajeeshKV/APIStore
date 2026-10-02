namespace KromicCommerce.Application.Features.Admin.SmsTemplates;

/// <summary>
/// Lists the gateways an administrator may select, so the admin screen and the backend
/// always agree on the supported set and the fields each requires.
/// </summary>
public sealed record GetSmsProvidersQuery : IQuery<IReadOnlyList<SmsProviderOptionResponse>>;

// -------------------------------------------------------------------------

/// <summary>
/// Lists the gateways an administrator may select, sourced from
/// <see cref="SmsProviderKinds.Selectable"/> rather than a hand-written list.
/// </summary>
internal sealed class GetSmsProvidersHandler : IQueryHandler<GetSmsProvidersQuery, IReadOnlyList<SmsProviderOptionResponse>>
{
    public Task<Result<IReadOnlyList<SmsProviderOptionResponse>>> Handle(
        GetSmsProvidersQuery query, CancellationToken ct)
    {
        // Served from the same table that validates a save, so the rendered form and the enforced
        // rules cannot drift apart. The UI therefore never needs to know, for example, that a
        // 2Factor template value is a template NAME while a Twilio one is a TemplateSid.
        IReadOnlyList<SmsProviderOptionResponse> result = SmsProviderFieldSchema.All
            .Select(s => new SmsProviderOptionResponse(
                s.Name,
                s.Description,
                s.RequiredSettings,
                s.Settings,
                s.Notes))
            .ToList();

        return Task.FromResult(Result.Success(result));
    }
}
