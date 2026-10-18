namespace Payments.Domain.Tests;

/// <summary>
/// Business outcomes are values, not exceptions. The caller must look at the result
/// before it can touch the value, so a refused refund can never be ignored by accident.
/// </summary>
public class ResultTests
{
    [Fact]
    public void Ok_carries_the_value()
    {
        var result = Result<string, PaymentError>.Ok("fine");

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal("fine", result.Value);
    }

    [Fact]
    public void Fail_carries_the_error()
    {
        var error = new PaymentError(PaymentErrorCode.IllegalTransition, "no");
        var result = Result<string, PaymentError>.Fail(error);

        Assert.True(result.IsFailure);
        Assert.False(result.IsSuccess);
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public void Reading_the_value_of_a_failure_throws()
    {
        var result = Result<string, PaymentError>.Fail(
            new PaymentError(PaymentErrorCode.IllegalTransition, "no"));

        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void Reading_the_error_of_a_success_throws()
    {
        var result = Result<string, PaymentError>.Ok("fine");

        Assert.Throws<InvalidOperationException>(() => result.Error);
    }
}
