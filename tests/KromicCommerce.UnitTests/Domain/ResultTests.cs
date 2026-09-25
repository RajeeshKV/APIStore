namespace KromicCommerce.UnitTests.Domain;

public sealed class ResultTests
{
    [Fact]
    public void Success_result_has_no_error()
    {
        var result = Result.Success();
        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.Error.Should().Be(Error.None);
    }

    [Fact]
    public void Failure_result_carries_error()
    {
        var error = Error.NotFound("TEST_NOT_FOUND", "Not found.");
        var result = Result.Failure(error);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("TEST_NOT_FOUND");
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public void Generic_success_exposes_value()
    {
        var result = Result.Success(42);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void Generic_failure_throws_on_value_access()
    {
        var result = Result.Failure<int>(Error.Failure("ERR", "fail"));
        result.IsFailure.Should().BeTrue();
        var act = () => result.Value;
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Implicit_conversion_creates_success()
    {
        Result<string> result = "hello";
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("hello");
    }
}
