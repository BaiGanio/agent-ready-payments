namespace Payments.Domain.Tests;

/// <summary>
/// Money is a value object: an amount plus an ISO 4217 currency code.
/// Illegal money must be impossible to build, so the checks live in the factory.
/// </summary>
public class MoneyTests
{
    [Fact]
    public void Of_keeps_the_amount_and_the_currency()
    {
        var money = Money.Of(10.50m, "EUR");

        Assert.Equal(10.50m, money.Amount);
        Assert.Equal("EUR", money.Currency);
    }

    [Fact]
    public void A_negative_amount_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Money.Of(-0.01m, "EUR"));
    }

    [Fact]
    public void Zero_is_allowed_as_a_total()
    {
        var nothing = Money.Zero("EUR");

        Assert.Equal(0m, nothing.Amount);
        Assert.Equal("EUR", nothing.Currency);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("EU")]
    [InlineData("EURO")]
    [InlineData("eur")]
    [InlineData("12A")]
    public void A_currency_that_is_not_three_upper_case_letters_is_rejected(string currency)
    {
        Assert.Throws<ArgumentException>(() => Money.Of(10m, currency));
    }

    [Fact]
    public void Two_amounts_in_the_same_currency_add_up()
    {
        var sum = Money.Of(10.10m, "EUR") + Money.Of(0.20m, "EUR");

        Assert.Equal(Money.Of(10.30m, "EUR"), sum);
    }

    [Fact]
    public void Cents_add_up_exactly()
    {
        var sum = Money.Zero("EUR");

        for (var i = 0; i < 10; i++)
        {
            sum += Money.Of(0.10m, "EUR");
        }

        Assert.Equal(Money.Of(1.00m, "EUR"), sum);
    }

    [Fact]
    public void Two_amounts_in_the_same_currency_subtract()
    {
        var left = Money.Of(100m, "EUR") - Money.Of(40m, "EUR");

        Assert.Equal(Money.Of(60m, "EUR"), left);
    }

    [Fact]
    public void Adding_two_currencies_throws()
    {
        Assert.Throws<CurrencyMismatchException>(() => Money.Of(10m, "EUR") + Money.Of(10m, "USD"));
    }

    [Fact]
    public void Subtracting_two_currencies_throws()
    {
        Assert.Throws<CurrencyMismatchException>(() => Money.Of(10m, "EUR") - Money.Of(10m, "USD"));
    }

    [Fact]
    public void Comparing_two_currencies_throws()
    {
        Assert.Throws<CurrencyMismatchException>(() => Money.Of(10m, "EUR") > Money.Of(10m, "USD"));
    }

    [Fact]
    public void Equal_amount_and_currency_are_equal()
    {
        Assert.Equal(Money.Of(10m, "EUR"), Money.Of(10m, "EUR"));
    }

    [Fact]
    public void The_same_amount_in_another_currency_is_not_equal()
    {
        Assert.NotEqual(Money.Of(10m, "EUR"), Money.Of(10m, "USD"));
    }

    [Fact]
    public void Bigger_and_smaller_amounts_compare()
    {
        var small = Money.Of(10m, "EUR");
        var big = Money.Of(20m, "EUR");

        Assert.True(big > small);
        Assert.True(small < big);
        Assert.True(big >= Money.Of(20m, "EUR"));
        Assert.True(small <= Money.Of(10m, "EUR"));
    }

    [Fact]
    public void IsZero_says_whether_anything_is_left()
    {
        Assert.True(Money.Zero("EUR").IsZero);
        Assert.False(Money.Of(0.01m, "EUR").IsZero);
    }
}
