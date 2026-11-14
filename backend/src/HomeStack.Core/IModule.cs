using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HomeStack.Core;

/// <summary>
/// A HomeStack feature module (Inventory, Recipes, Shopping, ...).
/// The host discovers registered modules, adds their services, lets them contribute
/// entities to the shared DbContext, and mounts their endpoints at /api/{Id} behind auth.
/// </summary>
public interface IModule
{
    /// <summary>URL-safe id, used as the route prefix.</summary>
    string Id { get; }
    string Name { get; }

    void AddServices(IServiceCollection services, IConfiguration configuration) { }

    /// <summary>Configure this module's entities. Tables should be prefixed with the module id.</summary>
    void ConfigureModel(ModelBuilder modelBuilder);

    /// <summary>Map endpoints onto a group already scoped to /api/{Id} and requiring auth.</summary>
    void MapEndpoints(RouteGroupBuilder group);

    /// <summary>Seed per-household data (e.g. default locations) when a household is created.</summary>
    Task OnHouseholdCreatedAsync(HomeStackDbContext db, int householdId, CancellationToken ct) => Task.CompletedTask;
}
