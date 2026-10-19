namespace Payments.Domain;
public sealed class Payment
{
    private readonly List<PaymentOperation> _operations = [];
    private readonly TimeProvider _clock;

    // TODO: private constructor. Create() validates and is the only door in.
    private Payment(string id, Money amount, TimeProvider clock)
    {
        _clock = clock;

        Id = id;
        Amount = amount;
        Status = PaymentStatus.Created;
        CapturedTotal = Money.Zero(amount.Currency);
        RefundedTotal = Money.Zero(amount.Currency);
        CreatedAt = clock.GetUtcNow();
    }

    public string Id { get; }
    public Money Amount { get; }
    public PaymentStatus Status { get; private set; }
    public Money CapturedTotal { get; private set; }
    public Money RefundedTotal { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public string? DeclineReason { get; private set; }
    public IReadOnlyList<PaymentOperation> Operations => _operations.AsReadOnly();

    public static Payment Create(string id, Money amount, TimeProvider clock)
    {
        if(string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("An id is required", nameof(id));

        if(amount.IsZero)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "An amount must be positive.");

        ArgumentNullException.ThrowIfNull(clock);
        
        
        return new Payment(id, amount, clock);
    }
    // TODO: Status = Created, CapturedTotal = RefundedTotal = Money.Zero(currency),
    //       CreatedAt = clock.GetUtcNow()



    // TODO: Authorise() : only from Created. Append one Authorise operation for the
    //       full Amount, stamped with _clock.GetUtcNow(). Return Ok(this).
    // TODO: Decline(reason) : only from Created. Blank reason -> ArgumentException.
    //       Keep the reason. The operation amount is Money.Zero(currency).

    public Result<Payment, PaymentError> Authorise()
    {
        if(Status != PaymentStatus.Created)
            return Fail(PaymentErrorCode.IllegalTransition, $"Cannot authorise a payment that is {Status}");

        Record(PaymentAction.Authorise, Amount);
        Status = PaymentStatus.Authorised;
        
        return Ok();
    }

    public Result<Payment, PaymentError> Decline(string reason)
    {
        if(string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A decline reason is required", nameof(reason));

        if(Status != PaymentStatus.Created)
            return Fail(PaymentErrorCode.IllegalTransition, $"Cannot decline a payment that is {Status}");

        Record(PaymentAction.Decline, Money.Zero(Amount.Currency));
        Status = PaymentStatus.Declined;
        DeclineReason = reason;

        return Ok();
    }



    // ----- helpers -----
    private void Record(PaymentAction kind, Money amount) 
        => _operations.Add(new PaymentOperation(kind, amount, _clock.GetUtcNow())); 
    private Result<Payment, PaymentError> Ok() => Result<Payment, PaymentError>.Ok(this);
    private Result<Payment, PaymentError> Fail(PaymentErrorCode code, string message) 
        => Result<Payment, PaymentError>.Fail(new PaymentError(code, message));
}
