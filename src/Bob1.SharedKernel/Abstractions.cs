using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bob1.SharedKernel;

/// <summary>Who is calling. Implemented in Bob1.Api from the JWT claims; modules only see this interface.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid UserId { get; }
    Guid PersonId { get; }
    Guid? FamilyId { get; }
    bool IsInRole(string role);
}

public sealed record JwtOptions(string Issuer, string Audience, string Key);

public static class Policies
{
    public const string Parent = nameof(Parent);
    public const string Staff = nameof(Staff);
    public const string Admin = nameof(Admin);
}

/// <summary>Every module exposes exactly this to the host.</summary>
public interface IModule
{
    void Register(IServiceCollection services, IConfiguration configuration);
    void MapEndpoints(IEndpointRouteBuilder api);
    /// <summary>Dev convenience: apply this module's migrations on startup.</summary>
    Task InitializeAsync(IServiceProvider services) => Task.CompletedTask;
}

public static class SharedKernelExtensions
{
    public static IServiceCollection AddSharedKernel(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        return services;
    }
}
