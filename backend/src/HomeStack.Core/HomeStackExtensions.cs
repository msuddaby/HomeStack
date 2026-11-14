using System.Text.Json.Serialization;
using HomeStack.Core.Auth;
using HomeStack.Core.Data;
using Microsoft.AspNetCore.Authentication.Cookies;
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
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
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
            });
        services.AddAuthorization();
        services.AddProblemDetails();
        services.AddExceptionHandler<HomeStackExceptionHandler>();
        return services;
    }

    /// <summary>Maps core endpoints and mounts each module at /api/{module.Id}.</summary>
    public static IEndpointRouteBuilder MapHomeStack(this IEndpointRouteBuilder app)
    {
        app.MapAuthEndpoints();

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
