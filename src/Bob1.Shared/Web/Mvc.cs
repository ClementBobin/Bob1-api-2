using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;

namespace Bob1.Shared;

public static class ApiRoutes
{
    public const string V1 = "api/v1";
}

/// <summary>
/// Base class of every controller in every module. Controllers live in their module's
/// <c>Features/</c> folder and stay <c>internal</c> (see <see cref="MvcBuilderExtensions.AddModuleControllers"/>).
/// </summary>
[ApiController]
public abstract class ApiControllerBase : ControllerBase;

public static class JsonDefaults
{
    /// <summary>Shared by MVC, the HTTP/ProblemDetails pipeline and OpenAPI so they all serialize identically.</summary>
    public static void Configure(JsonSerializerOptions o)
    {
        o.Converters.Add(new JsonStringEnumConverter());  // "Scheduled" instead of 0
        o.Converters.Add(new UtcDateTimeJsonConverter()); // always emit UTC ("...Z")
    }
}

public static class MvcBuilderExtensions
{
    /// <summary>
    /// Registers MVC controllers from every module assembly. Modules keep their controllers (and DTOs, DbContexts...)
    /// <c>internal</c>, which MVC ignores by default, hence the custom feature provider.
    /// </summary>
    public static IMvcBuilder AddModuleControllers(this IMvcBuilder mvc, IEnumerable<Assembly> moduleAssemblies)
    {
        mvc.AddMvcOptions(o =>
        {
            // Validation is done explicitly with Guard.* so clients get our stable error codes
            // instead of MVC's automatic "field is required" ValidationProblemDetails.
            o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
        });
        mvc.AddJsonOptions(o => JsonDefaults.Configure(o.JsonSerializerOptions));

        mvc.ConfigureApplicationPartManager(manager =>
        {
            foreach (var assembly in moduleAssemblies)
                if (!manager.ApplicationParts.OfType<AssemblyPart>().Any(p => p.Assembly == assembly))
                    manager.ApplicationParts.Add(new AssemblyPart(assembly));

            manager.FeatureProviders.Add(new InternalControllerFeatureProvider());
        });
        return mvc;
    }

    /// <summary>Same rules as MVC's default provider, minus the "must be public" one.</summary>
    private sealed class InternalControllerFeatureProvider : ControllerFeatureProvider
    {
        protected override bool IsController(TypeInfo typeInfo)
        {
            if (!typeInfo.IsClass || typeInfo.IsAbstract || typeInfo.ContainsGenericParameters)
                return false;
            if (typeInfo.IsDefined(typeof(NonControllerAttribute), inherit: true))
                return false;
            return typeInfo.Name.EndsWith("Controller", StringComparison.OrdinalIgnoreCase)
                   || typeInfo.IsDefined(typeof(ControllerAttribute), inherit: true);
        }
    }
}
