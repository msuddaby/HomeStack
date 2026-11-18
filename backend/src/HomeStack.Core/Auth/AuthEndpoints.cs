using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using HomeStack.Core.Admin;
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

public record ChangePasswordRequest(
    [Required] string CurrentPassword,
    [Required, StringLength(200, MinimumLength = 8)] string NewPassword);

public record UserDto(int Id, string Name, string Username, bool IsAdmin = false);
public record SetAdminRequest(bool IsAdmin);
public record HouseholdDto(int Id, string Name);
/// <param name="IsInstanceAdmin">Runs this HomeStack: can bring in and manage other households.</param>
public record MeDto(UserDto User, HouseholdDto Household, bool IsInstanceAdmin = false);
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
            IPasswordHasher<User> hasher, InstanceAdmin admin, HttpContext http, CancellationToken ct) =>
        {
            if (await db.Households.AnyAsync(ct)) return Results.Conflict(new { error = "Already set up" });

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var (user, household) = await CreateHouseholdAsync(db, modules, hasher, req.HouseholdName, req.Name, req.Username, req.Password, ct);
            await tx.CommitAsync(ct);

            await SignIn(http, user);
            return Results.Ok(ToMe(user, household, admin));
        }).Validate<SetupRequest>().RequireRateLimiting(LoginRateLimit.Policy);

        auth.MapPost("/login", async (LoginRequest req, HomeStackDbContext db, IPasswordHasher<User> hasher, InstanceAdmin admin, HttpContext http) =>
        {
            var username = req.Username.Trim().ToLowerInvariant();
            var user = await db.Users.Include(u => u.Household).SingleOrDefaultAsync(u => u.Username == username);
            if (user is null || hasher.VerifyHashedPassword(user, user.PasswordHash, req.Password) == PasswordVerificationResult.Failed)
                return Results.Problem("Invalid username or password", statusCode: StatusCodes.Status401Unauthorized);

            await SignIn(http, user);
            return Results.Ok(ToMe(user, user.Household!, admin));
        }).Validate<LoginRequest>().RequireRateLimiting(LoginRateLimit.Policy);

        auth.MapPost("/password", async (ChangePasswordRequest req, ICurrentUser me, HomeStackDbContext db, IPasswordHasher<User> hasher) =>
        {
            var user = await db.Users.SingleOrDefaultAsync(u => u.Id == me.UserId) ?? throw new NotFoundException("User not found");
            if (hasher.VerifyHashedPassword(user, user.PasswordHash, req.CurrentPassword) == PasswordVerificationResult.Failed)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["currentPassword"] = ["That isn't your current password."] });
            user.PasswordHash = hasher.HashPassword(user, req.NewPassword);
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).Validate<ChangePasswordRequest>().RequireAuthorization().RequireRateLimiting(LoginRateLimit.Policy);

        auth.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });

        auth.MapGet("/me", async (ICurrentUser me, HomeStackDbContext db, InstanceAdmin admin) =>
        {
            var user = await db.Users.Include(u => u.Household).SingleOrDefaultAsync(u => u.Id == me.UserId);
            return user is null ? Results.Unauthorized() : Results.Ok(ToMe(user, user.Household!, admin));
        }).RequireAuthorization();

        var household = app.MapGroup("/api/household").WithTags("Household").RequireAuthorization();

        household.MapGet("/users", async (ICurrentUser me, HomeStackDbContext db) =>
            await db.Users.Where(u => u.HouseholdId == me.HouseholdId).OrderBy(u => u.Name)
                .Select(u => new UserDto(u.Id, u.Name, u.Username, u.IsAdmin)).ToListAsync());

        // Admins make (or stop making) other members admin. There's always at least one.
        household.MapPut("/users/{id:int}/admin", async (int id, SetAdminRequest req, ICurrentUser me, HomeStackDbContext db) =>
        {
            await me.RequireAdminAsync(db);
            var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id && u.HouseholdId == me.HouseholdId)
                ?? throw new NotFoundException("User not found");
            if (!req.IsAdmin && user.IsAdmin && await db.Users.CountAsync(u => u.HouseholdId == me.HouseholdId && u.IsAdmin) == 1)
                throw new BadRequestException("The household needs at least one admin.");
            user.IsAdmin = req.IsAdmin;
            await db.SaveChangesAsync();
            return new UserDto(user.Id, user.Name, user.Username, user.IsAdmin);
        });

        household.MapPost("/users", async (CreateUserRequest req, ICurrentUser me, HomeStackDbContext db, IPasswordHasher<User> hasher, InstanceAdmin admin) =>
        {
            if (await UsernameProblemAsync(db, admin, req.Username) is { } problem) return problem;

            var user = NewUser(me.HouseholdId, req.Name, req.Username, req.Password, hasher);
            db.Users.Add(user);
            await db.SaveChangesAsync();
            return Results.Created($"/api/household/users/{user.Id}", new UserDto(user.Id, user.Name, user.Username));
        }).Validate<CreateUserRequest>();

        // Their history (who added or used what) stays; it stores the id without a foreign key.
        household.MapDelete("/users/{id:int}", async (int id, ICurrentUser me, HomeStackDbContext db) =>
        {
            if (id == me.UserId) throw new BadRequestException("You can't remove yourself.");
            var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id && u.HouseholdId == me.HouseholdId)
                ?? throw new NotFoundException("User not found");
            if (user.IsAdmin && await db.Users.CountAsync(u => u.HouseholdId == me.HouseholdId && u.IsAdmin) == 1)
                throw new BadRequestException("That's the household's only admin. Make someone else admin first.");
            db.Users.Remove(user);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }

    /// <summary>A validation problem if the username is taken or reserved for the instance admin; otherwise null.</summary>
    internal static async Task<IResult?> UsernameProblemAsync(HomeStackDbContext db, InstanceAdmin admin, string username)
    {
        var u = username.Trim().ToLowerInvariant();
        var error = admin.IsAdminUsername(u) ? "That username is reserved." : await db.Users.AnyAsync(x => x.Username == u) ? "Username is taken." : null;
        return error is null ? null : Results.ValidationProblem(new Dictionary<string, string[]> { ["username"] = [error] });
    }

    /// <summary>A new household with its first member as admin, set up by every module. Call inside a transaction.</summary>
    internal static async Task<(User User, Household Household)> CreateHouseholdAsync(HomeStackDbContext db, IEnumerable<IModule> modules,
        IPasswordHasher<User> hasher, string householdName, string name, string username, string password, CancellationToken ct)
    {
        var household = new Household { Name = householdName.Trim() };
        db.Households.Add(household);
        await db.SaveChangesAsync(ct);

        var user = NewUser(household.Id, name, username, password, hasher);
        user.IsAdmin = true;
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        foreach (var module in modules) await module.OnHouseholdCreatedAsync(db, household.Id, ct);
        return (user, household);
    }

    private static User NewUser(int householdId, string name, string username, string password, IPasswordHasher<User> hasher)
    {
        var user = new User { HouseholdId = householdId, Name = name.Trim(), Username = username.Trim().ToLowerInvariant() };
        user.PasswordHash = hasher.HashPassword(user, password);
        return user;
    }

    internal static Task SignIn(HttpContext http, User user)
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

    internal static MeDto ToMe(User u, Household h, InstanceAdmin admin) =>
        new(new UserDto(u.Id, u.Name, u.Username, u.IsAdmin), new HouseholdDto(h.Id, h.Name), admin.IsAdminUsername(u.Username));
}
