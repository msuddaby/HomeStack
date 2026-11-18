using System.Text.Json.Serialization;
using HomeStack.Core.Auth;
using HomeStack.Core.Admin;
using HomeStack.Core.Ai;
using HomeStack.Core.Data;
using HomeStack.Core.Notifications;
using HomeStack.Core.Images;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HomeStack.Core;

public static class HomeStackExtensions
{
    /// <summary>Registers core services (auth, JSON, current user) plus every module's services.</summary>
    public static IServiceCollection AddHomeStack(this IServiceCollection services, IConfiguration config, params IModule[] modules)
    {
        foreach (var module in modules)
        {
            services.AddSingleton(module);
            module.AddServices(services, config);
        }

        services.AddHttpContextAccessor();
        services.AddScoped<HouseholdScope>();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddScoped<DigestService>();
        services.AddScoped<Admin.InstanceAdmin>();
        services.AddScoped<Admin.HouseholdDeleter>();
        services.AddHttpClient<NtfyClient>(c => c.Timeout = TimeSpan.FromSeconds(10));
        services.AddScoped<PushService>();
        services.AddHttpClient<WebPushClient>(c => c.Timeout = TimeSpan.FromSeconds(10));

        // Images: small local copies of uploads and web pictures.
        services.AddScoped<Images.ImageService>();
        services.AddScoped<Images.FileService>();
        services.AddScoped<Images.ImageCache>();
        services.AddSingleton<Images.ImageFetchFailures>();
        services.AddHttpClient<Images.ImageFetcher>(c =>
        {
            c.Timeout = TimeSpan.FromSeconds(20);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("HomeStack/0.1 (self-hosted household app)");
        });
        services.AddHostedService<Images.ImageCacheWorker>();
        services.AddHostedService<DigestWorker>();

        // AI: one provider per wire format; AiService picks the household's.
        services.AddScoped<AiService>();
        services.AddHttpClient<OpenAiCompatibleProvider>(c => c.Timeout = TimeSpan.FromSeconds(120));
        services.AddHttpClient<AnthropicProvider>(c => c.Timeout = TimeSpan.FromSeconds(120));
        services.AddScoped<IAiProvider>(sp => sp.GetRequiredService<OpenAiCompatibleProvider>());
        services.AddScoped<IAiProvider>(sp => sp.GetRequiredService<AnthropicProvider>());
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();

        services.ConfigureHttpJsonOptions(o =>
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase)));

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.Cookie.Name = "homestack";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Lax;
                o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                o.ExpireTimeSpan = TimeSpan.FromDays(60);
                o.SlidingExpiration = true;
                // This is an API: answer 401/403 instead of redirecting to a login page.
                o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
                o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
                // A removed household member is signed out on their next request.
                o.Events.OnValidatePrincipal = async ctx =>
                {
                    if (!int.TryParse(ctx.Principal?.FindFirst(HomeStackClaims.UserId)?.Value, out var userId)) return;
                    var db = ctx.HttpContext.RequestServices.GetRequiredService<HomeStackDbContext>();
                    if (!await db.Users.AnyAsync(u => u.Id == userId))
                    {
                        ctx.RejectPrincipal();
                        await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    }
                };
            });
        services.AddAuthorization();
        services.AddLoginRateLimit(config);
        services.AddProblemDetails();
        services.AddExceptionHandler<HomeStackExceptionHandler>();
        return services;
    }

    /// <summary>Maps core endpoints and mounts each module at /api/{module.Id}.</summary>
    public static IEndpointRouteBuilder MapHomeStack(this IEndpointRouteBuilder app)
    {
        app.MapAuthEndpoints();
        app.MapAdminEndpoints();
        app.MapNotificationEndpoints();
        app.MapAiEndpoints();
        app.MapImageEndpoints();
        app.MapFileEndpoints();

        var modules = app.ServiceProvider.GetServices<IModule>().ToList();
        app.MapGet("/api/modules", () => modules.Select(m => new { m.Id, m.Name })).WithTags("Core");

        foreach (var module in modules)
        {
            var group = app.MapGroup($"/api/{module.Id}").WithTags(module.Name).RequireAuthorization();
            module.MapEndpoints(group);
        }
        return app;
    }
}
