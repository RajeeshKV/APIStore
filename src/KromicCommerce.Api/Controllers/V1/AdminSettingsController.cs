using Asp.Versioning;
using KromicCommerce.Application.Features.Store.GetAdminSettings;
using KromicCommerce.Application.Features.Store.SetStoreOpen;
using KromicCommerce.Application.Features.Store.UpdateAuthSettings;
using KromicCommerce.Application.Features.Store.UpdateBasicInfo;
using KromicCommerce.Application.Features.Store.UpdateDeliverySettings;
using KromicCommerce.Application.Features.Store.UpdateEmailSettings;
using KromicCommerce.Application.Features.Store.UpdateLocale;
using KromicCommerce.Application.Features.Store.UpdateSeoSettings;
using KromicCommerce.Contracts.Store;
using Microsoft.AspNetCore.Authorization;

namespace KromicCommerce.Api.Controllers.V1;

/// <summary>
/// Admin-only business settings management.
/// All endpoints require the AdminOnly policy — server-side enforcement.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/settings")]
[Authorize(Policy = "AdminOnly")]
public sealed class AdminSettingsController(IMediator mediator) : ControllerBase
{
    // -----------------------------------------------------------------------
    // Read
    // -----------------------------------------------------------------------

    /// <summary>Returns full business settings including auth and email configuration.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(AdminBusinessSettingsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetAdminSettingsQuery(), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    // -----------------------------------------------------------------------
    // Mutations
    // -----------------------------------------------------------------------

    /// <summary>Update business name, contact info, and social links.</summary>
    [HttpPut("basic")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateBasicInfo(
        [FromBody] UpdateBasicInfoRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new UpdateBasicInfoCommand(
            request.BusinessName, request.LegalName, request.WebsiteUrl,
            request.SupportEmail, request.SupportPhone, request.Address,
            request.FacebookUrl, request.InstagramUrl, request.TwitterUrl, request.YoutubeUrl),
            cancellationToken);

        return result.IsSuccess ? NoContent() : result.Error.ToActionResult();
    }

    /// <summary>Update country, currency, timezone, and culture.</summary>
    [HttpPut("locale")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateLocale(
        [FromBody] UpdateLocaleRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new UpdateLocaleCommand(request.CountryCode, request.CurrencyCode,
                request.TimeZoneId, request.Culture),
            cancellationToken);

        return result.IsSuccess ? NoContent() : result.Error.ToActionResult();
    }

    /// <summary>Update delivery fees, COD, and estimated delivery days.</summary>
    [HttpPut("delivery")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateDelivery(
        [FromBody] UpdateDeliverySettingsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new UpdateDeliverySettingsCommand(
            request.FlatFeeAmount, request.FreeShippingThreshold,
            request.CodEnabled, request.CodExtraFee,
            request.ProcessingDays, request.MinDeliveryDays, request.MaxDeliveryDays),
            cancellationToken);

        return result.IsSuccess ? NoContent() : result.Error.ToActionResult();
    }

    /// <summary>Update auth methods and OTP configuration.</summary>
    [HttpPut("auth")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateAuth(
        [FromBody] UpdateAuthSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new UpdateAuthSettingsCommand(
            request.GoogleOAuthEnabled, request.EmailPasswordEnabled,
            request.MobileOtpEnabled, request.OtpExpiryMinutes,
            request.OtpResendCooldownSeconds, request.OtpMaxAttempts,
            request.SmsProvider),
            cancellationToken);

        return result.IsSuccess ? NoContent() : result.Error.ToActionResult();
    }

    /// <summary>Update email mode and sender identity.</summary>
    [HttpPut("email")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateEmail(
        [FromBody] UpdateEmailSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new UpdateEmailSettingsCommand(request.Mode, request.SenderName, request.SenderEmail),
            cancellationToken);

        return result.IsSuccess ? NoContent() : result.Error.ToActionResult();
    }

    /// <summary>Update SEO metadata for the store.</summary>
    [HttpPut("seo")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateSeo(
        [FromBody] UpdateSeoSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new UpdateSeoSettingsCommand(
            request.MetaTitle, request.MetaDescription, request.MetaKeywords,
            request.FaviconUrl, request.OgImageUrl),
            cancellationToken);

        return result.IsSuccess ? NoContent() : result.Error.ToActionResult();
    }

    /// <summary>Open or close the store with an optional closure message.</summary>
    [HttpPut("status")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SetStatus(
        [FromBody] SetStoreOpenRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new SetStoreOpenCommand(request.IsOpen, request.ClosureMessage),
            cancellationToken);

        return result.IsSuccess ? NoContent() : result.Error.ToActionResult();
    }
}
