using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using HomeStack.Core.Notifications;
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
    public string BackupDir { get; } = Path.Combine(Path.GetTempPath(), $"homestack-test-backups-{Guid.NewGuid():N}");
    /// <summary>Stands in for the ntfy server.</summary>
    public FakeHttp Ntfy { get; } = new();
    /// <summary>Stands in for browser push services (Apple, Google, Mozilla).</summary>
    public FakeHttp Push { get; } = new();
    /// <summary>Stands in for every AI service (OpenAI-compatible and Anthropic).</summary>
    public FakeHttp Ai { get; } = new();
    /// <summary>Stands in for websites that product and recipe pictures are downloaded from.</summary>
    public FakeHttp Web { get; } = new();
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
        builder.UseSetting("BackgroundJobs:Enabled", "false");
        builder.UseSetting("Backup:Directory", BackupDir);
        builder.UseSetting("Backup:Keep", "3");
        builder.ConfigureServices(s => s.Replace(ServiceDescriptor.Singleton<TimeProvider>(Time)));
        builder.ConfigureTestServices(s =>
        {
            s.AddHttpClient<NtfyClient>().ConfigurePrimaryHttpMessageHandler(() => Ntfy);
            s.AddHttpClient<WebPushClient>().ConfigurePrimaryHttpMessageHandler(() => Push);
            s.AddHttpClient<HomeStack.Core.Ai.OpenAiCompatibleProvider>().ConfigurePrimaryHttpMessageHandler(() => Ai);
            s.AddHttpClient<HomeStack.Core.Ai.AnthropicProvider>().ConfigurePrimaryHttpMessageHandler(() => Ai);
            s.AddHttpClient<HomeStack.Core.Images.ImageFetcher>().ConfigurePrimaryHttpMessageHandler(() => Web);
        });
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
        try { Directory.Delete(BackupDir, recursive: true); } catch (IOException) { }
    }
}

/// <summary>Records requests and answers with <see cref="Status"/>.</summary>
public sealed class FakeHttp : HttpMessageHandler
{
    public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];
    /// <summary>Request bodies as sent, for binary content (encrypted push messages).</summary>
    public List<byte[]> RawBodies { get; } = [];
    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
    /// <summary>Response bodies to hand out in order; the last one repeats. Default "{}".</summary>
    public Queue<string> Bodies { get; } = new();
    /// <summary>When set, successful responses carry these bytes instead of <see cref="Bodies"/>.</summary>
    public byte[]? Binary { get; set; }
    private string _last = "{}";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var raw = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(ct);
        RawBodies.Add(raw);
        Requests.Add((request, System.Text.Encoding.UTF8.GetString(raw)));
        if (Binary is not null && Status == HttpStatusCode.OK)
            return new HttpResponseMessage(Status) { Content = new ByteArrayContent(Binary) };
        if (Bodies.Count > 0) _last = Bodies.Dequeue();
        return new HttpResponseMessage(Status) { Content = new StringContent(Status == HttpStatusCode.OK ? _last : "{\"error\":{\"message\":\"nope\"}}") };
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
