namespace Payments.Domain;

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
        ? _error
        : throw new InvalidOperationException("A success has no error. Check IsFailure first.");

    public static Result<TValue, TError> Ok(TValue value) => new(true, value, default);
    public static Result<TValue, TError> Fail(TError error) => new(false, default, error);
}