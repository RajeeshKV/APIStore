using KromicCommerce.Application.Features.Store.UpdateBasicInfo;
using KromicCommerce.Domain.Store;
using Microsoft.Extensions.Logging.Abstractions;

namespace KromicCommerce.UnitTests.Application;

public sealed class UpdateBasicInfoHandlerTests
{
    private readonly Mock<IApplicationDbContext> _db = new();
    private readonly Mock<IBusinessSettingsService> _svc = new();

    private UpdateBasicInfoHandler CreateHandler() =>
        new(_db.Object, _svc.Object, NullLogger<UpdateBasicInfoHandler>.Instance);

    private static UpdateBasicInfoCommand ValidCommand() =>
        new("New Store", null, null, null, null, null, null, null, null, null);

    [Fact]
    public async Task Returns_not_found_when_settings_missing()
    {
        _db.Setup(d => d.BusinessSettings.FindAsync(
                It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BusinessSettings?)null);

        var result = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("BUSINESS_SETTINGS_NOT_FOUND");
        _svc.Verify(s => s.Invalidate(), Times.Never);
    }

    [Fact]
    public async Task Updates_and_invalidates_cache_on_success()
    {
        var settings = BusinessSettings.CreateDefault("Old Store");
        _db.Setup(d => d.BusinessSettings.FindAsync(
                It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(settings);
        _db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateHandler().Handle(
            new UpdateBasicInfoCommand("New Store", "Legal Inc", "https://example.com",
                "s@e.com", "+91999", "Addr", null, null, null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        settings.BusinessName.Should().Be("New Store");
        settings.LegalName.Should().Be("Legal Inc");

        // Cache must be invalidated after every mutation
        _svc.Verify(s => s.Invalidate(), Times.Once);
        _db.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Validator_rejects_empty_business_name()
    {
        var validator = new UpdateBasicInfoValidator();
        var cmd = ValidCommand() with { BusinessName = "" };
        var result = validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_rejects_invalid_support_email()
    {
        var validator = new UpdateBasicInfoValidator();
        var cmd = ValidCommand() with { SupportEmail = "not-an-email" };
        var result = validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
    }
}
