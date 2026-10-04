using Asp.Versioning;
using KromicCommerce.Api.Extensions;
using KromicCommerce.Application.Features.Leads;
using KromicCommerce.Contracts.Common;
using KromicCommerce.Contracts.Leads;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace KromicCommerce.Api.Controllers.V1;

/// <summary>
/// Public "Get Started" lead capture.
///
/// <para>
/// Unauthenticated by design — this is the form a visitor fills before they have an account.
/// It is the most abused endpoint in the API as a result, so three defences apply: a per-IP rate
/// limit, a honeypot field, and a notification recipient that comes from server configuration
/// rather than the request body.
/// </para>
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/leads")]
public sealed class LeadsController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Captures an enquiry and notifies the sales team.
    ///
    /// <c>201</c> is returned for every accepted submission, including one discarded by the
    /// honeypot. A bot that gets a distinguishable status learns which field gave it away and
    /// simply stops filling that one, so a caught submission looks exactly like a good one.
    /// </summary>
    [HttpPost]
    [EnableRateLimiting(RateLimitingExtensions.LeadPolicy)]
    [ProducesResponseType(typeof(CreateLeadResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Create([FromBody] CreateLeadRequest request, CancellationToken ct)
    {
        if (request is null) return BadRequest();

        var result = await mediator.Send(new CreateLeadCommand(
            request.Name,
            request.Phone,
            request.Email,
            request.Business,
            request.Source,
            request.Website,
            IpOf(),
            Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null), ct);

        if (!result.IsSuccess) return result.Error.ToActionResult();

        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    /// <summary>
    /// Lists captured leads. Admin only.
    ///
    /// Reads the durable record, which exists so a notification email that fails — provider
    /// outage, quota, a spam filter — does not lose the enquiry.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(PagedResponse<LeadResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLeads(
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] bool includeArchived = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(
            new GetLeadsQuery(status, search, includeArchived, page, pageSize), ct);

        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    /// <summary>
    /// Client IP for the lead record.
    ///
    /// Read from the connection rather than <c>X-Forwarded-For</c> on purpose: that header is
    /// caller-controlled unless a proxy is known to overwrite it, so trusting it would let a bot
    /// forge a different IP per submission and walk straight through the per-IP rate limit.
    /// Configure ForwardedHeaders middleware to handle the proxied case properly.
    /// </summary>
    private string? IpOf() => HttpContext.Connection.RemoteIpAddress?.ToString();
}