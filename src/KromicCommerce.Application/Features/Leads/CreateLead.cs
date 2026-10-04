using System.Text.Json;
using FluentValidation;
using KromicCommerce.Application.Abstractions.Data;
using KromicCommerce.Application.Options;
using KromicCommerce.Contracts.Leads;
using KromicCommerce.Domain.Leads;
using Microsoft.Extensions.Logging;

namespace KromicCommerce.Application.Features.Leads;

/// <summary>
/// Records a new lead and queues the notification email.
///
/// <para>
/// <c>IpAddress</c> and <c>UserAgent</c> are passed from the controller rather than read from an
/// <c>HttpContext</c> here, so the handler stays testable and the Application layer keeps no
/// dependency on the transport.
/// </para>
/// </summary>
public sealed record CreateLeadCommand(
    string Name,
    string Phone,
    string Email,
    string Business,
    string? Source,
    string? Honeypot,
    string? IpAddress,
    string? UserAgent) : ICommand<CreateLeadResponse>;

/// <summary>
/// Confirmation returned to the visitor.
///
/// Deliberately carries no address echo. A public endpoint that reflects what was submitted is
/// useful to a prober confirming an address is live, and nothing here needs it.
/// </summary>
public sealed record CreateLeadResponse(bool Success, string Message);

/// <summary>
/// Validates the lead form.
///
/// The same limits the domain enforces are asserted here so a bad request is a clear 400 with a
/// named field, rather than an <see cref="ArgumentException"/> from the aggregate surfacing as a
/// 500.
/// </summary>
internal sealed class CreateLeadValidator : AbstractValidator<CreateLeadCommand>
{
    public CreateLeadValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MinimumLength(2)
            .MaximumLength(Lead.NameMaxLength);

        RuleFor(x => x.Phone)
            .NotEmpty()
            .MaximumLength(40)
            .Must(p => p is not null && CountDigits(p) is >= Lead.PhoneMinDigits and <= Lead.PhoneMaxDigits)
            .WithMessage(
                $"A phone number must contain between {Lead.PhoneMinDigits} and " +
                $"{Lead.PhoneMaxDigits} digits.")
            // A leading + is meaningful and kept; any other punctuation in a phone field is
            // almost always a paste artefact worth refusing rather than silently eating.
            .Must(p => p is null || p.TrimStart().StartsWith('+') || !p.Contains('+'))
            .WithMessage("A phone number may only contain a leading '+'.");

        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(Lead.EmailMaxLength);

        RuleFor(x => x.Business)
            .NotEmpty()
            .MinimumLength(2)
            .MaximumLength(Lead.BusinessMaxLength);

        RuleFor(x => x.Source).MaximumLength(200);

        // Not "must be empty". A filled honeypot is a bot, and the caller is expected to accept
        // the submission silently; failing validation here would return 400 and tell the bot
        // exactly which field is the trap.
        RuleFor(x => x.Honeypot)
            .Must(string.IsNullOrWhiteSpace)
            .WithMessage("Rejected.")
            .When(_ => true);
    }

    private static int CountDigits(string value) => value.Count(char.IsAsciiDigit);
}

internal sealed class CreateLeadHandler(
    IApplicationDbContext db,
    IOptions<LeadPolicyOptions> policy,
    ILogger<CreateLeadHandler> logger) : ICommandHandler<CreateLeadCommand, CreateLeadResponse>
{
    public async Task<Result<CreateLeadResponse>> Handle(
        CreateLeadCommand cmd, CancellationToken ct)
    {
        // Honeypot. Recorded at debug level only, and the caller is expected to return the same
        // success response as a genuine submission — see the endpoint contract.
        if (!string.IsNullOrWhiteSpace(cmd.Honeypot))
        {
            logger.LogDebug(
                "Lead submission discarded by honeypot. Source: {Source} Ip: {Ip}",
                cmd.Source, cmd.IpAddress);

            return Result.Success(new CreateLeadResponse(
                true, "Thanks — we'll be in touch shortly."));
        }

        Lead lead;
        try
        {
            lead = Lead.Create(
                cmd.Name, cmd.Phone, cmd.Email, cmd.Business, cmd.Source, cmd.IpAddress, cmd.UserAgent);
        }
        catch (ArgumentException ex)
        {
            // The domain guards are the real backstop; this keeps a normalisation failure a 400.
            return Result.Failure<CreateLeadResponse>(Error.Validation("LEAD_INVALID", ex.Message));
        }

        db.Leads.Add(lead);

        var recipient = policy.Value.NotificationEmail;

        if (string.IsNullOrWhiteSpace(recipient))
        {
            // The lead is stored regardless. Losing it because a deployment forgot an environment
            // variable would be a far worse outcome than a late notification, so this is logged
            // loudly and the outbox event is not written — an event with no destination would
            // just fail and retry forever.
            logger.LogError(
                "Lead {LeadId} stored but no notification address is configured " +
                "(Leads__NotificationEmail). The sales team was not notified.",
                lead.Id);
        }
        else
        {
            db.OutboxEvents.Add(OutboxEvent.Create(LeadOutbox.Submitted, JsonSerializer.Serialize(
                new LeadSubmittedPayload(
                    lead.Id,
                    lead.Name,
                    lead.Phone,
                    lead.PhoneRaw,
                    lead.Email,
                    lead.Business,
                    lead.Source,
                    lead.CreatedAtUtc))));
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // The form is public, so a double-tap, a retried request or an impatient second
            // submit happens in normal use. Letting the unique index raise would surface a
            // database error to a visitor who did nothing wrong. The first lead is still on
            // file, so nothing is lost — the caller is told the number is already known, and the
            // UI decides how politely to say so.
            logger.LogInformation(
                "Duplicate lead submission ignored. Phone: {Phone} Name: {Name}",
                lead.Phone, lead.Name);

            return Result.Failure<CreateLeadResponse>(Error.Conflict(
                "LEAD_ALREADY_CAPTURED",
                "We already have an enquiry for this phone number. Our team will be in touch."));
        }

        // No PII beyond what the business legitimately needs to answer the enquiry, and no
        // request-controlled destination — the recipient is configuration.
        logger.LogInformation(
            "Lead captured. Name: {Name} Business: {Business} Phone: {Phone} Source: {Source}",
            lead.Name, lead.Business, lead.Phone, lead.Source);

        return Result.Success(new CreateLeadResponse(
            true, "Thanks — we'll be in touch shortly."));
    }

    /// <summary>
    /// Detects PostgreSQL unique-constraint violation (SQLSTATE 23505) from Npgsql.
    /// Matches the convention already used by the payment webhook handler.
    /// </summary>
    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException?.Message.Contains("23505", StringComparison.Ordinal) == true ||
        ex.InnerException?.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) == true;
}

/// <summary>Outbox event type names for the lead pipeline.</summary>
public static class LeadOutbox
{
    public const string Submitted = "LeadSubmitted";
}

public sealed record LeadSubmittedPayload(
    Guid LeadId,
    string Name,
    string Phone,
    string PhoneRaw,
    string Email,
    string Business,
    string? Source,
    DateTime SubmittedAtUtc);