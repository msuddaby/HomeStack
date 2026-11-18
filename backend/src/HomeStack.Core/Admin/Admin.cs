using System.Security.Cryptography;
using System.Text;
using HomeStack.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HomeStack.Core.Admin;

// One HomeStack can hold several households. The instance admin (config Admin:Usernames, "matt" by default) brings
// new ones in with invite links and manages them — names, members' passwords, deleting — without seeing their data.

/// <summary>Instance-wide settings (one row).</summary>
public class InstanceSettings
{
    public int Id { get; set; } = 1;
    /// <summary>Households without their own AI provider use the instance admin's household's.</summary>
    public bool ShareAi { get; set; }

    internal static void Configure(ModelBuilder mb) => mb.Entity<InstanceSettings>(e =>
    {
        e.ToTable("instance_settings");
        e.Property(x => x.Id).ValueGeneratedNever();
    });
}

/// <summary>A link that lets one person create a new household and its first (admin) account.</summary>
public class HouseholdInvite
{
    public int Id { get; set; }
    /// <summary>SHA-256 of the token in the link; the token itself is only shown when the invite is made.</summary>
    public required string TokenHash { get; set; }
    /// <summary>Who it's for ("Sarah and Tom"), for the admin's list.</summary>
    public string? Note { get; set; }
    public int CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public int? UsedForHouseholdId { get; set; }
    public DateTime? RevokedAt { get; set; }

    public bool IsOpen(DateTime now) => UsedAt is null && RevokedAt is null && ExpiresAt > now;

    internal static void Configure(ModelBuilder mb) => mb.Entity<HouseholdInvite>(e =>
    {
        e.ToTable("household_invites");
        e.HasIndex(x => x.TokenHash).IsUnique();
        e.Property(x => x.TokenHash).HasMaxLength(64);
        e.Property(x => x.Note).HasMaxLength(80);
        // A deleted household leaves the used invite behind as history.
        e.HasOne<Household>().WithMany().HasForeignKey(x => x.UsedForHouseholdId).OnDelete(DeleteBehavior.SetNull);
    });

    public static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

/// <summary>Who runs this HomeStack instance, and the settings only they can change.</summary>
public class InstanceAdmin(HomeStackDbContext db, ICurrentUser me, IConfiguration config)
{
    /// <summary>Usernames that are instance admins. Reserved: nobody else can sign up with them.</summary>
    public IReadOnlyList<string> Usernames { get; } = Load(config);

    private static List<string> Load(IConfiguration config)
    {
        var names = config.GetSection("Admin:Usernames").Get<string[]>()
            ?? (config["Admin:Usernames"] is { Length: > 0 } one ? one.Split(',') : ["matt"]);
        return names.Select(n => n.Trim().ToLowerInvariant()).Where(n => n.Length > 0).ToList();
    }

    public bool IsAdminUsername(string username) => Usernames.Contains(username.Trim().ToLowerInvariant());

    public async Task<bool> IsMeAsync(CancellationToken ct = default)
    {
        var uid = me.UserId;
        var username = await db.Users.Where(u => u.Id == uid).Select(u => u.Username).SingleOrDefaultAsync(ct);
        return username is not null && IsAdminUsername(username);
    }

    public async Task RequireAsync(CancellationToken ct = default)
    {
        if (!await IsMeAsync(ct)) throw new ForbiddenException("Only the HomeStack admin can do that.");
    }

    /// <summary>The instance admin's own household: the one whose AI can be shared. Null if there's no admin account.</summary>
    public async Task<(int Id, string HouseholdName, string AdminName)?> MainHouseholdAsync(CancellationToken ct = default)
    {
        var names = Usernames;
        var admin = await db.Users.Where(u => names.Contains(u.Username)).OrderBy(u => u.Id)
            .Select(u => new { u.HouseholdId, Household = u.Household!.Name, u.Name }).FirstOrDefaultAsync(ct);
        return admin is null ? null : (admin.HouseholdId, admin.Household, admin.Name);
    }

    public async Task<InstanceSettings> SettingsAsync(CancellationToken ct = default) =>
        await db.Set<InstanceSettings>().SingleOrDefaultAsync(ct) ?? new InstanceSettings();
}

/// <summary>
/// Removes a household and everything in it. Core tables cascade from <c>households</c>, but module tables only carry
/// a <c>HouseholdId</c> column, so every table in the model that has one is cleared first (their child rows go by
/// their own cascades). New modules are covered without extra code.
/// </summary>
public class HouseholdDeleter(HomeStackDbContext db)
{
    public async Task DeleteAsync(int householdId, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Tables reference each other (items → products…): check foreign keys at commit, once everything is gone.
        await db.Database.ExecuteSqlRawAsync("PRAGMA defer_foreign_keys = ON;", ct);
        var tables = db.Model.GetEntityTypes()
            .Where(t => t.ClrType != typeof(Household) && t.FindProperty("HouseholdId") is not null && t.GetTableName() is not null)
            .Select(t => (Table: t.GetTableName()!, Column: t.FindProperty("HouseholdId")!.GetColumnName()))
            .Distinct();
        foreach (var (table, column) in tables)
            await db.Database.ExecuteSqlRawAsync($"DELETE FROM \"{table}\" WHERE \"{column}\" = {{0}}", [householdId], ct);
        await db.Households.Where(h => h.Id == householdId).ExecuteDeleteAsync(ct);
        await tx.CommitAsync(ct);
    }
}
