using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace HomeStack.Core.Notifications;

public record NotificationSettingsDto(bool Enabled, string ServerUrl, string Topic, bool HasAccessToken, int DigestHour,
    DateTime? LastSentAt, string? LastError, bool Saved);

/// <param name="AccessToken">null keeps the saved token, "" clears it.</param>
public record NotificationSettingsRequest(
    bool Enabled,
    [Required, Url, StringLength(200)] string ServerUrl,
    // Empty: no ntfy, just notifications on members' devices.
    [RegularExpression("^[A-Za-z0-9_-]{0,64}$", ErrorMessage = "Letters, numbers, - and _ only (up to 64).")] string? Topic,
    [StringLength(200)] string? AccessToken,
    [Range(0, 23)] int DigestHour) : IValidatable
{
    public IEnumerable<ValidationResult> Validate()
    {
        if (!Uri.TryCreate(ServerUrl, UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https"))
            yield return new ValidationResult("Must be an http(s) URL.", [nameof(ServerUrl)]);
    }
}

public static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/notifications").WithTags("Notifications").RequireAuthorization();

        g.MapGet("/settings", async (DigestService digest, CancellationToken ct) =>
            await digest.GetSettingsAsync(ct) is { } s ? ToDto(s, saved: true)
                // Not set up yet: suggest a hard-to-guess topic, since anyone who knows a public topic can read it.
                : new NotificationSettingsDto(false, "https://ntfy.sh", "homestack-" + RandomSuffix(), false, 8, null, null, false));

        g.MapPut("/settings", async (NotificationSettingsRequest req, DigestService digest, HomeStackDbContext db,
            ICurrentUser me, HttpRequest http, CancellationToken ct) =>
        {
            var s = await digest.GetSettingsAsync(ct);
            if (s is null) db.Add(s = new NotificationSettings { HouseholdId = me.HouseholdId });
            s.Enabled = req.Enabled;
            s.ServerUrl = req.ServerUrl.Trim().TrimEnd('/');
            s.Topic = req.Topic?.Trim() ?? "";
            if (req.AccessToken is not null) s.AccessToken = req.AccessToken.Trim() is "" ? null : req.AccessToken.Trim();
            s.DigestHour = req.DigestHour;
            s.AppUrl = $"{http.Scheme}://{http.Host}";
            await db.SaveChangesAsync(ct);
            return ToDto(s, saved: true);
        }).Validate<NotificationSettingsRequest>();

        g.MapGet("/preview", (DigestService digest, CancellationToken ct) => digest.BuildAsync(ct));
        g.MapPost("/test", (DigestService digest, CancellationToken ct) => digest.SendTestAsync(ct));

        // Notifications on this device (Web Push).
        g.MapGet("/push", (PushService push, CancellationToken ct) => push.StatusAsync(ct));
        g.MapPost("/push/subscriptions", (PushSubscribeRequest req, PushService push, CancellationToken ct) => push.SubscribeAsync(req, ct))
            .Validate<PushSubscribeRequest>();
        g.MapPost("/push/unsubscribe", async (PushUnsubscribeRequest req, PushService push, CancellationToken ct) =>
        {
            await push.UnsubscribeAsync(req.Endpoint, null, ct);
            return Results.NoContent();
        }).Validate<PushUnsubscribeRequest>();
        g.MapDelete("/push/subscriptions/{id:int}", async (int id, PushService push, CancellationToken ct) =>
        {
            await push.UnsubscribeAsync(null, id, ct);
            return Results.NoContent();
        });
    }

    private static NotificationSettingsDto ToDto(NotificationSettings s, bool saved) =>
        new(s.Enabled, s.ServerUrl, s.Topic, s.AccessToken is not null, s.DigestHour, s.LastSentAt, s.LastError, saved);

    private static string RandomSuffix()
    {
        const string alphabet = "abcdefghijkmnpqrstuvwxyz23456789";
        return string.Create(12, 0, (span, _) =>
        {
            for (var i = 0; i < span.Length; i++) span[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        });
    }
}
