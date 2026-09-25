using KromicCommerce.Application.Features.Store.SetStoreOpen;
using KromicCommerce.Domain.Store;
using Microsoft.Extensions.Logging.Abstractions;

namespace KromicCommerce.UnitTests.Application;

public sealed class SetStoreOpenHandlerTests
{
    private readonly Mock<IApplicationDbContext> _db = new();
    private readonly Mock<IBusinessSettingsService> _svc = new();

    private SetStoreOpenHandler CreateHandler() =>
        new(_db.Object, _svc.Object, NullLogger<SetStoreOpenHandler>.Instance);

    [Theory]
    [InlineData(true, null)]
    [InlineData(false, "Closed for maintenance")]
    public async Task Sets_store_status_and_invalidates_cache(bool isOpen, string? message)
    {
        var settings = BusinessSettings.CreateDefault("X");
        _db.Setup(d => d.BusinessSettings.FindAsync(
                It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(settings);
        _db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateHandler().Handle(
            new SetStoreOpenCommand(isOpen, message),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        settings.IsStoreOpen.Should().Be(isOpen);

        if (isOpen)
            settings.TemporaryClosureMessage.Should().BeNull();
        else
            settings.TemporaryClosureMessage.Should().Be(message);

        _svc.Verify(s => s.Invalidate(), Times.Once);
    }

    [Fact]
    public async Task Returns_not_found_when_settings_missing()
    {
        _db.Setup(d => d.BusinessSettings.FindAsync(
                It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BusinessSettings?)null);

        var result = await CreateHandler().Handle(
            new SetStoreOpenCommand(false, null), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("BUSINESS_SETTINGS_NOT_FOUND");
    }

    [Fact]
    public void Validator_rejects_closure_message_over_500_chars()
    {
        var validator = new SetStoreOpenValidator();
        var result = validator.Validate(
            new SetStoreOpenCommand(false, new string('x', 501)));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_accepts_open_with_no_message()
    {
        var validator = new SetStoreOpenValidator();
        var result = validator.Validate(new SetStoreOpenCommand(true, null));
        result.IsValid.Should().BeTrue();
    }
}
