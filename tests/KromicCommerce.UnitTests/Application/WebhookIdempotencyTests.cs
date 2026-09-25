using KromicCommerce.Domain.Webhooks;

namespace KromicCommerce.UnitTests.Application;

public sealed class WebhookIdempotencyTests
{
    [Fact]
    public void WebhookEvent_starts_unprocessed()
    {
        var we = WebhookEvent.Create("Razorpay", "evt_001", "payment.captured", "{}");
        we.IsProcessed.Should().BeFalse();
    }

    [Fact]
    public void MarkProcessed_sets_ProcessedAt()
    {
        var we = WebhookEvent.Create("Razorpay", "evt_001", "payment.captured", "{}");
        we.MarkProcessed();
        we.IsProcessed.Should().BeTrue();
        we.ProcessedAt.Should().NotBeNull();
    }

    [Fact]
    public void WebhookEvent_Create_throws_for_empty_providerEventId()
    {
        var act = () => WebhookEvent.Create("Razorpay", "", "payment.captured", "{}");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void WebhookEvent_Create_throws_for_empty_provider()
    {
        var act = () => WebhookEvent.Create("", "evt_001", "payment.captured", "{}");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void OutboxEvent_RecordFailure_increments_retry_count()
    {
        var evt = OutboxEvent.Create("OrderCreated", "{}");
        evt.RecordFailure("Brevo timeout");
        evt.RecordFailure("Brevo timeout again");
        evt.RetryCount.Should().Be(2);
        evt.Error.Should().Contain("Brevo");
    }

    [Fact]
    public void OutboxEvent_HasFailed_true_when_retries_exhausted()
    {
        var evt = OutboxEvent.Create("OrderCreated", "{}");
        for (var i = 0; i < 5; i++) evt.RecordFailure("error");
        evt.HasFailed(5).Should().BeTrue();
        evt.HasFailed(6).Should().BeFalse();
    }

    [Fact]
    public void OutboxEvent_MarkProcessed_sets_ProcessedAt()
    {
        var evt = OutboxEvent.Create("OrderCreated", "{}");
        evt.MarkProcessed();
        evt.IsProcessed.Should().BeTrue();
        evt.HasFailed(5).Should().BeFalse(); // processed, not failed
    }
}
