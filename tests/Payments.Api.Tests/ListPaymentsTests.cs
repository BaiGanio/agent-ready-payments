using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Payments.Api.Tests;

/// <summary>
/// The capped list the model reads before it refunds. Order, cap and filter are the contract.
/// Red until step 6.
/// </summary>
public sealed class ListPaymentsTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task An_empty_merchant_lists_nothing()
    {
        var list = await ListAsync();

        Assert.Empty(list.Items);
        Assert.False(list.Truncated);
    }

    [Fact]
    public async Task Lists_only_the_keys_merchant()
    {
        var mine = Guid.CreateVersion7();
        var theirs = Guid.CreateVersion7();

        await InsertPaymentAsync(mine, SeededMerchant, "Captured", SteppingClock.Default);
        await InsertPaymentAsync(theirs, OtherMerchant, "Captured", SteppingClock.Default.AddMinutes(1));

        var list = await ListAsync();

        Assert.Equal(mine, Assert.Single(list.Items).Id);
    }

    [Fact]
    public async Task Newest_first_with_the_id_breaking_ties()
    {
        // Postgres compares uuid values byte by byte, so …02 sorts after …01.
        var smaller = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var larger = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var sameInstant = SteppingClock.Default;

        await InsertPaymentAsync(smaller, SeededMerchant, "Captured", sameInstant);
        await InsertPaymentAsync(larger, SeededMerchant, "Captured", sameInstant);

        // Three calls, three identical orders. A tie the database breaks its own way is a flake.
        for (int call = 0; call < 3; call++)
        {
            var list = await ListAsync();

            Assert.Equal([larger, smaller], list.Items.Select(i => i.Id));
        }
    }

    [Fact]
    public async Task At_most_20_by_default()
    {
        await InsertPaymentsAsync(25);

        var list = await ListAsync();

        Assert.Equal(20, list.Items.Count);
        Assert.True(list.Truncated);
    }

    [Fact]
    public async Task Exactly_the_limit_is_not_truncated()
    {
        await InsertPaymentsAsync(20);

        var list = await ListAsync();

        Assert.Equal(20, list.Items.Count);
        // Exactly 20 rows means there is nothing more. A "count >= limit" says there is.
        Assert.False(list.Truncated);
    }

    [Fact]
    public async Task The_limit_is_respected()
    {
        await InsertPaymentsAsync(7);

        var list = await ListAsync("?limit=5");

        Assert.Equal(5, list.Items.Count);
        Assert.True(list.Truncated);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    [InlineData(-1)]
    public async Task A_limit_outside_1_to_20_is_400(int limit)
    {
        var response = await Client.GetAsync($"/payments?limit={limit}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("limit_out_of_range", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task The_status_filter_is_exact()
    {
        var captured = Guid.CreateVersion7();

        await InsertPaymentAsync(captured, SeededMerchant, "Captured", SteppingClock.Default, captured: 100m);
        await InsertPaymentAsync(Guid.CreateVersion7(), SeededMerchant, "Authorised", SteppingClock.Default.AddMinutes(1));
        await InsertPaymentAsync(Guid.CreateVersion7(), SeededMerchant, "PartiallyCaptured", SteppingClock.Default.AddMinutes(2), captured: 40m);

        var list = await ListAsync("?status=Captured");

        Assert.Equal(captured, Assert.Single(list.Items).Id);

        // The enum is spelled one way. "captured" is a typo, not a filter.
        var lowerCase = await Client.GetAsync("/payments?status=captured", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, lowerCase.StatusCode);
        Assert.Equal("invalid_status", await ErrorCodeAsync(lowerCase));
    }

    /// <summary>Gets the list with the default M1 service key and fails the test if it was not 200.</summary>
    private async Task<ListBody> ListAsync(string query = "")
    {
        var response = await Client.GetAsync($"/payments{query}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<ListBody>(TestContext.Current.CancellationToken))!;
    }

    /// <summary>
    /// Inserts one payment row straight into Postgres, so the test owns the id, the status
    /// and the exact created_at. Rows made through the API all land in the same instant.
    /// </summary>
    private Task InsertPaymentAsync(
        Guid id,
        Guid merchant,
        string status,
        DateTimeOffset createdAt,
        decimal authorised = 100m,
        decimal captured = 0m,
        decimal refunded = 0m,
        string currency = "EUR") =>
        ExecuteSqlAsync(
            """
            insert into payment (id, merchant_id, status, currency, authorised_amount,
                                 captured_amount, refunded_amount, created_at, updated_at)
            values (@Id, @Merchant, @Status, @Currency, @Authorised, @Captured, @Refunded, @CreatedAt, @CreatedAt)
            """,
            new
            {
                Id = id,
                Merchant = merchant,
                Status = status,
                Currency = currency,
                Authorised = authorised,
                Captured = captured,
                Refunded = refunded,
                CreatedAt = createdAt
            });

    /// <summary>Inserts <paramref name="count"/> captured payments for the seeded merchant, one minute apart.</summary>
    private async Task InsertPaymentsAsync(int count)
    {
        for (int i = 0; i < count; i++)
            await InsertPaymentAsync(
                Guid.CreateVersion7(),
                SeededMerchant,
                "Captured",
                SteppingClock.Default.AddMinutes(i),
                captured: 100m);
    }

    private sealed record ListBody(List<SummaryBody> Items, bool Truncated);

    private sealed record SummaryBody(
        Guid Id,
        string Status,
        string Currency,
        decimal AuthorisedAmount,
        decimal CapturedAmount,
        decimal RefundedAmount,
        DateTimeOffset CreatedAt);
}
