using System.Net;
using System.Net.Http.Json;
using Payments.Api;
using Xunit;

namespace Payments.Api.Tests;

/// <summary>
/// The race lab 03 could not see. <c>ConcurrencyTests</c> fires 50 refunds that all
/// carry the <b>same</b> Idempotency-Key, so the key table refuses every racer but one
/// and the money is safe whatever the payment code does. Two agents are not that case:
/// each one is an honest, separate request with its own key, so the key table waves both
/// through and only the <c>payment</c> row can say no. Today it does not — both load
/// <c>refunded_amount = 0</c>, both write, and a 100.00 payment is refunded 120.00
/// (29 of 30 rounds, measured 2026-10-22).
///
/// The shape of both tests is the same, and it matters. Build the two requests
/// <b>before</b> <c>Task.WhenAll</c>: JSON serialisation and header work happen one at a
/// time, so a request built inside the loop is already sent before its partner is born,
/// and the test passes while proving nothing. Then run the whole thing 20 times. A race
/// is a coin toss, so one green round is not evidence — the round that loses is the one
/// that costs the money.
/// </summary>
public sealed class RefundRaceTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    /// <summary>How many times each race runs. One round proves nothing.</summary>
    private const int Rounds = 20;

    [Fact]
    public async Task Two_partial_refunds_with_different_keys_refund_once()
    {
        for (int round = 1; round <= Rounds; round++)
        {
            var payment = await CapturedPaymentAsync(amount: 100m);

            // 60 + 60 on a captured 100. One of them must be refused: 120 is more than
            // the payment ever captured.
            var answers = await RaceAsync(payment.Id, 60m);

            try
            {
                var ok = answers.Where(a => a.StatusCode == HttpStatusCode.OK).ToList();
                var refused = answers.Where(a => a.StatusCode == HttpStatusCode.UnprocessableEntity).ToList();

                Assert.Empty(answers.Where(a => (int)a.StatusCode >= 500));

                Assert.True(
                    ok.Count == 1,
                    $"Round {round}: {ok.Count} of 2 refunds got a 200. Exactly one is owed; "
                    + $"two means 120.00 was refunded on a 100.00 payment. The answers were "
                    + $"{string.Join(", ", answers.Select(a => (int)a.StatusCode))}.");

                Assert.Equal("amount_exceeds_captured", await ErrorCodeAsync(Assert.Single(refused)));

                // The two claims the HTTP codes cannot make: the column and the ledger.
                Assert.Equal(60m, await QuerySqlAsync<decimal>(
                    "select refunded_amount from payment where id = @Id", new { payment.Id }));

                Assert.Equal(1, await CountOperationsAsync(payment.Id, "Refund"));
            }
            finally
            {
                foreach (var answer in answers) answer.Dispose();
            }
        }
    }

    [Fact]
    public async Task Two_full_refunds_with_different_keys_refund_once()
    {
        for (int round = 1; round <= Rounds; round++)
        {
            var payment = await CapturedPaymentAsync(amount: 100m);

            // 100 + 100. The winner leaves the payment Refunded, which is a final status,
            // so the loser is refused by the state machine and not by the amount check.
            var answers = await RaceAsync(payment.Id, 100m);

            try
            {
                var ok = answers.Where(a => a.StatusCode == HttpStatusCode.OK).ToList();
                var refused = answers.Where(a => a.StatusCode == HttpStatusCode.Conflict).ToList();

                Assert.Empty(answers.Where(a => (int)a.StatusCode >= 500));

                Assert.True(
                    ok.Count == 1,
                    $"Round {round}: {ok.Count} of 2 refunds got a 200. Exactly one is owed; "
                    + $"two means 200.00 was refunded on a 100.00 payment. The answers were "
                    + $"{string.Join(", ", answers.Select(a => (int)a.StatusCode))}.");

                Assert.Equal("illegal_transition", await ErrorCodeAsync(Assert.Single(refused)));

                Assert.Equal(100m, await QuerySqlAsync<decimal>(
                    "select refunded_amount from payment where id = @Id", new { payment.Id }));

                Assert.Equal(1, await CountOperationsAsync(payment.Id, "Refund"));
            }
            finally
            {
                foreach (var answer in answers) answer.Dispose();
            }
        }
    }

    /// <summary>
    /// Two refunds of the same amount on one payment, each with its <b>own</b>
    /// Idempotency-Key, built first and then fired together. Separate keys is the whole
    /// point: that is what two agents look like to the API.
    /// </summary>
    private async Task<HttpResponseMessage[]> RaceAsync(Guid paymentId, decimal amount)
    {
        string path = $"/payments/{paymentId}/refund";
        var body = new AmountRequest(amount, "EUR");

        // Built before the send. An HttpRequestMessage cannot be sent twice, so each
        // racer needs its own.
        var requests = new[] { Build(path, body), Build(path, body) };

        return await Task.WhenAll(requests.Select(r => Client.SendAsync(r, TestContext.Current.CancellationToken)));
    }

    private static HttpRequestMessage Build(string path, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, body.GetType()),
        };

        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        return request;
    }
}
