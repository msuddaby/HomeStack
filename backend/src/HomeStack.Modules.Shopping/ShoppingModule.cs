using HomeStack.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HomeStack.Modules.Shopping;

/// <summary>Shared shopping list. Depends on Inventory for products, low-stock and put-away.</summary>
public class ShoppingModule : IModule
{
    public string Id => "shopping";
    public string Name => "Shopping";

    public void AddServices(IServiceCollection services, IConfiguration configuration) =>
        services.AddScoped<ShoppingService>();

    public void ConfigureModel(ModelBuilder mb) =>
        mb.Entity<ShoppingItem>(e =>
        {
            e.ToTable("shopping_items");
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.Category).HasMaxLength(32);
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(16);
            e.HasIndex(x => new { x.HouseholdId, x.ProductId });
        });

    public void MapEndpoints(RouteGroupBuilder g)
    {
        g.MapGet("/items", (ShoppingService s) => s.GetListAsync());
        g.MapPost("/items", (AddShoppingItemRequest req, ShoppingService s) => s.AddAsync(req)).Validate<AddShoppingItemRequest>();
        g.MapPut("/items/{id:int}", (int id, UpdateShoppingItemRequest req, ShoppingService s) => s.UpdateAsync(id, req)).Validate<UpdateShoppingItemRequest>();
        g.MapPost("/items/{id:int}/check", (int id, CheckRequest req, ShoppingService s) => s.CheckAsync(id, req.Checked));
        g.MapDelete("/items/{id:int}", async (int id, ShoppingService s) => { await s.RemoveAsync(id); return Results.NoContent(); });
        g.MapPost("/clear-checked", async (ShoppingService s) => Results.Ok(new { cleared = await s.ClearCheckedAsync() }));
        g.MapPost("/put-away", (PutAwayRequest req, ShoppingService s) => s.PutAwayAsync(req)).Validate<PutAwayRequest>();
    }
}
