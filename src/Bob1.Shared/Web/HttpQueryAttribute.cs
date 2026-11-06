using Microsoft.AspNetCore.Mvc.Routing;

namespace Bob1.Shared;

/// <summary>
/// The HTTP QUERY method (IETF "safe method with body"): a safe, idempotent read whose search /
/// filter / paging criteria travel in the request body instead of an ever-growing query string.
/// ASP.NET Core has no built-in attribute for it, hence this one.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class HttpQueryAttribute : HttpMethodAttribute
{
    private static readonly string[] Methods = ["QUERY"];

    public HttpQueryAttribute() : base(Methods) { }

    public HttpQueryAttribute(string template) : base(Methods, template) { }
}
