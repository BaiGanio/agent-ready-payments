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
                    // CF-019. updatedAt comes from the store, never from the wall clock:
                    // InsertAsync writes updated_at = created_at, so payment.CreatedAt is
                    // the value really in the row. DateTimeOffset.Now here made a create and
                    // a read of the same payment disagree on the field, and it also broke
                    // the injected clock the rest of the domain is built on.
                    PaymentDto.From(payment, req.MerchantId, payment.CreatedAt), JsonSerializerOptions.Web);

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

        // Authorise has no body, so null is the request for the hash. The three commands share
        // one flow: only the domain call and the request body differ.
        group.MapPost("/{id:guid}/authorise", (Guid id, IPaymentStore store, IIdempotencyStore keys,
            HttpContext http, CancellationToken ct) =>
            CommandAsync(id, req: null, p => p.Authorise(), store, keys, http, ct))
            .RequireScope("payments:write");

        group.MapPost("/{id:guid}/capture", (Guid id, AmountRequest req, IPaymentStore store, IIdempotencyStore keys,
            HttpContext http, CancellationToken ct) =>
            CommandAsync(id, req, p => p.Capture(Money.Of(req.Amount, req.Currency)), store, keys, http, ct))
            .RequireScope("payments:write");

        group.MapPost("/{id:guid}/refund", (Guid id, AmountRequest req, IPaymentStore store, IIdempotencyStore keys,
            HttpContext http, CancellationToken ct) =>
            CommandAsync(id, req, p => p.Refund(Money.Of(req.Amount, req.Currency)), store, keys, http, ct))
            .RequireScope("payments:refund");

    }

    /// <summary>
    /// The two-phase flow for `authorise`, `capture` and `refund`. It is the create flow with two
    /// differences. The merchant comes from the stored payment, so the payment is loaded before the
    /// claim — and that 404 is never stored, because it may not be true tomorrow. And a domain
    /// refusal is a real outcome: a 409 or 422 from the state machine is a final business answer,
    /// so it is stored with CompleteAsync and replayed. Only an exception is released.
    /// </summary>
    private static async Task<IResult> CommandAsync(
        Guid id,
        object? req,
        Func<Payment, Result<Payment, PaymentError>> act,
        IPaymentStore store,
        IIdempotencyStore keys,
        HttpContext http,
        CancellationToken ct)
    {
        var (key, problem) = ReadKey(http);
        if (problem is not null) return problem;

        var loaded = await store.LoadAsync(id, ct);

        if (loaded is null || loaded.MerchantId != http.Caller().MerchantId)
            return ProblemMapper.NotFound("payment_not_found", $"Payment with Id: @{id} not found.", http);

        // The path is in the hash, so one key on two endpoints, or on two payments, is a mismatch.
        string hash = RequestHash.Of("POST", http.Request.Path, req);

        switch (await keys.ClaimAsync(loaded.MerchantId, key!, hash, ct))
        {
            case Claim.Mismatch:
                return ProblemMapper.Idempotency("idempotency_key_reused",
                    StatusCodes.Status422UnprocessableEntity,
                    "That Idempotency-Key was used for a different request.", http);

            case Claim.InProgress:
                return ProblemMapper.Idempotency("idempotency_request_in_progress",
                    StatusCodes.Status409Conflict,
                    "The first request with that Idempotency-Key is still running. Try again.", http);

            case Claim.Replay replay:
                return Replay(replay, http);
        }

        try
        {
            // `loaded` is proven non-null above, but the retry reassigns it and the compiler
            // forgets that. A separate non-null local keeps the proof: `current` is assigned
            // from the checked value, and the reload below only ever assigns a checked value.
            var current = loaded;

            // One retry, no more. A second loss means a third writer, and a loop that keeps
            // retrying under load never ends. The outer catch then releases the key and rethrows.
            for (var attempt = 1; ; attempt++)
            {
                if (attempt > 1)
                {
                    // Fresh data for the second try. The key stays claimed through the retry,
                    // so another copy of the same request still gets InProgress, not a double.
                    var reloaded = await store.LoadAsync(id, ct);

                    if (reloaded is null || reloaded.MerchantId != http.Caller().MerchantId)
                        return ProblemMapper.NotFound("payment_not_found", $"Payment with Id: @{id} not found.", http);

                    current = reloaded;
                }

                var result = act(current.Payment);

                if (result.IsFailure)
                {
                    // A final answer. Store it, so a retry is told the same thing instead of
                    // being allowed to try the same illegal move again.
                    var (status, refusal) = ProblemMapper.Refusal(result.Error, http);

                    await keys.CompleteAsync(new Completion(current.MerchantId, key!, status, refusal, null), ct);

                    return Results.Content(refusal, "application/problem+json", statusCode: status);
                }

                // Serialize once. The stored body and the answer must be the same bytes.
                string body = JsonSerializer.Serialize(
                    PaymentDto.From(current.Payment, current.MerchantId, current.Payment.Operations[^1].At),
                    JsonSerializerOptions.Web);

                try
                {
                    await store.UpdateAsync(current.Payment, current.OperationCount,
                        new Completion(current.MerchantId, key!, StatusCodes.Status200OK, body, null), ct);
                }
                catch (ConcurrentUpdateException) when (attempt == 1)
                {
                    // The payment moved while we were deciding. Nothing was written. Go round
                    // once with fresh data, so the loser gets the domain's answer (422
                    // amount_exceeds_captured) and not a technical conflict it cannot act on.
                    continue;
                }

                return Results.Content(body, "application/json", statusCode: StatusCodes.Status200OK);
            }
        }
        catch
        {
            // CancellationToken.None, never ct: a cancelled token would skip the release
            // and leave the key stuck InProgress for ever.
            await keys.ReleaseAsync(loaded.MerchantId, key!, CancellationToken.None);
            throw;
        }
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
