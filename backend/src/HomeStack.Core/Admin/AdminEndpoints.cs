using System.ComponentModel.DataAnnotations;
using HomeStack.Core.Ai;
using HomeStack.Core.Auth;
using HomeStack.Core.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HomeStack.Core.Admin;

/// <param name="OwnAi">Has its own AI connection (which it uses even when sharing is on).</param>
/// <param name="SharedAi">The admin shares their AI with this household.</param>
public record AdminHouseholdDto(int Id, string Name, DateTime CreatedAt, int Members, List<string> Admins, bool OwnAi, bool IsMine, bool SharedAi);
public record AdminUserDto(int Id, string Name, string Username, bool IsAdmin);
public record RenameHouseholdRequest([Required, StringLength(80, MinimumLength = 1)] string Name);
public record ResetPasswordRequest([Required, StringLength(200, MinimumLength = 8)] string NewPassword);

public record InviteDto(int Id, string? Note, DateTime CreatedAt, DateTime ExpiresAt, DateTime? UsedAt, string? Household, DateTime? RevokedAt);
/// <param name="Token">Goes in the link (/join/{token}). Shown only now: only a hash of it is kept.</param>
public record NewInviteDto(InviteDto Invite, string Token);
public record CreateInviteRequest([StringLength(80)] string? Note, [Range(1, 30)] int Days = 7);

/// <param name="MainHousehold">The admin's household, whose AI can be shared.</param>
/// <param name="MainHasAi">It has an AI connection to share.</param>
public record InstanceSettingsDto(string? MainHousehold, bool MainHasAi);
public record SharedAiRequest(bool Shared);

