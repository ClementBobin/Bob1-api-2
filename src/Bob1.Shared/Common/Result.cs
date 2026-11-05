namespace Bob1.Shared;

public enum ErrorType { Validation, NotFound, Conflict, Forbidden, Unauthorized }

/// <summary>Stable machine-readable code (e.g. "match.already_started") + human description.</summary>
public sealed record Error(string Code, string Description, ErrorType Type)
{
    public static Error Validation(string code, string description) => new(code, description, ErrorType.Validation);
    public static Error NotFound(string code, string description) => new(code, description, ErrorType.NotFound);
    public static Error Conflict(string code, string description) => new(code, description, ErrorType.Conflict);
    public static Error Forbidden(string code, string description) => new(code, description, ErrorType.Forbidden);
    public static Error Unauthorized(string code, string description) => new(code, description, ErrorType.Unauthorized);
}

public class Result
{
    private readonly Error? _error;

    protected Result(Error? error) => _error = error;

    public bool IsSuccess => _error is null;
    public Error Error => _error ?? throw new InvalidOperationException("Result is successful.");

    public static Result Success() => new(null);
    public static Result Failure(Error error) => new(error);
    public static implicit operator Result(Error error) => Failure(error);
}

public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(T value) : base(null) => _value = value;
    private Result(Error error) : base(error) { }

    public T Value => IsSuccess ? _value! : throw new InvalidOperationException("Result is failed.");

    public static Result<T> Success(T value) => new(value);
    public static implicit operator Result<T>(T value) => new(value);
    public static implicit operator Result<T>(Error error) => new(error);
}
