namespace Compass.Server.Services;

/// <summary>
/// The outcome of a domain operation: a value, or a message and the status code it deserves.
///
/// Domain rules live in services, but the HTTP status a broken rule maps to is part of the rule, not
/// an afterthought for the endpoint to guess at. Carrying both keeps that decision in one place.
/// </summary>
public readonly record struct OperationResult<T>
{
    private OperationResult(T? value, string? error, int statusCode)
    {
        Value = value;
        Error = error;
        StatusCode = statusCode;
    }

    public T? Value { get; }

    public string? Error { get; }

    public int StatusCode { get; }

    public bool Succeeded => Error is null;

    public static OperationResult<T> Ok(T value) => new(value, null, StatusCodes.Status200OK);

    public static OperationResult<T> NotFound(string error = "Not found.") =>
        new(default, error, StatusCodes.Status404NotFound);

    public static OperationResult<T> Forbidden(string error) =>
        new(default, error, StatusCodes.Status403Forbidden);

    public static OperationResult<T> Invalid(string error) =>
        new(default, error, StatusCodes.Status400BadRequest);

    public static OperationResult<T> Conflict(string error) =>
        new(default, error, StatusCodes.Status409Conflict);

    /// <summary>Turns the result into an HTTP response, so endpoints stay one line long.</summary>
    public IResult ToHttpResult() =>
        Succeeded
            ? Results.Ok(Value)
            : Results.Json(new { error = Error }, statusCode: StatusCode);
}
