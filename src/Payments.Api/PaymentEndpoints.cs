using Payments.Api.Storage;
using Payments.Domain;

namespace Payments.Api;

public static class PaymentEndpoints
{
    public static void MapPayments(this WebApplication app)
    {
        var group = app.MapGroup("/payments");

        group.MapPost("/", async (CreatePaymentRequest req, IPaymentStore store, TimeProvider clock, HttpContext http, CancellationToken ct) =>
        {
            var merchantId = await store.FindMerchantAsync(req.MerchantId, ct);
            if(merchantId is null)
                return ProblemMapper.NotFound("merchant_not_found", $"Merchant with Id: @{req.MerchantId} not found.", http);

            var payment = Payment.Create(Guid.CreateVersion7(), Money.Of(req.Amount, req.Currency), clock);

            await store.InsertAsync(payment, req.MerchantId, ct);

            return Results.Created($"/payments/{payment.Id}", PaymentDto.From(payment, req.MerchantId, DateTimeOffset.Now));
        });

        group.MapGet("/{id:guid}", async(Guid id, IPaymentStore store, HttpContext http, CancellationToken ct) =>
        {
            var loaded = await store.LoadAsync(id, ct);

            if(loaded is null)
                return ProblemMapper.NotFound("payment_not_found", $"Payment with Id: @{id} not found.", http);

            var dto = PaymentDto.From(loaded.Payment, loaded.MerchantId, loaded.UpdatedAt);

            return Results.Ok(dto);
        });

        group.MapPost("/{id:guid}/authorise", async(Guid id, IPaymentStore store, HttpContext http, CancellationToken cancellationToken) =>
        {
            var loaded = await store.LoadAsync(id, cancellationToken);

            if(loaded is null)
                return ProblemMapper.NotFound("payment_not_found", $"Payment with Id: @{id} not found.", http);

            var result = loaded.Payment.Authorise();

            if(result.IsFailure)
                return ProblemMapper.ToProblem(result.Error, http);

            await store.UpdateAsync(loaded.Payment, loaded.OperationCount, cancellationToken);

            return Results.Ok(PaymentDto.From(loaded.Payment, loaded.MerchantId, loaded.UpdatedAt));
        });

        group.MapPost("/{id:guid}/capture", async(Guid id, AmountRequest req, IPaymentStore store, HttpContext http, CancellationToken cancellationToken) =>
        {
            var loaded = await store.LoadAsync(id, cancellationToken);

            if(loaded is null)
                return ProblemMapper.NotFound("payment_not_found", $"Payment with Id: @{id} not found.", http);

            var result = loaded.Payment.Capture(Money.Of(req.Amount, req.Currency));

            if(result.IsFailure)
                return ProblemMapper.ToProblem(result.Error, http);

            await store.UpdateAsync(loaded.Payment, loaded.OperationCount, cancellationToken);

            return Results.Ok(PaymentDto.From(loaded.Payment, loaded.MerchantId, loaded.Payment.Operations[^1].At));
        });

         group.MapPost("/{id:guid}/refund", async(Guid id, AmountRequest req, IPaymentStore store, HttpContext http, CancellationToken cancellationToken) =>
        {
            var loaded = await store.LoadAsync(id, cancellationToken);

            if(loaded is null)
                return ProblemMapper.NotFound("payment_not_found", $"Payment with Id: @{id} not found.", http);

            var result = loaded.Payment.Refund(Money.Of(req.Amount, req.Currency));

            if(result.IsFailure)
                return ProblemMapper.ToProblem(result.Error, http);

            await store.UpdateAsync(loaded.Payment, loaded.OperationCount, cancellationToken);

            return Results.Ok(PaymentDto.From(loaded.Payment, loaded.MerchantId, loaded.Payment.Operations[^1].At));
        });

    }
}