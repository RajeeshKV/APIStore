namespace KromicCommerce.UnitTests.Domain;

/// <summary>
/// Refund receipt recording on <see cref="Payment"/>.
///
/// The recorded refund is the idempotency checkpoint for order cancellation: once a payment
/// carries a refund receipt, a retried cancellation reads it and skips the provider call. That
/// only works if the receipt is written as part of accepting the refund, so these tests pin
/// exactly when <see cref="IsRefunded"/> flips.
/// </summary>
public sealed class PaymentRefundTests
{
    private static Payment CreateCapturedPayment(decimal amount = 1000m)
    {
        var payment = Payment.Create(Guid.NewGuid(), "Razorpay", amount, "INR");
        payment.MarkPaid("pay_abc123");
        return payment;
    }

    // -----------------------------------------------------------------------
    // MarkRefunded
    // -----------------------------------------------------------------------

    [Fact]
    public void MarkRefunded_records_the_provider_refund_id()
    {
        var payment = CreateCapturedPayment();

        payment.MarkRefunded("rfnd_xyz789");

        payment.ProviderRefundId.Should().Be("rfnd_xyz789");
        payment.Status.Should().Be(PaymentStatus.Refunded);
    }

    /// <summary>
    /// The amount defaults to the full captured amount. This system exposes no partial refunds
    /// to customers or admins, so a refund record must never imply a smaller amount came back
    /// than was actually charged.
    /// </summary>
    [Fact]
    public void MarkRefunded_defaults_to_the_full_captured_amount()
    {
        var payment = CreateCapturedPayment(amount: 1234.56m);

        payment.MarkRefunded("rfnd_xyz789");

        payment.RefundedAmount.Should().Be(1234.56m);
    }

    [Fact]
    public void MarkRefunded_records_when_the_provider_accepted_it()
    {
        var payment = CreateCapturedPayment();
        var before = DateTime.UtcNow;

        payment.MarkRefunded("rfnd_xyz789");

        payment.RefundedAtUtc.Should().NotBeNull();
        payment.RefundedAtUtc!.Value.Should().BeOnOrAfter(before);
    }

    /// <summary>
    /// A blank provider id is stored as null rather than as an empty string. A provider that
    /// returns no id must not leave a value that looks like a usable reference.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MarkRefunded_normalises_a_blank_provider_refund_id_to_null(string? providerRefundId)
    {
        var payment = CreateCapturedPayment();

        payment.MarkRefunded(providerRefundId);

        payment.ProviderRefundId.Should().BeNull();
    }

    [Fact]
    public void MarkRefunded_trims_the_provider_refund_id()
    {
        var payment = CreateCapturedPayment();

        payment.MarkRefunded("  rfnd_xyz789  ");

        payment.ProviderRefundId.Should().Be("rfnd_xyz789");
    }

    // -----------------------------------------------------------------------
    // IsRefunded — the idempotency guard
    // -----------------------------------------------------------------------

    [Fact]
    public void A_newly_captured_payment_is_not_refunded()
    {
        CreateCapturedPayment().IsRefunded.Should().BeFalse();
    }

    [Fact]
    public void A_refunded_payment_reports_IsRefunded()
    {
        var payment = CreateCapturedPayment();

        payment.MarkRefunded("rfnd_xyz789");

        payment.IsRefunded.Should().BeTrue();
    }

    /// <summary>
    /// IsRefunded is checked by the cancellation flow to decide whether to call the provider.
    /// A failed or pending payment must not short-circuit into "already refunded", or a
    /// cancellation would skip the refund entirely and never issue one.
    /// </summary>
    [Theory]
    [InlineData(PaymentStatus.Pending)]
    [InlineData(PaymentStatus.Authorized)]
    [InlineData(PaymentStatus.Failed)]
    [InlineData(PaymentStatus.RefundPending)]
    public void A_payment_that_was_never_refunded_does_not_report_IsRefunded(PaymentStatus status)
    {
        var payment = CreateCapturedPayment();
        if (status == PaymentStatus.Failed) payment.MarkFailed("card declined");
        else if (status == PaymentStatus.RefundPending) payment.MarkRefundPending();
        else if (status == PaymentStatus.Pending) payment = Payment.Create(
            Guid.NewGuid(), "Razorpay", 1000m, "INR");
        else payment.MarkAuthorized("pay_abc123");

        payment.IsRefunded.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // No secrets in the record
    // -----------------------------------------------------------------------

    /// <summary>
    /// Only provider references may be stored. Card data never belongs on this entity; the
    /// audit is on the shape of the type so a future field cannot quietly reintroduce it.
    /// </summary>
    [Fact]
    public void Payment_stores_no_card_data()
    {
        var forbidden = typeof(Payment)
            .GetProperties()
            .Select(p => p.Name)
            .Where(n => n.Contains("Card", StringComparison.OrdinalIgnoreCase)
                     || n.Contains("Cvv", StringComparison.OrdinalIgnoreCase)
                     || n.Contains("Secret", StringComparison.OrdinalIgnoreCase))
            .ToList();

        forbidden.Should().BeEmpty();
    }
}
