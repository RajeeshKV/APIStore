using KromicCommerce.Application.Abstractions.Catalog;

namespace KromicCommerce.Application.Services;

/// <summary>
/// Calculates an estimated delivery window based on processing and delivery days.
/// Logic: today + processingDays + min/maxDeliveryDays → from/to date strings.
/// Registered as a singleton — it has no state or external dependencies.
/// </summary>
public sealed class DeliveryEstimateService : IDeliveryEstimateService
{
    public DeliveryEstimateDto? Calculate(
        int processingDays,
        int minDeliveryDays,
        int maxDeliveryDays,
        DateOnly? referenceDate = null)
    {
        var today = referenceDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var fromDate = today.AddDays(processingDays + minDeliveryDays);
        var toDate = today.AddDays(processingDays + maxDeliveryDays);

        var totalMin = processingDays + minDeliveryDays;
        var totalMax = processingDays + maxDeliveryDays;

        var description = totalMin == totalMax
            ? $"Delivered in {totalMin} business day{(totalMin == 1 ? "" : "s")}"
            : $"Delivered in {totalMin}–{totalMax} business days";

        return new DeliveryEstimateDto(
            From: fromDate.ToString("yyyy-MM-dd"),
            To: toDate.ToString("yyyy-MM-dd"),
            Description: description);
    }
}
