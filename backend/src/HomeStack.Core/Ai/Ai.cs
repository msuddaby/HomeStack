using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HomeStack.Core.Ai;

/// <summary>Which wire format to speak. Almost every service (DeepSeek, OpenAI, Ollama, OpenRouter, LM Studio,
/// Groq, vLLM…) offers the OpenAI chat-completions format; Claude gets its native API.</summary>
public enum AiProviderKind { OpenAiCompatible, Anthropic }

/// <summary>Per-household AI connection (one row per household).</summary>
public class AiSettings
{
    public int HouseholdId { get; set; }
    public AiProviderKind Provider { get; set; } = AiProviderKind.OpenAiCompatible;
    public string BaseUrl { get; set; } = "https://api.deepseek.com";
    public string? ApiKey { get; set; }
    /// <summary>Model for text and, unless <see cref="VisionModel"/> is set, images too.</summary>
    public string Model { get; set; } = "deepseek-flash";
    /// <summary>Optional separate model for photos (e.g. a local text model plus a vision one).</summary>
    public string? VisionModel { get; set; }

    internal static void Configure(ModelBuilder mb) => mb.Entity<AiSettings>(e =>
    {
        e.ToTable("ai_settings");
        e.HasKey(x => x.HouseholdId);
        e.Property(x => x.HouseholdId).ValueGeneratedNever();
        e.Property(x => x.Provider).HasConversion<string>().HasMaxLength(24);
        e.Property(x => x.BaseUrl).HasMaxLength(300);
        e.Property(x => x.ApiKey).HasMaxLength(300);
        e.Property(x => x.Model).HasMaxLength(120);
        e.Property(x => x.VisionModel).HasMaxLength(120);
        e.HasOne<Data.Household>().WithOne().HasForeignKey<AiSettings>(x => x.HouseholdId).OnDelete(DeleteBehavior.Cascade);
    });
}

public record AiImage(byte[] Data, string MediaType);

/// <param name="System">Instructions.</param>
/// <param name="User">The request; should describe the JSON shape wanted.</param>
/// <param name="MaxTokens">Reply length limit, for Anthropic (which requires one). OpenAI-compatible services use their own maximum.</param>
public record AiPrompt(string System, string User, IReadOnlyList<AiImage>? Images = null, int MaxTokens = 4096)
{
    public bool HasImages => Images is { Count: > 0 };
}

/// <summary>One wire format. Returns the model's raw text reply; throws <see cref="AiException"/> on failure.</summary>
public interface IAiProvider
{
    AiProviderKind Kind { get; }
    Task<string> CompleteAsync(AiSettings settings, string model, AiPrompt prompt, bool json, CancellationToken ct);
}

/// <summary>A failure talking to the AI service, with a message fit to show the user. Answers 502.</summary>
public class AiException(string message) : HomeStackException(Microsoft.AspNetCore.Http.StatusCodes.Status502BadGateway, message);

/// <summary>What modules use: "ask the household's AI for JSON". Knows nothing about receipts or recipes.</summary>
public class AiService(HomeStackDbContext db, ICurrentUser me, IEnumerable<IAiProvider> providers, Admin.InstanceAdmin instance, ILogger<AiService> logger)
{
    // Models sometimes write numbers as strings ("servings": "4").
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { NumberHandling = JsonNumberHandling.AllowReadingFromString };

    /// <summary>The household's own AI connection (what Settings → AI edits), if it has one.</summary>
    public async Task<AiSettings?> GetSettingsAsync(CancellationToken ct = default) =>
        await db.Set<AiSettings>().SingleOrDefaultAsync(s => s.HouseholdId == me.HouseholdId, ct);

    /// <summary>
    /// What AI calls use: the household's own connection, or else the instance admin's household's when the admin
    /// shares it with this household (<see cref="Data.Household.UseSharedAi"/>). <c>SharedBy</c> names the admin for a shared one.
    /// </summary>
    public async Task<(AiSettings Settings, string? SharedBy)?> GetEffectiveAsync(CancellationToken ct = default)
    {
        if (await GetSettingsAsync(ct) is { } own) return (own, null);
        var hid = me.HouseholdId;
        if (!await db.Households.AnyAsync(h => h.Id == hid && h.UseSharedAi, ct)
            || await instance.MainHouseholdAsync(ct) is not { } main || main.Id == hid)
            return null;
        var shared = await db.Set<AiSettings>().AsNoTracking().SingleOrDefaultAsync(s => s.HouseholdId == main.Id, ct);
        return shared is null ? null : (shared, main.AdminName);
    }

    /// <summary>Asks for JSON and deserialises it. Retries once if the reply is blank or not JSON.</summary>
    public async Task<T> JsonAsync<T>(AiPrompt prompt, CancellationToken ct = default)
    {
        var settings = (await GetEffectiveAsync(ct))?.Settings ?? throw new BadRequestException("AI isn't set up yet. Add a provider in Settings → AI.");
        return await JsonAsync<T>(settings, prompt, ct);
    }

    public async Task<T> JsonAsync<T>(AiSettings settings, AiPrompt prompt, CancellationToken ct = default)
    {
        var provider = providers.Single(p => p.Kind == settings.Provider);
        var model = prompt.HasImages && !string.IsNullOrWhiteSpace(settings.VisionModel) ? settings.VisionModel! : settings.Model;
        string? lastReply = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            lastReply = await provider.CompleteAsync(settings, model, prompt, json: true, ct);
            if (ExtractJson(lastReply) is { } text)
            {
                try
                {
                    if (JsonSerializer.Deserialize<T>(text, Json) is { } value) return value;
                }
                catch (JsonException ex)
                {
                    // Keep the whole reply: the message shown to the user is only its first 160 characters.
                    logger.LogWarning("AI reply ({Model}, attempt {Attempt}) didn't fit {Type}: {Error}{NewLine}{Reply}",
                        model, attempt + 1, typeof(T).Name, ex.Message, Environment.NewLine, lastReply);
                    continue;
                }
            }
            logger.LogWarning("AI reply ({Model}, attempt {Attempt}) had no JSON object:{NewLine}{Reply}",
                model, attempt + 1, Environment.NewLine, lastReply);
        }
        throw new AiException($"The AI's reply wasn't usable JSON: {Shorten(lastReply)}");
    }

    /// <summary>Pulls the JSON object out of a reply that may be wrapped in prose or ``` fences.</summary>
    public static string? ExtractJson(string? reply)
    {
        if (string.IsNullOrWhiteSpace(reply)) return null;
        var start = reply.IndexOf('{');
        var end = reply.LastIndexOf('}');
        return start >= 0 && end > start ? reply[start..(end + 1)] : null;
    }

    internal static string Shorten(string? s, int n = 160) =>
        string.IsNullOrWhiteSpace(s) ? "(empty)" : s.Length <= n ? s.Trim() : s[..n].Trim() + "…";
}
