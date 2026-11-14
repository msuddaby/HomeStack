using HomeStack.Core;
using HomeStack.Modules.Inventory;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ---- Modules: register new feature modules here ----
IModule[] modules =
[
    new InventoryModule(),
];

builder.Services.AddHomeStack(builder.Configuration, modules);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? $"Data Source={Path.Combine(builder.Configuration["DataDir"] ?? "data", "homestack.db")}";
builder.Services.AddDbContext<HomeStackDbContext>(o => o.UseSqlite(connectionString, x => x.MigrationsAssembly(typeof(Program).Assembly.GetName().Name)));

builder.Services.AddOpenApi();

// Expect to run behind Tailscale serve / Caddy on a home network: trust X-Forwarded-* so
// cookies get the Secure flag when the outside connection is HTTPS.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<HomeStackDbContext>();
    var dbFile = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(connectionString).DataSource;
    if (Path.GetDirectoryName(Path.GetFullPath(dbFile)) is { } dir) Directory.CreateDirectory(dir);
    await db.Database.MigrateAsync();
    await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
}

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment()) app.MapOpenApi();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapHomeStack();

// Unknown API routes are 404s; everything else is the React app (client-side routing).
app.Map("/api/{**rest}", () => Results.NotFound());
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
