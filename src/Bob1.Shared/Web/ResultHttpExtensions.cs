using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Bob1.Shared;

public static class ResultHttpExtensions
{
    /// <summary>RFC 9457 ProblemDetails with a stable "code" the mobile app can map to UI states.</summary>
    public static IActionResult ToProblem(this Error error) => new ErrorActionResult(error);

    private sealed class ErrorActionResult(Error error) : IActionResult
    {
        public Task ExecuteResultAsync(ActionContext context)
        {
            var status = error.Type switch
            {
                ErrorType.Validation => StatusCodes.Status400BadRequest,
                ErrorType.NotFound => StatusCodes.Status404NotFound,
                ErrorType.Conflict => StatusCodes.Status409Conflict,
                ErrorType.Forbidden => StatusCodes.Status403Forbidden,
                ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
                _ => StatusCodes.Status500InternalServerError
            };

            var factory = context.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
            var problem = factory.CreateProblemDetails(context.HttpContext,
                statusCode: status, title: error.Code, detail: error.Description);
            problem.Extensions["code"] = error.Code;

            var result = new ObjectResult(problem) { StatusCode = status };
            result.ContentTypes.Add("application/problem+json");
            return result.ExecuteResultAsync(context);
        }
    }
}