/// <summary>What someone opening an invite link sees before signing up.</summary>
public record InviteCheckDto(bool Valid, string? Note, DateTime? ExpiresAt);
public record JoinRequest(
    [Required, StringLength(200)] string Token,
    [Required, StringLength(80, MinimumLength = 1)] string HouseholdName,
    [Required, StringLength(80, MinimumLength = 1)] string Name,
    [Required, StringLength(40, MinimumLength = 2), RegularExpression("^[a-zA-Z0-9._-]+$")] string Username,
    [Required, StringLength(200, MinimumLength = 8)] string Password);

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        // ---------- Joining with an invite (signed out) ----------
        var auth = app.MapGroup("/api/auth").WithTags("Auth");

        auth.MapGet("/invites/{token}", async (string token, HomeStackDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            var invite = await FindAsync(db, token, ct);
            return invite is not null && invite.IsOpen(time.GetUtcNow().UtcDateTime)
                ? new InviteCheckDto(true, invite.Note, invite.ExpiresAt)
                : new InviteCheckDto(false, null, null);
        }).RequireRateLimiting(LoginRateLimit.Policy);

        auth.MapPost("/join", async (JoinRequest req, HomeStackDbContext db, IEnumerable<IModule> modules, IPasswordHasher<User> hasher,
            InstanceAdmin admin, TimeProvider time, HttpContext http, CancellationToken ct) =>
        {
            if (await AuthEndpoints.UsernameProblemAsync(db, admin, req.Username) is { } problem) return problem;
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var now = time.GetUtcNow().UtcDateTime;
            var invite = await FindAsync(db, req.Token, ct);
            if (invite is null || !invite.IsOpen(now))
                throw new BadRequestException("This invite has been used or has expired. Ask for a new one.");
            var (user, household) = await AuthEndpoints.CreateHouseholdAsync(db, modules, hasher, req.HouseholdName, req.Name, req.Username, req.Password, ct);
            invite.UsedAt = now;
            invite.UsedForHouseholdId = household.Id;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            await AuthEndpoints.SignIn(http, user);
            return Results.Ok(AuthEndpoints.ToMe(user, household, admin));
        }).Validate<JoinRequest>().RequireRateLimiting(LoginRateLimit.Policy);

        // ---------- Managing households (instance admin only) ----------
        var g = app.MapGroup("/api/admin").WithTags("Admin").RequireAuthorization()
            .AddEndpointFilter(async (ctx, next) =>
            {
                await ctx.HttpContext.RequestServices.GetRequiredService<InstanceAdmin>().RequireAsync(ctx.HttpContext.RequestAborted);
                return await next(ctx);
            });

        g.MapGet("/households", async (HomeStackDbContext db, ICurrentUser me, CancellationToken ct) =>
        {
            var mine = me.HouseholdId;
            var users = await db.Users.Select(u => new { u.HouseholdId, u.Name, u.IsAdmin }).ToListAsync(ct);
            var ownAi = await db.Set<AiSettings>().Select(a => a.HouseholdId).ToListAsync(ct);
            return (await db.Households.OrderBy(h => h.Name).ToListAsync(ct)).Select(h => new AdminHouseholdDto(
                h.Id, h.Name, h.CreatedAt, users.Count(u => u.HouseholdId == h.Id),
                users.Where(u => u.HouseholdId == h.Id && u.IsAdmin).Select(u => u.Name).Order().ToList(),
                ownAi.Contains(h.Id), h.Id == mine, h.UseSharedAi && h.Id != mine)).ToList();
        });

        g.MapPut("/households/{id:int}", async (int id, RenameHouseholdRequest req, HomeStackDbContext db, CancellationToken ct) =>
        {
            var h = await db.Households.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Household not found");
            h.Name = req.Name.Trim();
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).Validate<RenameHouseholdRequest>();

        // ?confirmName=<the household's name>, typed by the admin.
        g.MapDelete("/households/{id:int}", async (int id, string? confirmName, HomeStackDbContext db, ICurrentUser me,
            HouseholdDeleter deleter, CancellationToken ct) =>
        {
            var h = await db.Households.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Household not found");
            if (h.Id == me.HouseholdId) throw new BadRequestException("You can't delete your own household.");
            if (!string.Equals(confirmName?.Trim(), h.Name, StringComparison.OrdinalIgnoreCase))
                throw new BadRequestException("Type the household's name to confirm.");
            await deleter.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        g.MapGet("/households/{id:int}/users", async (int id, HomeStackDbContext db, CancellationToken ct) =>
            await db.Users.Where(u => u.HouseholdId == id).OrderBy(u => u.Name)
                .Select(u => new AdminUserDto(u.Id, u.Name, u.Username, u.IsAdmin)).ToListAsync(ct));

        // For someone who's forgotten theirs: there's no email to reset it with.
        g.MapPost("/users/{id:int}/password", async (int id, ResetPasswordRequest req, HomeStackDbContext db, IPasswordHasher<User> hasher, CancellationToken ct) =>
        {
            var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id, ct) ?? throw new NotFoundException("User not found");
            user.PasswordHash = hasher.HashPassword(user, req.NewPassword);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).Validate<ResetPasswordRequest>();

        // Invites: open ones, plus those used or revoked in the last 30 days.
        g.MapGet("/invites", async (HomeStackDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            var since = time.GetUtcNow().UtcDateTime.AddDays(-30);
            var invites = await db.Set<HouseholdInvite>().Where(i => i.CreatedAt >= since || (i.UsedAt == null && i.RevokedAt == null))
                .OrderByDescending(i => i.CreatedAt).ToListAsync(ct);
            return await ToDtosAsync(db, invites, ct);
        });

        g.MapPost("/invites", async (CreateInviteRequest req, HomeStackDbContext db, ICurrentUser me, TimeProvider time, CancellationToken ct) =>
        {
            var token = HouseholdInvite.NewToken();
            var now = time.GetUtcNow().UtcDateTime;
            var invite = new HouseholdInvite
            {
                TokenHash = HouseholdInvite.Hash(token), Note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim(),
                CreatedByUserId = me.UserId, CreatedAt = now, ExpiresAt = now.AddDays(req.Days),
            };
            db.Add(invite);
            await db.SaveChangesAsync(ct);
            return new NewInviteDto((await ToDtosAsync(db, [invite], ct))[0], token);
        }).Validate<CreateInviteRequest>();

        g.MapDelete("/invites/{id:int}", async (int id, HomeStackDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            var invite = await db.Set<HouseholdInvite>().SingleOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException("Invite not found");
            if (invite.UsedAt is null) invite.RevokedAt ??= time.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        g.MapGet("/settings", async (InstanceAdmin admin, HomeStackDbContext db, CancellationToken ct) =>
        {
            var main = await admin.MainHouseholdAsync(ct);
            var hasAi = main is { } m && await db.Set<AiSettings>().AnyAsync(a => a.HouseholdId == m.Id, ct);
            return new InstanceSettingsDto(main?.HouseholdName, hasAi);
        });

        // Share (or stop sharing) the admin's AI with one household.
        g.MapPut("/households/{id:int}/shared-ai", async (int id, SharedAiRequest req, HomeStackDbContext db, ICurrentUser me, CancellationToken ct) =>
        {
            var h = await db.Households.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Household not found");
            if (h.Id == me.HouseholdId) throw new BadRequestException("That's your own household: it uses your AI already.");
            h.UseSharedAi = req.Shared;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    private static async Task<HouseholdInvite?> FindAsync(HomeStackDbContext db, string token, CancellationToken ct)
    {
        var hash = HouseholdInvite.Hash(token.Trim());
        return await db.Set<HouseholdInvite>().SingleOrDefaultAsync(i => i.TokenHash == hash, ct);
    }

    private static async Task<List<InviteDto>> ToDtosAsync(HomeStackDbContext db, List<HouseholdInvite> invites, CancellationToken ct)
    {
        var ids = invites.Select(i => i.UsedForHouseholdId).OfType<int>().ToList();
        var names = await db.Households.Where(h => ids.Contains(h.Id)).ToDictionaryAsync(h => h.Id, h => h.Name, ct);
        return invites.Select(i => new InviteDto(i.Id, i.Note, i.CreatedAt, i.ExpiresAt, i.UsedAt,
            i.UsedForHouseholdId is int h ? names.GetValueOrDefault(h) : null, i.RevokedAt)).ToList();
    }
}
