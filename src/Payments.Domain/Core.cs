namespace Payments.Domain;

public sealed class CurrencyMismatchException(string message) : Exception(message);
public sealed record PaymentError(PaymentErrorCode Code, string Message);
public sealed record PaymentOperation(PaymentAction Kind, Money Amount, DateTimeOffset At);


public enum PaymentAction { Authorise, Decline, Capture, Void, Refund }
public enum PaymentErrorCode { IllegalTransition, AmountNotPositive, AmountExceedsAuthorised, AmountExceedsCaptured, CurrencyMismatch }
public enum PaymentStatus { Created, Authorised, Declined,  Voided, PartiallyCaptured, Captured, PartiallyRefunded, Refunded }

public class Result<TValue, TError>
{
    private readonly TValue? _value;
    private readonly TError? _error;
 
    private Result(bool isSuccess, TValue? value, TError? error)
    {
        IsSuccess = isSuccess;
        _value = value;
        _error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;

    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("A failure has no value. Check IsSuccess first.");

    public TError Error => IsFailure
        ? _error!
        : throw new InvalidOperationException("A success has no error. Check IsFailure first.");

    public static Result<TValue, TError> Ok(TValue value) => new(true, value, default);
    public static Result<TValue, TError> Fail(TError error) => new(false, default, error);
}