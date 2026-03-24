namespace FlowPilot.Shared;

/// <summary>
/// Represents an error with a code and description. Used with <see cref="Result{T}"/>.
/// </summary>
public sealed record Error(string Code, string Description)
{
    public static readonly Error None = new(string.Empty, string.Empty);
    public static readonly Error NullValue = new("Error.NullValue", "A null value was provided.");

    public static Error NotFound(string entity, Guid id) =>
        new($"{entity}.NotFound", $"{entity} with id '{id}' was not found.");

    public static Error Validation(string code, string description) =>
        new(code, description);

    public static Error Conflict(string code, string description) =>
        new(code, description);

    public static Error Unauthorized(string description = "Unauthorized.") =>
        new("Auth.Unauthorized", description);

    public static Error Forbidden(string description = "Forbidden.") =>
        new("Auth.Forbidden", description);
}

/// <summary>
/// Discriminated result type for business logic. Never throw exceptions for business errors.
/// </summary>
public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    public static Result Success() => new(true, Error.None);
    public static Result Failure(Error error) => new(false, error);
    public static Result<T> Success<T>(T value) => new(value, true, Error.None);
    public static Result<T> Failure<T>(Error error) => new(default, false, error);
}

/// <summary>
/// Generic result carrying a value on success. All service methods return this instead of throwing.
/// </summary>
public class Result<T> : Result
{
    private readonly T? _value;

    internal Result(T? value, bool isSuccess, Error error)
        : base(isSuccess, error)
    {
        _value = value;
    }

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot access Value on a failed result.");

    public static implicit operator Result<T>(T value) => Success(value);
}
