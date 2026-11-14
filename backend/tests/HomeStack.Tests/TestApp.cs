using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HomeStack.Tests;

public sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

/// <summary>Spins up the real API against a throwaway SQLite file and a fixed clock.</summary>
public sealed class TestApp : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"homestack-test-{Guid.NewGuid():N}.db");
    // Anchored to the real date: cookie auth also uses this clock, and the HTTP client drops cookies that look expired.
    public FixedTime Time { get; } = new(new DateTimeOffset(DateTime.UtcNow.Date.AddHours(12), TimeSpan.Zero));
    public DateOnly Today => DateOnly.FromDateTime(Time.Now.UtcDateTime);

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={_dbPath};Pooling=False");
        builder.ConfigureServices(s => s.Replace(ServiceDescriptor.Singleton<TimeProvider>(Time)));
    }

    /// <summary>A client logged in as the first user of a freshly set-up household.</summary>
    public async Task<HttpClient> SetupClientAsync()
    {
        var client = CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/setup",
            new { householdName = "Test Home", name = "Tester", username = "tester", password = "password123" });
        res.EnsureSuccessStatusCode();
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        foreach (var f in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            try { File.Delete(f); } catch (IOException) { }
    }
}

public static class HttpExtensions
{
    public static async Task<T> Read<T>(this HttpResponseMessage res)
    {
        if (!res.IsSuccessStatusCode)
            throw new Xunit.Sdk.XunitException($"{(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<T>(TestApp.Json))!;
    }

    public static Task<HttpResponseMessage> Post(this HttpClient c, string url, object body) =>
        c.PostAsJsonAsync(url, body, TestApp.Json);
}
