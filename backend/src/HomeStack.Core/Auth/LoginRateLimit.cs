using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HomeStack.Core.Auth;

/// <summary>
/// Caps password guesses: a fixed number of login/setup/password-change attempts per minute per client IP
/// (<c>Auth:LoginAttemptsPerMinute</c>, default 10). Behind a reverse proxy every request may share one IP,
/// so the whole household shares the budget, which is plenty for real use.
/// </summary>
public static class LoginRateLimit
{
    public const string Policy = "login";

    public static IServiceCollection AddLoginRateLimit(this IServiceCollection services, IConfiguration config)
    {
        var perMinute = config.GetValue("Auth:LoginAttemptsPerMinute", 10);
        return services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = async (ctx, ct) =>
            {
                ctx.HttpContext.Response.ContentType = "application/problem+json";
                await ctx.HttpContext.Response.WriteAsJsonAsync(
                    new { status = 429, title = "Too many attempts. Wait a minute and try again." }, ct);
            };
            o.AddPolicy(Policy, http => RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
    }
}
