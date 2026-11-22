namespace Caninos.Api.Middleware;

public abstract class ApiException : Exception
{
    public int StatusCode { get; }
    public string ErrorTitle { get; }
    public Dictionary<string, string[]> Errors { get; }

    protected ApiException(
        int statusCode,
        string errorTitle,
        string message,
        Dictionary<string, string[]>? errors = null) : base(message)
    {
        StatusCode = statusCode;
        ErrorTitle = errorTitle;
        Errors = errors ?? new Dictionary<string, string[]>();
    }
}

public sealed class ApiValidationException : ApiException
{
    public ApiValidationException(string message, Dictionary<string, string[]>? errors = null)
        : base(StatusCodes.Status400BadRequest, "Bad Request", message, errors)
    {
    }

    public ApiValidationException(Dictionary<string, List<string>> errors)
        : base(
            StatusCodes.Status400BadRequest,
            "Bad Request",
            "Error de validación en los datos enviados.",
            errors.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToArray()))
    {
    }

    public static ApiValidationException Single(string field, string errorMessage) =>
        new("Error de validación en los datos enviados.", new Dictionary<string, string[]>
        {
            [field] = [errorMessage]
        });
}

public sealed class ApiUnauthorizedException : ApiException
{
    public ApiUnauthorizedException(string message)
        : base(StatusCodes.Status401Unauthorized, "Unauthorized", message)
    {
    }
}

public sealed class ApiNotFoundException : ApiException
{
    public ApiNotFoundException(string message)
        : base(StatusCodes.Status404NotFound, "Not Found", message)
    {
    }
}

public sealed class ApiConflictException : ApiException
{
    public ApiConflictException(string message, Dictionary<string, string[]>? errors = null)
        : base(StatusCodes.Status409Conflict, "Conflict", message, errors)
    {
    }
}
