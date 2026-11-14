using System.Net;
using System.Net.Sockets;
using HomeStack.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HomeStack.Modules.Meals;

/// <summary>Recipes, "what can I make?", and the weekly meal plan. Depends on Inventory and Shopping.</summary>
public class MealsModule : IModule
{
    public string Id => "meals";
    public string Name => "Meals";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<MealsService>();
        services.AddHttpClient<RecipeImporter>(c =>
            {
                c.Timeout = TimeSpan.FromSeconds(12);
                c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; HomeStack/0.1; recipe import)");
                c.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 5,
                AutomaticDecompression = DecompressionMethods.All,
                // Check the address actually connected to (covers redirects and DNS rebinding), not just the first lookup.
                ConnectCallback = async (ctx, ct) =>
                {
                    var addresses = await Dns.GetHostAddressesAsync(ctx.DnsEndPoint.Host, ct);
                    var ip = addresses.FirstOrDefault(a => !RecipeImporter.IsPrivate(a))
                        ?? throw new HttpRequestException("Only public websites can be imported.");
                    if (addresses.Any(RecipeImporter.IsPrivate)) throw new HttpRequestException("Only public websites can be imported.");
                    var socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                    try { await socket.ConnectAsync(ip, ctx.DnsEndPoint.Port, ct); return new NetworkStream(socket, ownsSocket: true); }
                    catch { socket.Dispose(); throw; }
                },
            });
    }

    public void ConfigureModel(ModelBuilder mb)
    {
        mb.Entity<Recipe>(e =>
        {
            e.ToTable("meals_recipes");
            e.Property(x => x.Title).HasMaxLength(150);
            e.HasMany(x => x.Ingredients).WithOne().HasForeignKey(i => i.RecipeId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.HouseholdId);
        });
        mb.Entity<RecipeIngredient>(e =>
        {
            e.ToTable("meals_recipe_ingredients");
            e.Property(x => x.Text).HasMaxLength(300);
            e.Property(x => x.Name).HasMaxLength(300);
            e.HasIndex(x => x.RecipeId);
        });
        mb.Entity<MealPlanEntry>(e =>
        {
            e.ToTable("meals_plan");
            e.Property(x => x.Slot).HasConversion<string>().HasMaxLength(16);
            e.HasOne(x => x.Recipe).WithMany().HasForeignKey(x => x.RecipeId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.HouseholdId, x.Date });
        });
    }

    public void MapEndpoints(RouteGroupBuilder g)
    {
        g.MapGet("/recipes", (string? q, string? tag, MealsService s) => s.ListRecipesAsync(q, tag));
        g.MapGet("/suggestions", (MealsService s) => s.SuggestAsync());
        g.MapGet("/recipes/{id:int}", (int id, MealsService s) => s.GetRecipeAsync(id));
        g.MapPost("/recipes", (RecipeRequest req, MealsService s) => s.CreateRecipeAsync(req)).Validate<RecipeRequest>();
        g.MapPut("/recipes/{id:int}", (int id, RecipeRequest req, MealsService s) => s.UpdateRecipeAsync(id, req)).Validate<RecipeRequest>();
        g.MapDelete("/recipes/{id:int}", async (int id, MealsService s) => { await s.DeleteRecipeAsync(id); return Results.NoContent(); });
        g.MapPut("/recipes/{id:int}/ingredients/{ingredientId:int}/link", (int id, int ingredientId, LinkRequest req, MealsService s) =>
            s.LinkIngredientAsync(id, ingredientId, req));
        g.MapPost("/import", (ImportRequest req, RecipeImporter importer, CancellationToken ct) => importer.ImportAsync(req.Url, ct)).Validate<ImportRequest>();

        g.MapGet("/recipes/{id:int}/cook", (int id, int? servings, MealsService s) => s.GetCookPlanAsync(id, servings));
        g.MapPost("/cook", (CookRequest req, MealsService s) => s.CookAsync(req)).Validate<CookRequest>();
        g.MapPost("/recipes/{id:int}/shopping", (int id, MealsService s) => s.AddMissingToListAsync(id));

        g.MapGet("/plan", (DateOnly from, DateOnly to, MealsService s) => s.GetPlanAsync(from, to));
        g.MapPost("/plan", (PlanRequest req, MealsService s) => s.AddPlanAsync(req)).Validate<PlanRequest>();
        g.MapPut("/plan/{id:int}", (int id, PlanRequest req, MealsService s) => s.UpdatePlanAsync(id, req)).Validate<PlanRequest>();
        g.MapDelete("/plan/{id:int}", async (int id, MealsService s) => { await s.DeletePlanAsync(id); return Results.NoContent(); });
        g.MapPost("/plan/shopping", (PlanShoppingRequest req, MealsService s) => s.AddPlanMissingToListAsync(req));
    }
}
