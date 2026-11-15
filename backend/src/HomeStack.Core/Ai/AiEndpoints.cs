using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace HomeStack.Core.Ai;

public record AiSettingsDto(bool Configured, AiProviderKind Provider, string BaseUrl, bool HasApiKey, string Model, string? VisionModel);

/// <param name="ApiKey">null keeps the saved key, "" clears it.</param>
public record AiSettingsRequest(
    AiProviderKind Provider,
    [Required, StringLength(300)] string BaseUrl,
    [StringLength(300)] string? ApiKey,
    [Required, StringLength(120)] string Model,
    [StringLength(120)] string? VisionModel) : IValidatable
{
    public IEnumerable<ValidationResult> Validate()
    {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https"))
            yield return new ValidationResult("Must be an http(s) URL.", [nameof(BaseUrl)]);
    }
}

public record AiTestResult(bool Text, bool Vision, string Model, string? VisionModel, string? VisionError);

public static class AiEndpoints
{
    // A 32×32 red square: asking for its colour proves the model can see images.
    private static readonly byte[] RedSquare = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAIAAAD8GO2jAAAAKklEQVR42mO4o6ZGU8QwasGoBaMWjFowasGoBaMWjFowasGoBaMWDBULAIjyoD2JhwFtAAAAAElFTkSuQmCC");

    private record Pong(bool Ok);
    private record ColorReply(string? Color);

    public static void MapAiEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/ai").WithTags("AI").RequireAuthorization();

        g.MapGet("/settings", async (AiService ai, CancellationToken ct) =>
            await ai.GetSettingsAsync(ct) is { } s ? ToDto(s) : ToDto(new AiSettings(), configured: false));

        g.MapPut("/settings", async (AiSettingsRequest req, AiService ai, HomeStackDbContext db, ICurrentUser me, CancellationToken ct) =>
        {
            var s = await ai.GetSettingsAsync(ct);
            if (s is null) db.Add(s = new AiSettings { HouseholdId = me.HouseholdId });
            Apply(s, req);
            await db.SaveChangesAsync(ct);
            return ToDto(s);
        }).Validate<AiSettingsRequest>();

        g.MapDelete("/settings", async (AiService ai, HomeStackDbContext db, CancellationToken ct) =>
        {
            if (await ai.GetSettingsAsync(ct) is { } s) { db.Remove(s); await db.SaveChangesAsync(ct); }
            return Results.NoContent();
        });

        // Tests the settings in the form (not yet saved). A missing key falls back to the saved one.
        g.MapPost("/test", async (AiSettingsRequest req, AiService ai, CancellationToken ct) =>
        {
            var saved = await ai.GetSettingsAsync(ct);
            var s = new AiSettings { ApiKey = saved?.ApiKey };
            Apply(s, req);

            await ai.JsonAsync<Pong>(s, new AiPrompt("You are a connectivity check.", """Reply with this JSON exactly: {"ok": true}""", MaxTokens: 50), ct);

            string? visionError = null;
            var vision = false;
            try
            {
                var reply = await ai.JsonAsync<ColorReply>(s, new AiPrompt("You describe images.",
                    """What single colour fills this image? Reply as JSON: {"color": "<colour name>"}""",
                    [new AiImage(RedSquare, "image/png")], MaxTokens: 50), ct);
                vision = reply.Color?.Contains("red", StringComparison.OrdinalIgnoreCase) == true;
                if (!vision) visionError = $"The model answered \"{reply.Color}\" for a red image, so it probably can't see images.";
            }
            catch (AiException ex) { visionError = ex.Message; }

            return new AiTestResult(true, vision, s.Model, s.VisionModel, visionError);
        }).Validate<AiSettingsRequest>();
    }

    private static void Apply(AiSettings s, AiSettingsRequest req)
    {
        s.Provider = req.Provider;
        s.BaseUrl = req.BaseUrl.Trim().TrimEnd('/');
        if (req.ApiKey is not null) s.ApiKey = req.ApiKey.Trim() is "" ? null : req.ApiKey.Trim();
        s.Model = req.Model.Trim();
        s.VisionModel = string.IsNullOrWhiteSpace(req.VisionModel) ? null : req.VisionModel.Trim();
    }

    private static AiSettingsDto ToDto(AiSettings s, bool configured = true) =>
        new(configured, s.Provider, s.BaseUrl, s.ApiKey is not null, s.Model, s.VisionModel);
}
