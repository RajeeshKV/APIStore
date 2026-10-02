namespace KromicCommerce.Application.Features.Admin.SmsTemplates;

public sealed record GetSmsTemplatesQuery : IQuery<IReadOnlyList<SmsTemplateResponse>>;
public sealed record GetSmsProvidersQuery : IQuery<IReadOnlyList<SmsProviderOptionResponse>>;

public sealed record CreateSmsTemplateCommand(
    string Provider,
    string Name,
    string? Body,
    string? ExternalTemplateId,
    bool IsActive) : ICommand<SmsTemplateResponse>;

public sealed record UpdateSmsTemplateCommand(
    Guid TemplateId,
    string Name,
    string? Body,
    string? ExternalTemplateId,
    bool IsActive) : ICommand<SmsTemplateResponse>;

public sealed record DeleteSmsTemplateCommand(Guid TemplateId) : ICommand;

// -------------------------------------------------------------------------

internal static class SmsTemplateMapper
{
    internal static SmsTemplateResponse Map(SmsTemplate t) => new(
        t.Id,
        t.Provider.ToName(),
        t.Name,
        t.Body,
        t.ExternalTemplateId,
        t.IsActive,
        t.UpdatedAtUtc);
}

/// <summary>
/// Lists the gateways an administrator may select, so the admin screen and the backend agree on
/// the supported set. Sourced from <see cref="SmsProviderKinds.Selectable"/> rather than a
/// hand-written list, which is what stops the two drifting apart when a provider is added or
/// removed.
/// </summary>
internal sealed class GetSmsProvidersHandler : IQueryHandler<GetSmsProvidersQuery, IReadOnlyList<SmsProviderOptionResponse>>
{
    public Task<Result<IReadOnlyList<SmsProviderOptionResponse>>> Handle(
        GetSmsProvidersQuery query, CancellationToken ct)
    {
        // Served from the same table that validates a save, so the rendered form and the enforced
        // rules cannot drift apart. The UI therefore never needs to know, for example, that a
        // 2Factor template value is a template NAME while a Twilio one is a Template SID.
        IReadOnlyList<SmsProviderOptionResponse> result = SmsProviderFieldSchema.All
            .Select(s => new SmsProviderOptionResponse(
                s.Name,
                s.Description,
                s.SupportsNativeOtp,
                s.RequiresTemplate,
                s.RequiredSettings,
                s.Settings,
                s.TemplateFields,
                s.Notes))
            .ToList();

        return Task.FromResult(Result.Success(result));
    }
}

internal sealed class GetSmsTemplatesHandler(IApplicationDbContext db)
    : IQueryHandler<GetSmsTemplatesQuery, IReadOnlyList<SmsTemplateResponse>>
{
    public async Task<Result<IReadOnlyList<SmsTemplateResponse>>> Handle(
        GetSmsTemplatesQuery query, CancellationToken ct)
    {
        var templates = await db.SmsTemplates.AsNoTracking()
            .OrderBy(t => t.Provider).ThenByDescending(t => t.IsActive).ThenBy(t => t.Name)
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<SmsTemplateResponse>>(
            templates.Select(SmsTemplateMapper.Map).ToList());
    }
}

internal sealed class CreateSmsTemplateHandler(IApplicationDbContext db)
    : ICommandHandler<CreateSmsTemplateCommand, SmsTemplateResponse>
{
    public async Task<Result<SmsTemplateResponse>> Handle(CreateSmsTemplateCommand cmd, CancellationToken ct)
    {
        if (SmsProviderKinds.Parse(cmd.Provider) is not { } provider)
        {
            return Result.Failure<SmsTemplateResponse>(Error.Validation(
                "SMS_PROVIDER_UNSUPPORTED",
                $"'{cmd.Provider}' is not a supported SMS provider. " +
                $"Choose one of: {string.Join(", ", SmsProviderKinds.Selectable.Select(p => p.ToName()))}."));
        }

        // Validated against the same descriptors the admin form is rendered from, so the required
        // fields and formats the UI enforces are the ones the server enforces.
        var errors = SmsProviderFieldSchema.ValidateTemplate(provider, cmd.Body, cmd.ExternalTemplateId);
        if (errors.Count > 0)
        {
            return Result.Failure<SmsTemplateResponse>(Error.Validation(
                "SMS_TEMPLATE_INVALID",
                string.Join(" ", errors.Values)));
        }

        if (cmd.IsActive)
        {
            // The database enforces one active template per provider, but relying on the unique
            // index would surface as an unhandled DbUpdateException. Deactivating the incumbent
            // first makes the rule an explicit, expected state transition.
            var incumbents = await db.SmsTemplates
                .Where(t => t.Provider == provider && t.IsActive)
                .ToListAsync(ct);

            foreach (var incumbent in incumbents)
                incumbent.Deactivate();
        }

        var template = SmsTemplate.Create(provider, cmd.Name, cmd.Body, cmd.ExternalTemplateId, cmd.IsActive);
        db.SmsTemplates.Add(template);

        await db.SaveChangesAsync(ct);
        return Result.Success(SmsTemplateMapper.Map(template));
    }
}

internal sealed class UpdateSmsTemplateHandler(IApplicationDbContext db)
    : ICommandHandler<UpdateSmsTemplateCommand, SmsTemplateResponse>
{
    public async Task<Result<SmsTemplateResponse>> Handle(UpdateSmsTemplateCommand cmd, CancellationToken ct)
    {
        var template = await db.SmsTemplates.FindAsync([cmd.TemplateId], ct);
        if (template is null)
            return Result.Failure<SmsTemplateResponse>(Error.NotFound("SMS_TEMPLATE_NOT_FOUND", "SMS template not found."));

        var errors = SmsProviderFieldSchema.ValidateTemplate(
            template.Provider, cmd.Body, cmd.ExternalTemplateId);
        if (errors.Count > 0)
        {
            return Result.Failure<SmsTemplateResponse>(Error.Validation(
                "SMS_TEMPLATE_INVALID",
                string.Join(" ", errors.Values)));
        }

        template.Update(cmd.Name, cmd.Body, cmd.ExternalTemplateId);

        if (cmd.IsActive && !template.IsActive)
        {
            var incumbents = await db.SmsTemplates
                .Where(t => t.Provider == template.Provider && t.IsActive && t.Id != template.Id)
                .ToListAsync(ct);

            foreach (var incumbent in incumbents)
                incumbent.Deactivate();
        }

        if (cmd.IsActive) template.Activate(); else template.Deactivate();

        await db.SaveChangesAsync(ct);
        return Result.Success(SmsTemplateMapper.Map(template));
    }
}

internal sealed class DeleteSmsTemplateHandler(IApplicationDbContext db) : ICommandHandler<DeleteSmsTemplateCommand>
{
    public async Task<Result> Handle(DeleteSmsTemplateCommand cmd, CancellationToken ct)
    {
        var template = await db.SmsTemplates.FindAsync([cmd.TemplateId], ct);
        if (template is null)
            return Result.Failure<SmsTemplateResponse>(Error.NotFound("SMS_TEMPLATE_NOT_FOUND", "SMS template not found."));

        db.SmsTemplates.Remove(template);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
