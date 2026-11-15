using HomeStack.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HomeStack.Api;

public class BackupOptions
{
    /// <summary>Where backups go. Defaults to {DataDir}/backups; in Docker, mount a separate folder at /backups.</summary>
    public string? Directory { get; set; }
    /// <summary>How many daily backups to keep.</summary>
    public int Keep { get; set; } = 14;
    /// <summary>Local hour after which the day's backup is taken.</summary>
    public int Hour { get; set; } = 3;
}

public record BackupFileDto(string Name, long SizeBytes, DateTime CreatedAt);
public record BackupsDto(string Directory, int Keep, IReadOnlyList<BackupFileDto> Files);

/// <summary>Daily copies of the SQLite database via <c>VACUUM INTO</c> (consistent while the app is running).</summary>
public class BackupService(HomeStackDbContext db, IOptions<BackupOptions> options, IConfiguration config, TimeProvider time, ILogger<BackupService> logger)
{
    private const string Prefix = "homestack-";
    private readonly BackupOptions _opt = options.Value;

    public string Directory => Path.GetFullPath(_opt.Directory ?? Path.Combine(config["DataDir"] ?? "data", "backups"));

    private string FileFor(DateOnly day) => Path.Combine(Directory, $"{Prefix}{day:yyyy-MM-dd}.db");

    public bool HasBackupFor(DateOnly day) => File.Exists(FileFor(day));

    public BackupsDto List()
    {
        var files = System.IO.Directory.Exists(Directory)
            ? new DirectoryInfo(Directory).GetFiles($"{Prefix}*.db").OrderByDescending(f => f.Name)
                .Select(f => new BackupFileDto(f.Name, f.Length, f.LastWriteTimeUtc)).ToList()
            : [];
        return new BackupsDto(Directory, _opt.Keep, files);
    }

    /// <summary>Writes today's backup (replacing one already taken today) and prunes old ones.</summary>
    public async Task<BackupFileDto> RunAsync(CancellationToken ct = default)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var target = FileFor(time.Today());
        var temp = target + ".tmp";
        File.Delete(temp);
        // Path goes in as a SQL string literal; VACUUM INTO doesn't take parameters.
        var sql = "VACUUM INTO '" + temp.Replace("'", "''") + "'";
        await db.Database.ExecuteSqlRawAsync(sql, ct);
        File.Move(temp, target, overwrite: true);

        foreach (var old in new DirectoryInfo(Directory).GetFiles($"{Prefix}*.db").OrderByDescending(f => f.Name).Skip(Math.Max(1, _opt.Keep)))
            old.Delete();

        var info = new FileInfo(target);
        logger.LogInformation("Backed up database to {File} ({Size:N0} bytes)", target, info.Length);
        return new BackupFileDto(info.Name, info.Length, info.LastWriteTimeUtc);
    }
}

internal sealed class BackupWorker(IServiceScopeFactory scopes, IConfiguration config, IOptions<BackupOptions> options, TimeProvider time, ILogger<BackupWorker> logger)
    : PeriodicWorker(scopes, config, logger)
{
    protected override TimeSpan Interval => TimeSpan.FromMinutes(30);

    protected override async Task TickAsync(IServiceProvider services, CancellationToken ct)
    {
        if (time.GetLocalNow().Hour < options.Value.Hour) return;
        var backups = services.GetRequiredService<BackupService>();
        if (!backups.HasBackupFor(time.Today())) await backups.RunAsync(ct);
    }
}

public static class BackupEndpoints
{
    public static void AddBackups(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<BackupOptions>(config.GetSection("Backup"));
        services.AddScoped<BackupService>();
        services.AddHostedService<BackupWorker>();
    }

    public static void MapBackupEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/system/backups").WithTags("System").RequireAuthorization();
        g.MapGet("/", (BackupService b) => b.List());
        g.MapPost("/", (BackupService b, CancellationToken ct) => b.RunAsync(ct));
    }
}
