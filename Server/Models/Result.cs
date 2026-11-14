namespace Server.Models;

public class Result<T>
{
    public bool Success { get; private init; }
    public T? Value { get; private init; }
    public string? ErrorCode { get; private init; }
    public string? ErrorMessage { get; private init; }

    public static Result<T> Ok(T value) => new() { Success = true, Value = value };
    public static Result<T> Fail(string code, string message) =>
        new() { ErrorCode = code, ErrorMessage = message };
}