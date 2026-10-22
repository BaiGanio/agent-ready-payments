using System.Text.Json;
using Payments.Api.Auth;
using Payments.Api.Storage;
using Payments.Domain;

namespace Payments.Api;

public static class PaymentEndpoints
{
    public static void MapPayments(this WebApplication app)
    {
        var group = app.MapGroup("/payments").RequireApiKey();

        group.MapGet("/", async (string? status, int? limit, IPaymentQueries queries, HttpContext http, CancellationToken ct) =>
        {
            int take = limit ?? 20;
            
            if (take is < 1 or > 20)
                return ProblemMapper.BadRequest("limit_out_of_range", "The limit must be between 1 and 20.", http);
            
            if (status is not null
                && !(Enum.TryParse<PaymentStatus>(status, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed)))
                return ProblemMapper.BadRequest("invalid_status", $"{status} is not a payment status.", http);

            var list = await queries.ListAsync(http.Caller().MerchantId, status, take, ct);
            return Results.Ok(list);
        }).RequireScope("payments:read");

        group.MapPost("/", async (CreatePaymentRequest req, IPaymentStore store, IIdempotencyStore keys,
            TimeProvider clock, HttpContext http, CancellationToken ct) =>
        {
            // The order here is the design. Header, merchant, hash, claim, work.
            var (key, problem) = ReadKey(http);
            if (problem is not null) return problem;

            // Before the claim: a key row needs a real merchant, and a 404 may not be true tomorrow.
            var merchantId = await store.FindMerchantAsync(req.MerchantId, ct);
            if(merchantId is null)
                return ProblemMapper.NotFound("merchant_not_found", $"Merchant with Id: @{req.MerchantId} not found.", http);

            if (req.MerchantId != http.Caller().MerchantId)
                return ProblemMapper.Auth(StatusCodes.Status403Forbidden, "merchant_mismatch",
                    "You can only create payments for your own merchant.", http);

            string hash = RequestHash.Of("POST", "/payments", req);

            switch (await keys.ClaimAsync(req.MerchantId, key!, hash, ct))
            {
                case Claim.Mismatch:
                    return ProblemMapper.Idempotency("idempotency_key_reused",
                        StatusCodes.Status422UnprocessableEntity,
                        "That Idempotency-Key was used for a different request body.", http);

                case Claim.InProgress:
                    return ProblemMapper.Idempotency("idempotency_request_in_progress",
                        StatusCodes.Status409Conflict,
                        "The first request with that Idempotency-Key is still running. Try again.", http);

                case Claim.Replay replay:
                    return Replay(replay, http);
            }

            // We own the key. Do the work, and store the answer in the same transaction.
            try
            {
                var payment = Payment.Create(Guid.CreateVersion7(), Money.Of(req.Amount, req.Currency), clock);

                string location = $"/payments/{payment.Id}";

                // Serialize once. The stored body and the answer must be the same bytes,
                // or a replay would not match the first answer.
                string body = JsonSerializer.Serialize(
                    PaymentDto.From(payment, req.MerchantId, DateTimeOffset.Now), JsonSerializerOptions.Web);

                await store.InsertAsync(payment, req.MerchantId,
                    new Completion(req.MerchantId, key!, StatusCodes.Status201Created, body, location), ct);

                http.Response.Headers.Location = location;

                return Results.Content(body, "application/json", statusCode: StatusCodes.Status201Created);
            }
            catch
            {
                // CancellationToken.None, never ct: if the client hung up, ct is already cancelled,
                // the release would be skipped, and the key would be stuck InProgress for ever.
                await keys.ReleaseAsync(req.MerchantId, key!, CancellationToken.None);
                throw;
            }
        }).RequireScope("payments:write");

        group.MapGet("/{id:guid}", async(Guid id, IPaymentStore store, HttpContext http, CancellationToken ct) =>
        {
            var loaded = await store.LoadAsync(id, ct);

            if(loaded is null || loaded.MerchantId != http.Caller().MerchantId)
                return ProblemMapper.NotFound("payment_not_found", $"Payment with Id: @{id} not found.", http);

            var dto = PaymentDto.From(loaded.Payment, loaded.MerchantId, loaded.UpdatedAt);

            return Results.Ok(dto);
        }).RequireScope("payments:read");

        group.MapPost("/{id:guid}/authorise", async(Guid id, IPaymentStore store, HttpContext http, CancellationToken cancellationToken) =>
        {
            // Step 10 puts the header check on all four POSTs. The key itself is used in step 12.
            var (_, problem) = ReadKey(http);
            if (problem is not null) return problem;

            var loaded = await store.LoadAsync(id, cancellationToken);

           if(loaded is null || loaded.MerchantId != http.Caller().MerchantId)
                return ProblemMapper.NotFound("payment_not_found", $"Payment with Id: @{id} not found.", http);

            var result = loaded.Payment.Authorise();

            if(result.IsFailure)
                return ProblemMapper.ToProblem(result.Error, http);

            await store.UpdateAsync(loaded.Payment, loaded.OperationCount, completion: null, cancellationToken);

            return Results.Ok(PaymentDto.From(loaded.Payment, loaded.MerchantId, loaded.UpdatedAt));
        }).RequireScope("payments:write");

        group.MapPost("/{id:guid}/capture", async(Guid id, AmountRequest req, IPaymentStore store, HttpContext http, CancellationToken cancellationToken) =>
        {
            // Step 10 puts the header check on all four POSTs. The key itself is used in step 12.
            var (_, problem) = ReadKey(http);
            if (problem is not null) return problem;

            var loaded = await store.LoadAsync(id, cancellationToken);

            if(loaded is null || loaded.MerchantId != http.Caller().MerchantId)
                return ProblemMapper.NotFound("payment_not_found", $"Payment with Id: @{id} not found.", http);

            var result = loaded.Payment.Capture(Money.Of(req.Amount, req.Currency));

            if(result.IsFailure)
                return ProblemMapper.ToProblem(result.Error, http);

            await store.UpdateAsync(loaded.Payment, loaded.OperationCount, completion: null, cancellationToken);

            return Results.Ok(PaymentDto.From(loaded.Payment, loaded.MerchantId, loaded.Payment.Operations[^1].At));
        }).RequireScope("payments:write");

        group.MapPost("/{id:guid}/refund", async(Guid id, AmountRequest req, IPaymentStore store, HttpContext http, CancellationToken cancellationToken) =>
        {
            // Step 10 puts the header check on all four POSTs. The key itself is used in step 12.
            var (_, problem) = ReadKey(http);
            if (problem is not null) return problem;

            var loaded = await store.LoadAsync(id, cancellationToken);

            if(loaded is null || loaded.MerchantId != http.Caller().MerchantId)
                return ProblemMapper.NotFound("payment_not_found", $"Payment with Id: @{id} not found.", http);

            var result = loaded.Payment.Refund(Money.Of(req.Amount, req.Currency));

            if(result.IsFailure)
                return ProblemMapper.ToProblem(result.Error, http);

            await store.UpdateAsync(loaded.Payment, loaded.OperationCount, completion: null, cancellationToken);

            return Results.Ok(PaymentDto.From(loaded.Payment, loaded.MerchantId, loaded.Payment.Operations[^1].At));
        }).RequireScope("payments:refund");

    }

