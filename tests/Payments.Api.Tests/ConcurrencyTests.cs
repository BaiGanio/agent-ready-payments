using System.Net;
using System.Net.Http.Json;
using Dapper;
using Payments.Api;
using Xunit;

namespace Payments.Api.Tests;

/// <summary>
/// The race. Every other test in this lab is sequential, so a claim written as
/// select-then-insert passes all of them: the select simply never sees a row that a
/// parallel request has not committed yet. These three tests are the only ones that
/// can tell the difference, which is why <c>CF-035</c> called the lab unproven
/// without them.
///
/// The shape of all three is the same. Build all 50 requests <b>first</b>, then fire
/// them with one <c>Task.WhenAll</c>. Building them inside the loop is the classic
/// mistake: JSON serialisation and header work happen one at a time, the first request
/// is already finished before the fiftieth is born, and the test proves nothing while
/// looking like it does.
///
/// To see these go red, swap the claim in <c>DapperIdempotencyStore.ClaimAsync</c> for
/// a <c>select</c> that checks for the key and an <c>insert</c> that follows it. The
/// money is still safe — the primary key on <c>(merchant_id, key)</c> refuses the
/// second insert whatever the code above it does — so the row counts below stay right.
/// What breaks is the <b>answer</b>: the loser's insert throws instead of returning
/// nothing, the exception escapes, and the caller gets a 500 where a 409 was owed.
/// <see cref="No_loser_in_the_storm_ever_gets_a_5xx"/> is the test that says so.
/// </summary>
public sealed class ConcurrencyTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    private const int Racers = 50;

    [Fact]
    public async Task Fifty_parallel_creates_with_one_key_write_one_payment()
    {
        string key = Guid.NewGuid().ToString();
        var body = new CreatePaymentRequest(SeededMerchant, 100m, "EUR");

        // Built before any of them is sent. See the class summary.
        var requests = BuildRequests("/payments", body, key);

        var answers = await Task.WhenAll(requests.Select(r => Client.SendAsync(r, TestContext.Current.CancellationToken)));

        try
        {
            // One row. This is the money claim, and the primary key alone is enough for it.
            Assert.Equal(1, await CountRowsAsync("payment"));

            // Exactly one key row too: 50 requests claimed one key between them.
            Assert.Equal(1, await CountRowsAsync("idempotency_key"));

            var created = answers.Where(a => a.StatusCode == HttpStatusCode.Created).ToList();
            var conflicts = answers.Where(a => a.StatusCode == HttpStatusCode.Conflict).ToList();

            // At least one winner. More than one 201 is possible and correct: the winner
            // answers 201, and a racer that arrives after the key is Completed replays it.
            Assert.NotEmpty(created);

            // Every answer is one of the two. No 500, no 422, nothing else.
            Assert.Equal(Racers, created.Count + conflicts.Count);

            // Every 201 names the same payment. A second id would mean a second row.
            var ids = new HashSet<Guid>();
            foreach (var answer in created)
            {
                var dto = await answer.Content.ReadFromJsonAsync<PaymentDto>(TestContext.Current.CancellationToken);
                ids.Add(dto!.Id);
            }
            Assert.Single(ids);

            // Every loser is told to come back, and told when.
            foreach (var answer in conflicts)
            {
                Assert.Equal("idempotency_request_in_progress", await ErrorCodeAsync(answer));
                Assert.Equal("1", Assert.Single(answer.Headers.GetValues("Retry-After")));
            }

            // After the storm the key is settled, so a retry replays the 201 instead of
            // conflicting. This is the part a caller actually depends on.
            using var retry = Build("/payments", body, key);
            var replay = await Client.SendAsync(retry, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
            Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotent-Replayed")));

            var replayed = await replay.Content.ReadFromJsonAsync<PaymentDto>(TestContext.Current.CancellationToken);
            Assert.Equal(ids.Single(), replayed!.Id);

            // And still one row.
            Assert.Equal(1, await CountRowsAsync("payment"));
            replay.Dispose();
        }
        finally
        {
            foreach (var answer in answers) answer.Dispose();
        }
    }

    [Fact]
    public async Task Fifty_parallel_refunds_with_one_key_write_one_operation()
    {
        var payment = await CapturedPaymentAsync(amount: 100m);

        string key = Guid.NewGuid().ToString();
        var body = new AmountRequest(40m, "EUR");
        string path = $"/payments/{payment.Id}/refund";

        long operationsBefore = await CountOperationsAsync(payment.Id, "Refund");
        Assert.Equal(0, operationsBefore);

        var requests = BuildRequests(path, body, key);

        var answers = await Task.WhenAll(requests.Select(r => Client.SendAsync(r, TestContext.Current.CancellationToken)));

        try
        {
            // The money claim: 50 requests, 40.00 refunded once. This is the case that
            // costs real money when it is wrong, which is why it gets its own test.
            Assert.Equal(1, await CountOperationsAsync(payment.Id, "Refund"));

            Assert.Equal(40m, await QuerySqlAsync<decimal>(
                "select refunded_amount from payment where id = @Id", new { payment.Id }));

            var ok = answers.Where(a => a.StatusCode == HttpStatusCode.OK).ToList();
            var conflicts = answers.Where(a => a.StatusCode == HttpStatusCode.Conflict).ToList();

            Assert.NotEmpty(ok);
            Assert.Equal(Racers, ok.Count + conflicts.Count);

            // Every winner and every replay agrees on the amount left to refund. A second
            // refund would make this 20.00 somewhere in the set.
            foreach (var answer in ok)
            {
                var dto = await answer.Content.ReadFromJsonAsync<PaymentDto>(TestContext.Current.CancellationToken);
                Assert.Equal(40m, dto!.RefundedAmount);
                Assert.Equal(60m, dto.RefundableRemaining);
            }

            foreach (var answer in conflicts)
                Assert.Equal("idempotency_request_in_progress", await ErrorCodeAsync(answer));
        }
        finally
        {
            foreach (var answer in answers) answer.Dispose();
        }
    }

    /// <summary>
    /// The test <c>CF-035</c> named: the primary key saves the money either way, so what
    /// is really untested is the <b>answer</b> the loser gets. A 409 says "not now, come
    /// back"; a 500 says "something is broken", and a caller reading a 500 is entitled to
    /// give up or to escalate. Both races run here, so one failure covers either endpoint.
    /// </summary>
    [Fact]
    public async Task No_loser_in_the_storm_ever_gets_a_5xx()
    {
        var payment = await CapturedPaymentAsync(amount: 100m);

        var createRequests = BuildRequests("/payments", new CreatePaymentRequest(SeededMerchant, 100m, "EUR"),
            Guid.NewGuid().ToString());

        var refundRequests = BuildRequests($"/payments/{payment.Id}/refund", new AmountRequest(25m, "EUR"),
            Guid.NewGuid().ToString());

        // Both storms at once, so the connection pool is under real pressure too.
        var answers = await Task.WhenAll(createRequests.Concat(refundRequests)
            .Select(r => Client.SendAsync(r, TestContext.Current.CancellationToken)));

        try
        {
            var bad = answers.Where(a => (int)a.StatusCode >= 500).ToList();

            // Print the body, not the HttpResponseMessage. A race failure you cannot read is
            // a race failure you will delete instead of fix.
            if (bad.Count > 0)
            {
                string first = await bad[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

                Assert.Fail($"{bad.Count} of {answers.Length} racers got a 5xx. The first said: {first}");
            }

            // And no 4xx we did not design for either. 409 is the only refusal a racer earns.
            foreach (var answer in answers)
                Assert.True(
                    answer.IsSuccessStatusCode || answer.StatusCode == HttpStatusCode.Conflict,
                    $"A racer got {(int)answer.StatusCode}. Only 2xx and 409 are owed.");
        }
        finally
        {
            foreach (var answer in answers) answer.Dispose();
        }
    }

    /// <summary>
    /// <paramref name="count"/> requests to one path, all carrying the same
    /// Idempotency-Key, built and ready to send. An HttpRequestMessage cannot be sent
    /// twice, so each racer needs its own.
    /// </summary>
    private static List<HttpRequestMessage> BuildRequests(string path, object body, string key, int count = Racers)
        => Enumerable.Range(0, count).Select(_ => Build(path, body, key)).ToList();

    private static HttpRequestMessage Build(string path, object body, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, body.GetType()),
        };

        request.Headers.Add("Idempotency-Key", key);

        return request;
    }
}
