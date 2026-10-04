namespace KromicCommerce.Application.Features.Support;

/// <summary>
/// Admin edits a queued invoice before the renderer reaches it.
///
/// The window is the <see cref="TicketInvoiceStatus.Pending"/> state, and it is genuinely
/// short: the worker polls on a short interval and the first thing it does is increment
/// GenerationAttempts. This is the "before generation" half of the override requirement;
/// the "during generation" half is <see cref="RetryInvoiceCommand"/>, which produces a new
/// revision from the corrected content.
///
/// Money fields are not editable here at all. An administrator who needs a corrected amount
/// fixes the order, not the document — otherwise the invoice would disagree with the
/// payment record, which is a far worse problem than an awkward line item.
/// </summary>
internal sealed class OverrideInvoiceContentHandler(
    IApplicationDbContext db,
    ILogger<OverrideInvoiceContentHandler> logger) : ICommandHandler<OverrideInvoiceContentCommand, TicketInvoiceResponse>
{
    public async Task<Result<TicketInvoiceResponse>> Handle(OverrideInvoiceContentCommand cmd, CancellationToken ct)
    {
        var invoice = await db.TicketInvoices.FirstOrDefaultAsync(i => i.Id == cmd.InvoiceId, ct);
        if (invoice is null)
            return Result.Failure<TicketInvoiceResponse>(
                Error.NotFound("INVOICE_NOT_FOUND", "Invoice not found."));

        if (!invoice.CanEditContent)
            return Result.Failure<TicketInvoiceResponse>(Error.Conflict(
                "INVOICE_CONTENT_LOCKED",
                $"Invoice {invoice.InvoiceNumber} is {invoice.Status}; its content can no longer be edited. " +
                "Queue a new revision instead."));

        try
        {
            invoice.OverrideContent(invoice.Content.WithOverride(
                issuerName: cmd.IssuerName,
                billToName: cmd.BillToName,
                notes: cmd.Notes,
                terms: cmd.Terms,
                footerNote: cmd.FooterNote,
                accentColor: cmd.AccentColor), cmd.AdminId);
        }
        catch (ArgumentException ex)
        {
            // InvoiceContent re-validates what the validator already checked. Catching here
            // keeps the entity as the single source of truth for the rules.
            return Result.Failure<TicketInvoiceResponse>(
                Error.Validation("INVOICE_OVERRIDE_INVALID", ex.Message));
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Invoice {InvoiceNumber} content overridden by {AdminId}", invoice.InvoiceNumber, cmd.AdminId);

        return Result.Success(TicketMapper.MapInvoice(invoice));
    }
}

/// <summary>Admin puts a failed invoice revision back in the render queue.</summary>
internal sealed class RetryInvoiceCommandHandler(
    IApplicationDbContext db,
    ILogger<RetryInvoiceCommandHandler> logger) : ICommandHandler<RetryInvoiceCommand, TicketInvoiceResponse>
{
    public async Task<Result<TicketInvoiceResponse>> Handle(RetryInvoiceCommand cmd, CancellationToken ct)
    {
        var invoice = await db.TicketInvoices.FirstOrDefaultAsync(i => i.Id == cmd.InvoiceId, ct);
        if (invoice is null)
            return Result.Failure<TicketInvoiceResponse>(
                Error.NotFound("INVOICE_NOT_FOUND", "Invoice not found."));

        if (invoice.Status != TicketInvoiceStatus.Failed)
            return Result.Failure<TicketInvoiceResponse>(Error.Conflict(
                "INVOICE_NOT_FAILED",
                $"Invoice {invoice.InvoiceNumber} is {invoice.Status}; only a failed revision can be retried."));

        // The attempt budget is finite and a human retry does not reset it. Otherwise a
        // broken renderer could be retried forever by an admin clicking the button.
        if (invoice.GenerationAttempts >= TicketInvoice.MaxGenerationAttempts)
            return Result.Failure<TicketInvoiceResponse>(Error.Conflict(
                "INVOICE_RETRY_BUDGET_EXHAUSTED",
                $"Invoice {invoice.InvoiceNumber} has used all " +
                $"{TicketInvoice.MaxGenerationAttempts} rendering attempts. Queue a new revision instead."));

        invoice.Requeue();
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Invoice {InvoiceNumber} requeued by {AdminId} (attempt {Attempts} of {Max})",
            invoice.InvoiceNumber, cmd.AdminId, invoice.GenerationAttempts, TicketInvoice.MaxGenerationAttempts);

        return Result.Success(TicketMapper.MapInvoice(invoice));
    }
}

