using Backend.Contracts.Interfaces;
using Backend.RoutingEngine.Clients;
using Backend.RoutingEngine.Services;
using Backend.RoutingEngine.Solvers;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.RoutingEngine;

public static class DependencyInjection
{
    public static IServiceCollection AddRoutingEngine(this IServiceCollection services, string valhallaBaseUrl = "http://valhalla:8002")
    {
        services.AddHttpClient<IValhallaClient, ValhallaClient>(client =>
        {
            client.BaseAddress = new Uri(valhallaBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        services.AddScoped<OrToolsVrpSolver>();
        services.AddScoped<MockRouteOptimizer>();
        services.AddScoped<IRouteOptimizer, RouteOptimizer>();
        return services;
    }
}
