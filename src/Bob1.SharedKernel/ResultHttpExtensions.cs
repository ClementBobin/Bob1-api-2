using Microsoft.AspNetCore.Http;

namespace Bob1.SharedKernel;

public static class ResultHttpExtensions
{
    /// <summary>RFC 9457 ProblemDetails with a stable "code" the mobile app can map to UI states.</summary>
    public static IResult ToProblem(this Error error) => Results.Problem(
        title: error.Code,
        detail: error.Description,
        statusCode: error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            _ => StatusCodes.Status500InternalServerError
        },
        extensions: new Dictionary<string, object?> { ["code"] = error.Code });
}