/// <summary>Admin edits the standardised invoice template.</summary>
internal sealed class UpdateInvoiceTemplateHandler(
    IApplicationDbContext db,
    ILogger<UpdateInvoiceTemplateHandler> logger) : ICommandHandler<UpdateInvoiceTemplateCommand, InvoiceTemplateResponse>
{
    public async Task<Result<InvoiceTemplateResponse>> Handle(UpdateInvoiceTemplateCommand cmd, CancellationToken ct)
    {
        var template = await db.InvoiceTemplates.FirstOrDefaultAsync(t => t.Id == cmd.TemplateId, ct);
        if (template is null)
            return Result.Failure<InvoiceTemplateResponse>(
                Error.NotFound("INVOICE_TEMPLATE_NOT_FOUND", "Invoice template not found."));

        try
        {
            template.Update(
                cmd.Name, cmd.Description, cmd.IssuerNameOverride, cmd.TaxId,
                cmd.Notes, cmd.Terms, cmd.FooterNote, cmd.AccentColor,
                cmd.ShowIssuerIdentity, cmd.ShowLineItemTable, cmd.ShowTerms, cmd.ShowNotes);
        }
        catch (ArgumentException ex)
        {
            return Result.Failure<InvoiceTemplateResponse>(
                Error.Validation("INVOICE_TEMPLATE_INVALID", ex.Message));
        }

        await db.SaveChangesAsync(ct);

        // Editing the template does not touch invoices that already reference it. Each
        // revision froze its own copy of the wording at queue time, so a document already
        // produced cannot change after the fact.
        logger.LogInformation(
            "Invoice template {TemplateId} updated by {AdminId}", template.Id, cmd.AdminId);

        return Result.Success(TicketMapper.MapTemplate(template));
    }
}

/// <summary>Admin flips the support automation switches, including the global invoice mailing toggle.</summary>
internal sealed class UpdateSupportSettingsHandler(
    IApplicationDbContext db,
    SupportSettingsProvider settingsProvider,
    IOptions<SupportPolicyOptions> policyOptions,
    ILogger<UpdateSupportSettingsHandler> logger) : ICommandHandler<UpdateSupportSettingsCommand, SupportSettingsResponse>
{
    public async Task<Result<SupportSettingsResponse>> Handle(UpdateSupportSettingsCommand cmd, CancellationToken ct)
    {
        var settings = await settingsProvider.GetOrCreateAsync(ct);

        settings.UpdateAutomation(
            autoGenerateInvoiceOnResolve: cmd.AutoGenerateInvoiceOnResolve,
            automatedInvoiceMailingEnabled: cmd.AutomatedInvoiceMailingEnabled,
            invoiceMailSubjectOverride: cmd.InvoiceMailSubjectOverride,
            autoCloseIdleHours: cmd.AutoCloseIdleHours);

        settings.UpdateNotifications(
            notifyAdminOnTicketCreated: cmd.NotifyAdminOnTicketCreated,
            notifyAdminOnTicketReopened: cmd.NotifyAdminOnTicketReopened,
            notifyCustomerOnTicketResolved: cmd.NotifyCustomerOnTicketResolved);

        if (cmd.MaxAttachmentsPerComment.HasValue)
            settings.SetMaxAttachmentsPerComment(cmd.MaxAttachmentsPerComment.Value);

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Support settings updated by {AdminId}. InvoiceMailing={InvoiceMailing} AutoInvoice={AutoInvoice} IdleHours={IdleHours}",
            cmd.AdminId,
            settings.AutomatedInvoiceMailingEnabled,
            settings.AutoGenerateInvoiceOnResolve,
            settings.AutoCloseIdleHours);

        return Result.Success(SupportSettingsProjection.Map(settings, policyOptions.Value));
    }
}

/// <summary>Maps the settings singleton to its contract, including the masked admin target.</summary>
internal static class SupportSettingsProjection
{
    /// <summary>
    /// The recipient address is deployment configuration, not merchant data, so it is echoed
    /// back masked. An admin UI needs to know *where* notifications go — a masked value is
    /// enough to verify that — and returning it in full would turn an admin read endpoint into
    /// an address disclosure surface.
    /// </summary>
    public static SupportSettingsResponse Map(SupportSettings settings, SupportPolicyOptions policy) =>
        new(
            settings.AutoCloseIdleHours,
            settings.AutomatedInvoiceMailingEnabled,
            settings.AutoGenerateInvoiceOnResolve,
            settings.InvoiceMailSubjectOverride,
            settings.NotifyAdminOnTicketCreated,
            settings.NotifyAdminOnTicketReopened,
            settings.NotifyCustomerOnTicketResolved,
            settings.MaxAttachmentsPerComment,
            policy.IsAdminNotificationConfigured,
            Mask(policy.AdminNotificationEmail));

    internal static string Mask(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@')) return "not configured";

        var parts = email.Split('@', 2);
        var local = parts[0];
        var visible = local.Length <= 2 ? local : local[..2];

        return $"{visible}{new string('*', Math.Max(1, local.Length - visible.Length))}@{parts[1]}";
    }
}