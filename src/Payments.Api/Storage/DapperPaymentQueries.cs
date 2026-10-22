using Dapper;
using Npgsql;

namespace Payments.Api.Storage
{
    public sealed class DapperPaymentQueries(string connectionString) : IPaymentQueries
    {
        public async Task<PaymentList> ListAsync(Guid merchantId, string? status, int limit, CancellationToken ct)
        {
            await using var conn = new NpgsqlConnection(connectionString);

            // Ask for one row more than the caller wants. If the extra row comes back there is more
            // to see, so there is no second count(*) and no race between the count and the page.
            // The ::text cast tells Postgres the type of @Status when it is null.
            var rows = (await conn.QueryAsync<SummaryRow>(new CommandDefinition(
                """
                select id                as "Id",
                       status            as "Status",
                       currency          as "Currency",
                       authorised_amount as "AuthorisedAmount",
                       captured_amount   as "CapturedAmount",
                       refunded_amount   as "RefundedAmount",
                       created_at        as "CreatedAt"
                from payment
                where merchant_id = @MerchantId
                  and (@Status::text is null or status = @Status)
                order by created_at desc, id desc
                limit @Take
                """,
                new { MerchantId = merchantId, Status = status, Take = limit + 1 },
                cancellationToken: ct))).AsList();

            // Strictly greater. A ">=" here would call a full last page "truncated".
            bool truncated = rows.Count > limit;

            if (truncated) rows.RemoveAt(rows.Count - 1);

            return new PaymentList([.. rows.Select(r => r.ToSummary())], truncated);
        }

        // Npgsql hands timestamptz back as a DateTime with Kind = Utc. PaymentSummary wants a
        // DateTimeOffset, and Dapper matches a record constructor by exact type, so the row lands here first.
        private sealed class SummaryRow
        {
            public Guid Id { get; init; }
            public string Status { get; init; } = "";
            public string Currency { get; init; } = "";
            public decimal AuthorisedAmount { get; init; }
            public decimal CapturedAmount { get; init; }
            public decimal RefundedAmount { get; init; }
            public DateTime CreatedAt { get; init; }

            public PaymentSummary ToSummary() =>
                new(Id, Status, Currency, AuthorisedAmount, CapturedAmount, RefundedAmount, new DateTimeOffset(CreatedAt));
        }
    }
}
