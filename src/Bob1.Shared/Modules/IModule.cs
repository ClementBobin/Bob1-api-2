using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bob1.Shared;

/// <summary>Every module exposes exactly this to the host.</summary>
public interface IModule
{
    void Register(IServiceCollection services, IConfiguration configuration);
    /// <summary>Dev convenience: apply this module's migrations on startup.</summary>
    Task InitializeAsync(IServiceProvider services) => Task.CompletedTask;
}

public static class SharedExtensions
{
    public static IServiceCollection AddShared(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        return services;
    }
}
