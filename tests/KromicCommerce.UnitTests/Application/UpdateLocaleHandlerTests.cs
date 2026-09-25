using KromicCommerce.Application.Features.Store.UpdateLocale;
using KromicCommerce.Domain.Store;
using Microsoft.Extensions.Logging.Abstractions;

namespace KromicCommerce.UnitTests.Application;

public sealed class UpdateLocaleHandlerTests
{
    private readonly Mock<IApplicationDbContext> _db = new();
    private readonly Mock<IBusinessSettingsService> _svc = new();

    private UpdateLocaleHandler CreateHandler() =>
        new(_db.Object, _svc.Object, NullLogger<UpdateLocaleHandler>.Instance);

    [Fact]
    public async Task Updates_locale_and_invalidates_cache()
    {
        var settings = BusinessSettings.CreateDefault("X");
        _db.Setup(d => d.BusinessSettings.FindAsync(
                It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(settings);
        _db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateHandler().Handle(
            new UpdateLocaleCommand("US", "USD", "America/New_York", "en-US"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        settings.CountryCode.Should().Be("US");
        settings.CurrencyCode.Should().Be("USD");
        _svc.Verify(s => s.Invalidate(), Times.Once);
    }

    [Theory]
    [InlineData("X", "USD", "UTC", "en-US")]   // country too short/long — rule: 2 chars
    [InlineData("US", "US", "UTC", "en-US")]    // currency not 3 chars
    [InlineData("US", "USD", "UTC", "en")]      // culture not xx-XX format
    public void Validator_rejects_invalid_locale(string cc, string cur, string tz, string culture)
    {
        var validator = new UpdateLocaleValidator();
        var result = validator.Validate(new UpdateLocaleCommand(cc, cur, tz, culture));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_accepts_valid_locale()
    {
        var validator = new UpdateLocaleValidator();
        var result = validator.Validate(new UpdateLocaleCommand("IN", "INR", "Asia/Kolkata", "en-IN"));
        result.IsValid.Should().BeTrue();
    }
}
