using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using HomeStack.Core.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace HomeStack.Core.Auth;

public record SetupRequest(
    [Required, StringLength(80, MinimumLength = 1)] string HouseholdName,
    [Required, StringLength(80, MinimumLength = 1)] string Name,
    [Required, StringLength(40, MinimumLength = 2), RegularExpression("^[a-zA-Z0-9._-]+$")] string Username,
    [Required, StringLength(200, MinimumLength = 8)] string Password);

public record CreateUserRequest(
    [Required, StringLength(80, MinimumLength = 1)] string Name,
    [Required, StringLength(40, MinimumLength = 2), RegularExpression("^[a-zA-Z0-9._-]+$")] string Username,
    [Required, StringLength(200, MinimumLength = 8)] string Password);

public record LoginRequest([Required] string Username, [Required] string Password);

public record UserDto(int Id, string Name, string Username);
public record HouseholdDto(int Id, string Name);
public record MeDto(UserDto User, HouseholdDto Household);
public record AuthStatusDto(bool NeedsSetup, bool Authenticated);

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth").WithTags("Auth");

        auth.MapGet("/status", async (HomeStackDbContext db, HttpContext http) =>
            new AuthStatusDto(!await db.Households.AnyAsync(), http.User.Identity?.IsAuthenticated == true));

        // First-run: create the household and its first user. Only allowed when no household exists.
        auth.MapPost("/setup", async (SetupRequest req, HomeStackDbContext db, IEnumerable<IModule> modules,
            IPasswordHasher<User> hasher, HttpContext http, CancellationToken ct) =>
        {
            if (await db.Households.AnyAsync(ct)) return Results.Conflict(new { error = "Already set up" });

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var household = new Household { Name = req.HouseholdName.Trim() };
            db.Households.Add(household);
            await db.SaveChangesAsync(ct);

            var user = NewUser(household.Id, req.Name, req.Username, req.Password, hasher);
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);

            foreach (var module in modules) await module.OnHouseholdCreatedAsync(db, household.Id, ct);
            await tx.CommitAsync(ct);

            await SignIn(http, user);
            return Results.Ok(ToMe(user, household));
        }).Validate<SetupRequest>();

        auth.MapPost("/login", async (LoginRequest req, HomeStackDbContext db, IPasswordHasher<User> hasher, HttpContext http) =>
        {
            var username = req.Username.Trim().ToLowerInvariant();
            var user = await db.Users.Include(u => u.Household).SingleOrDefaultAsync(u => u.Username == username);
            if (user is null || hasher.VerifyHashedPassword(user, user.PasswordHash, req.Password) == PasswordVerificationResult.Failed)
                return Results.Problem("Invalid username or password", statusCode: StatusCodes.Status401Unauthorized);

            await SignIn(http, user);
            return Results.Ok(ToMe(user, user.Household!));
        }).Validate<LoginRequest>();

        auth.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });

        auth.MapGet("/me", async (ICurrentUser me, HomeStackDbContext db) =>
        {
            var user = await db.Users.Include(u => u.Household).SingleOrDefaultAsync(u => u.Id == me.UserId);
            return user is null ? Results.Unauthorized() : Results.Ok(ToMe(user, user.Household!));
        }).RequireAuthorization();

        var household = app.MapGroup("/api/household").WithTags("Household").RequireAuthorization();

        household.MapGet("/users", async (ICurrentUser me, HomeStackDbContext db) =>
            await db.Users.Where(u => u.HouseholdId == me.HouseholdId).OrderBy(u => u.Name)
                .Select(u => new UserDto(u.Id, u.Name, u.Username)).ToListAsync());

        household.MapPost("/users", async (CreateUserRequest req, ICurrentUser me, HomeStackDbContext db, IPasswordHasher<User> hasher) =>
        {
            var username = req.Username.Trim().ToLowerInvariant();
            if (await db.Users.AnyAsync(u => u.Username == username))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["username"] = ["Username is taken."] });

            var user = NewUser(me.HouseholdId, req.Name, req.Username, req.Password, hasher);
            db.Users.Add(user);
            await db.SaveChangesAsync();
            return Results.Created($"/api/household/users/{user.Id}", new UserDto(user.Id, user.Name, user.Username));
        }).Validate<CreateUserRequest>();
    }

    private static User NewUser(int householdId, string name, string username, string password, IPasswordHasher<User> hasher)
    {
        var user = new User { HouseholdId = householdId, Name = name.Trim(), Username = username.Trim().ToLowerInvariant() };
        user.PasswordHash = hasher.HashPassword(user, password);
        return user;
    }

    private static Task SignIn(HttpContext http, User user)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(HomeStackClaims.UserId, user.Id.ToString()),
            new Claim(HomeStackClaims.HouseholdId, user.HouseholdId.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
        ], CookieAuthenticationDefaults.AuthenticationScheme);
        return http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });
    }

    private static MeDto ToMe(User u, Household h) => new(new UserDto(u.Id, u.Name, u.Username), new HouseholdDto(h.Id, h.Name));
}
