using HomeStack.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HomeStack.Modules.Inventory;

public class InventoryModule : IModule
{
    public string Id => "inventory";
    public string Name => "Inventory";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<InventoryOptions>(configuration.GetSection("Inventory"));
        services.AddScoped<InventoryService>();
        services.AddHttpClient<OpenFoodFactsClient>(c =>
        {
            c.BaseAddress = new Uri(configuration["Inventory:OpenFoodFactsUrl"] ?? "https://world.openfoodfacts.org/");
            c.Timeout = TimeSpan.FromSeconds(6);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("HomeStack/0.1 (self-hosted household app)");
        });
    }

    public void ConfigureModel(ModelBuilder mb)
    {
        mb.Entity<Location>(e =>
        {
            e.ToTable("inventory_locations");
            e.Property(x => x.Name).HasMaxLength(60);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
            e.HasIndex(x => x.HouseholdId);
        });
        mb.Entity<Product>(e =>
        {
            e.ToTable("inventory_products");
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.Category).HasMaxLength(32);
            e.HasIndex(x => new { x.HouseholdId, x.Name });
            e.HasIndex(x => new { x.HouseholdId, x.Barcode }).IsUnique().HasFilter("\"Barcode\" IS NOT NULL");
        });
        mb.Entity<InventoryItem>(e =>
        {
            e.ToTable("inventory_items");
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.HouseholdId, x.ExpiresAt });
        });
        mb.Entity<InventoryEvent>(e =>
        {
            e.ToTable("inventory_events");
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(16);
            e.HasIndex(x => new { x.HouseholdId, x.At });
        });
    }

    public async Task OnHouseholdCreatedAsync(HomeStackDbContext db, int householdId, CancellationToken ct)
    {
        db.AddRange(
            new Location { HouseholdId = householdId, Name = "Pantry", Kind = LocationKind.Pantry, Sort = 0 },
            new Location { HouseholdId = householdId, Name = "Fridge", Kind = LocationKind.Fridge, Sort = 1 },
            new Location { HouseholdId = householdId, Name = "Freezer", Kind = LocationKind.Freezer, Sort = 2 });
        await db.SaveChangesAsync(ct);
    }

    public void MapEndpoints(RouteGroupBuilder g)
    {
        g.MapGet("/categories", () => Categories.All);
        g.MapGet("/dashboard", (InventoryService s) => s.GetDashboardAsync());

        // Locations
        g.MapGet("/locations", (InventoryService s) => s.GetLocationsAsync());
        g.MapPost("/locations", (LocationRequest req, InventoryService s) => s.CreateLocationAsync(req)).Validate<LocationRequest>();
        g.MapPut("/locations/{id:int}", (int id, LocationRequest req, InventoryService s) => s.UpdateLocationAsync(id, req)).Validate<LocationRequest>();
        g.MapDelete("/locations/{id:int}", async (int id, InventoryService s) => { await s.DeleteLocationAsync(id); return Results.NoContent(); });

        // Products
        g.MapGet("/products", (string? q, int? limit, InventoryService s) => s.SearchProductsAsync(q, limit ?? 20));
        g.MapPost("/products", (ProductRequest req, InventoryService s) => s.CreateProductAsync(req)).Validate<ProductRequest>();
        g.MapPut("/products/{id:int}", (int id, ProductRequest req, InventoryService s) => s.UpdateProductAsync(id, req)).Validate<ProductRequest>();
        g.MapGet("/barcode/{code}", async (string code, InventoryService s, OpenFoodFactsClient off, CancellationToken ct) =>
        {
            if (await s.FindByBarcodeAsync(code) is { } local) return new BarcodeLookupDto("local", local, null);
            var suggestion = await off.LookupAsync(code, ct);
            return new BarcodeLookupDto(suggestion is null ? "none" : "openfoodfacts", null, suggestion);
        });

        // Items
        g.MapGet("/items", (int? locationId, LocationKind? kind, string? q, InventoryService s) => s.ListItemsAsync(locationId, kind, q));
        g.MapGet("/items/{id:int}", (int id, InventoryService s) => s.GetItemAsync(id));
        g.MapPost("/items", (AddItemRequest req, InventoryService s) => s.AddItemAsync(req)).Validate<AddItemRequest>();
        g.MapPut("/items/{id:int}", (int id, UpdateItemRequest req, InventoryService s) => s.UpdateItemAsync(id, req)).Validate<UpdateItemRequest>();
        g.MapPost("/items/{id:int}/consume", async (int id, ConsumeRequest req, InventoryService s) =>
            await s.ConsumeAsync(id, req) is { } remaining ? Results.Ok(remaining) : Results.NoContent()).Validate<ConsumeRequest>();
        g.MapPost("/items/{id:int}/move", (int id, MoveRequest req, InventoryService s) => s.MoveAsync(id, req)).Validate<MoveRequest>();
        g.MapDelete("/items/{id:int}", async (int id, InventoryService s) => { await s.DeleteItemAsync(id); return Results.NoContent(); });
    }
}