    /// <summary>
    /// The Idempotency-Key header, checked before anything else runs, so a bad header never
    /// reaches the database. Returns the key, or the problem to return instead — never both.
    /// </summary>
    private static (string? Key, IResult? Problem) ReadKey(HttpContext http)
    {
        string? key = http.Request.Headers["Idempotency-Key"];

        if (string.IsNullOrWhiteSpace(key))
            return (null, ProblemMapper.Idempotency("idempotency_key_required",
                StatusCodes.Status400BadRequest,
                "Every money-moving POST needs an Idempotency-Key header.", http));

        // The column stops at 255, so the edge refuses a longer key before the insert does.
        if (key.Length > 255)
            return (null, ProblemMapper.Idempotency("idempotency_key_invalid",
                StatusCodes.Status400BadRequest,
                "The Idempotency-Key must be 255 characters or fewer.", http));

        return (key, null);
    }

    /// <summary>
    /// The stored answer again, byte for byte, with a header that says it is a repeat.
    /// A replay is a stored answer, not a fresh read of the payment.
    /// </summary>
    private static IResult Replay(Claim.Replay replay, HttpContext http)
    {
        http.Response.Headers["Idempotent-Replayed"] = "true";

        if (replay.Location is not null)
            http.Response.Headers.Location = replay.Location;

        return Results.Content(
            replay.Body,
            replay.StatusCode >= 400 ? "application/problem+json" : "application/json",
            statusCode: replay.StatusCode);
    }
}
