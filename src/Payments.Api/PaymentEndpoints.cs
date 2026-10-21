using Payments.Api.Auth;
using Payments.Api.Storage;
using Payments.Domain;

namespace Payments.Api;

public static class PaymentEndpoints
{
    public static void MapPayments(this WebApplication app)
    {
        var group = app.MapGroup("/payments").RequireApiKey();

        group.MapPost("/", async (CreatePaymentRequest req, IPaymentStore store, TimeProvider clock, HttpContext http, CancellationToken ct) =>
        {
            var merchantId = await store.FindMerchantAsync(req.MerchantId, ct);
            if(merchantId is null)
                return ProblemMapper.NotFound("merchant_not_found", $"Merchant with Id: @{req.MerchantId} not found.", http);

            if (req.MerchantId != http.Caller().MerchantId)
                return ProblemMapper.Auth(StatusCodes.Status403Forbidden, "merchant_mismatch",
                    "You can only create payments for your own merchant.", http);

            var payment = Payment.Create(Guid.CreateVersion7(), Money.Of(req.Amount, req.Currency), clock);

            await store.InsertAsync(payment, req.MerchantId, completion: null, ct);

            return Results.Created($"/payments/{payment.Id}", PaymentDto.From(payment, req.MerchantId, DateTimeOffset.Now));
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
}