using System.Buffers.Binary;
using System.Buffers.Text;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HomeStack.Core.Notifications;

// Native notifications for the installed app (Web Push): each device a member turns notifications on for is a
// subscription; the server signs with its own VAPID key and encrypts each message for that device. No third party
// beyond the browser's own push service (Apple, Google, Mozilla).

/// <summary>One device that gets notifications for one member.</summary>
public class PushSubscription
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public int UserId { get; set; }
    /// <summary>The push service URL for this device (a secret: anyone with it and the keys can notify the device).</summary>
    public required string Endpoint { get; set; }
    public required string P256dh { get; set; }
    public required string Auth { get; set; }
    /// <summary>"iPhone", "Android · Chrome", … to tell devices apart in Settings.</summary>
    public string? Device { get; set; }
    /// <summary>Where the app was opened from when subscribing (https://…); also the VAPID contact.</summary>
    public string? AppUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastSentAt { get; set; }
    public string? LastError { get; set; }

    internal static void Configure(ModelBuilder mb) => mb.Entity<PushSubscription>(e =>
    {
        e.ToTable("push_subscriptions");
        e.HasIndex(x => x.Endpoint).IsUnique();
        e.HasIndex(x => x.HouseholdId);
        e.Property(x => x.Endpoint).HasMaxLength(1000);
        e.Property(x => x.P256dh).HasMaxLength(200);
        e.Property(x => x.Auth).HasMaxLength(100);
        e.Property(x => x.Device).HasMaxLength(100);
        e.Property(x => x.AppUrl).HasMaxLength(200);
        e.Property(x => x.LastError).HasMaxLength(500);
        e.HasOne<Data.Household>().WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne<Data.User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    });
}

/// <summary>This server's VAPID key pair (one row, made on first use). Changing it breaks every subscription.</summary>
public class VapidKeys
{
    public int Id { get; set; } = 1;
    /// <summary>Uncompressed P-256 point, base64url: what browsers subscribe with.</summary>
    public required string PublicKey { get; set; }
    public required string PrivateKey { get; set; }

    internal static void Configure(ModelBuilder mb) => mb.Entity<VapidKeys>(e =>
    {
        e.ToTable("vapid_keys");
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.PublicKey).HasMaxLength(100);
        e.Property(x => x.PrivateKey).HasMaxLength(100);
    });
}

public record PushMessage(string Title, string Body, string? Url = null, string? Tag = null, bool Urgent = false);
public record PushResult(int Sent, int Failed, string? Error);

public record PushDeviceDto(int Id, string? Device, string Endpoint, DateTime CreatedAt, DateTime? LastSentAt, string? LastError);
public record PushStatusDto(string PublicKey, List<PushDeviceDto> Devices);

public record PushSubscribeRequest(
    [Required, StringLength(1000)] string Endpoint,
    [Required, StringLength(200)] string P256dh,
    [Required, StringLength(100)] string Auth,
    [StringLength(100)] string? Device,
    [StringLength(200)] string? AppUrl) : IValidatable
{
    // The server posts to this URL, so only the browsers' own push services are allowed.
    private static readonly string[] PushHosts = ["fcm.googleapis.com", "android.googleapis.com", "push.services.mozilla.com", "push.apple.com", "notify.windows.com"];

    public IEnumerable<ValidationResult> Validate()
    {
        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var u) || u.Scheme != "https"
            || !PushHosts.Any(h => u.Host == h || u.Host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase)))
            yield return new("Not a browser push service.", [nameof(Endpoint)]);
        if (WebPushCrypto.TryDecode(P256dh) is not { Length: 65 } key || key[0] != 0x04)
            yield return new("Not a P-256 public key.", [nameof(P256dh)]);
        if (WebPushCrypto.TryDecode(Auth) is not { Length: 16 })
            yield return new("Auth secret must be 16 bytes.", [nameof(Auth)]);
        if (AppUrl is not null && !(Uri.TryCreate(AppUrl, UriKind.Absolute, out var a) && a.Scheme is "http" or "https"))
            yield return new("Must be an http(s) URL.", [nameof(AppUrl)]);
    }
}

public record PushUnsubscribeRequest([Required, StringLength(1000)] string Endpoint);

/// <summary>Message encryption (RFC 8291, aes128gcm) and VAPID signing (RFC 8292).</summary>
public static class WebPushCrypto
{
    private const int RecordSize = 4096;

    public static string Encode(ReadOnlySpan<byte> bytes) => Base64Url.EncodeToString(bytes);

    public static byte[]? TryDecode(string? s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        try { return Base64Url.DecodeFromChars(s.TrimEnd('=')); } catch (FormatException) { return null; }
    }

    public static VapidKeys NewKeys()
    {
        using var k = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = k.ExportParameters(true);
        return new VapidKeys { PublicKey = Encode(Point(p)), PrivateKey = Encode(p.D) };
    }

    private static byte[] Point(ECParameters p) => [0x04, .. p.Q.X!, .. p.Q.Y!];

    private static ECParameters Public(byte[] point) => new()
    {
        Curve = ECCurve.NamedCurves.nistP256,
        Q = new ECPoint { X = point[1..33], Y = point[33..65] },
    };

    /// <summary>Encrypts <paramref name="plaintext"/> for one device: a single aes128gcm record with a fresh key and salt.</summary>
    public static byte[] Encrypt(byte[] uaPublic, byte[] authSecret, byte[] plaintext)
    {
        if (plaintext.Length + 17 > RecordSize) throw new ArgumentException("Push message too long.", nameof(plaintext));
        using var asKey = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var asPublic = Point(asKey.ExportParameters(false));
        using var ua = ECDiffieHellman.Create(Public(uaPublic));
        var shared = asKey.DeriveRawSecretAgreement(ua.PublicKey);

        // IKM = HKDF(salt = auth secret, ikm = ECDH secret, info = "WebPush: info\0" || ua_public || as_public)
        byte[] keyInfo = [.. Encoding.ASCII.GetBytes("WebPush: info\0"), .. uaPublic, .. asPublic];
        var ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, shared, 32, authSecret, keyInfo);
        var salt = RandomNumberGenerator.GetBytes(16);
        var cek = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 16, salt, Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0"));
        var nonce = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 12, salt, Encoding.ASCII.GetBytes("Content-Encoding: nonce\0"));

        byte[] record = [.. plaintext, 0x02]; // 0x02: the last (only) record, no padding
        var body = new byte[16 + 4 + 1 + asPublic.Length + record.Length + 16];
        salt.CopyTo(body, 0);
        BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(16), RecordSize);
        body[20] = (byte)asPublic.Length;
        asPublic.CopyTo(body, 21);
        var header = 21 + asPublic.Length;
        using var aes = new AesGcm(cek, 16);
        aes.Encrypt(nonce, record, body.AsSpan(header, record.Length), body.AsSpan(header + record.Length, 16));
        return body;
    }

    /// <summary>The Authorization header value: a short-lived ES256 JWT for the push service, plus our public key.</summary>
    public static string Vapid(string endpoint, VapidKeys keys, string subject, DateTimeOffset now)
    {
        var aud = new Uri(endpoint).GetLeftPart(UriPartial.Authority);
        var header = Encode("""{"typ":"JWT","alg":"ES256"}"""u8);
        var claims = Encode(JsonSerializer.SerializeToUtf8Bytes(new { aud, exp = now.AddHours(12).ToUnixTimeSeconds(), sub = subject }));
        var p = Public(TryDecode(keys.PublicKey)!);
        p.D = TryDecode(keys.PrivateKey);
        using var ecdsa = ECDsa.Create(p);
        // .NET signs in IEEE P1363 form (r || s), which is what JWS wants.
        var sig = ecdsa.SignData(Encoding.ASCII.GetBytes($"{header}.{claims}"), HashAlgorithmName.SHA256);
        return $"vapid t={header}.{claims}.{Encode(sig)}, k={keys.PublicKey}";
    }
}

/// <summary>Posts encrypted messages to browser push services.</summary>
public class WebPushClient(HttpClient http)
{
    public async Task<(HttpStatusCode Status, string Detail)> SendAsync(string endpoint, string authorization, byte[] body,
        bool urgent, string? topic, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new ByteArrayContent(body) };
        req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        req.Content.Headers.ContentEncoding.Add("aes128gcm");
        req.Headers.TryAddWithoutValidation("Authorization", authorization);
        req.Headers.Add("TTL", "43200"); // a reminder that can't be delivered within 12 hours isn't worth showing
        req.Headers.Add("Urgency", urgent ? "high" : "normal");
        // A newer message with the same topic replaces one still waiting to be delivered.
        if (topic is not null) req.Headers.Add("Topic", topic);
        using var res = await http.SendAsync(req, ct);
        var detail = res.IsSuccessStatusCode ? "" : await res.Content.ReadAsStringAsync(ct);
        return (res.StatusCode, detail.Length > 200 ? detail[..200] : detail.Trim());
    }
}

/// <summary>Members' devices and sending to them.</summary>
public class PushService(HomeStackDbContext db, ICurrentUser me, WebPushClient client, IConfiguration config, TimeProvider time)
{
    private const int MaxBody = 1500;
    private IQueryable<PushSubscription> Subs => db.Set<PushSubscription>().Where(s => s.HouseholdId == me.HouseholdId);

    public async Task<VapidKeys> KeysAsync(CancellationToken ct = default)
    {
        if (await db.Set<VapidKeys>().AsNoTracking().SingleOrDefaultAsync(ct) is { } keys) return keys;
        var made = WebPushCrypto.NewKeys();
        db.Add(made);
        try { await db.SaveChangesAsync(ct); return made; }
        catch (DbUpdateException)
        {
            // Someone else made them at the same moment: use theirs.
            db.Entry(made).State = EntityState.Detached;
            return await db.Set<VapidKeys>().AsNoTracking().SingleAsync(ct);
        }
    }

    public async Task<PushStatusDto> StatusAsync(CancellationToken ct = default)
    {
        var uid = me.UserId;
        var devices = await Subs.Where(s => s.UserId == uid).OrderBy(s => s.CreatedAt).ToListAsync(ct);
        return new PushStatusDto((await KeysAsync(ct)).PublicKey, devices.Select(ToDto).ToList());
    }

    /// <summary>
    /// Turns notifications on for the calling device (again, if it already was: the browser may have new keys).
    /// The first device in a household switches the daily reminder on.
    /// </summary>
    public async Task<PushDeviceDto> SubscribeAsync(PushSubscribeRequest req, CancellationToken ct = default)
    {
        // A shared device that's changed hands follows whoever is signed in now.
        var sub = await db.Set<PushSubscription>().SingleOrDefaultAsync(s => s.Endpoint == req.Endpoint, ct);
        if (sub is null) db.Add(sub = new PushSubscription { Endpoint = req.Endpoint, P256dh = "", Auth = "", CreatedAt = time.GetUtcNow().UtcDateTime });
        sub.HouseholdId = me.HouseholdId;
        sub.UserId = me.UserId;
        sub.P256dh = req.P256dh.TrimEnd('=');
        sub.Auth = req.Auth.TrimEnd('=');
        sub.Device = req.Device?.Trim() is { Length: > 0 } d ? d : sub.Device;
        sub.AppUrl = req.AppUrl?.TrimEnd('/') ?? sub.AppUrl;

        var hid = me.HouseholdId;
        if (!await db.Set<NotificationSettings>().AnyAsync(s => s.HouseholdId == hid, ct))
            db.Add(new NotificationSettings { HouseholdId = hid, Enabled = true, Topic = "", AppUrl = sub.AppUrl });
        await db.SaveChangesAsync(ct);
        return ToDto(sub);
    }

    /// <summary>The calling device turned notifications off (or a member removed one of their devices).</summary>
    public async Task UnsubscribeAsync(string? endpoint, int? id, CancellationToken ct = default)
    {
        var uid = me.UserId;
        var sub = await Subs.SingleOrDefaultAsync(s => s.UserId == uid && (s.Endpoint == endpoint || s.Id == id), ct);
        if (sub is null) return;
        db.Remove(sub);
        await db.SaveChangesAsync(ct);
    }

    public async Task<PushResult> SendToUsersAsync(IReadOnlyCollection<int> userIds, PushMessage m, CancellationToken ct = default) =>
        await SendAsync(await Subs.Where(s => userIds.Contains(s.UserId)).ToListAsync(ct), m, ct);

    public async Task<PushResult> SendToHouseholdAsync(PushMessage m, CancellationToken ct = default) =>
        await SendAsync(await Subs.ToListAsync(ct), m, ct);

    public Task<bool> AnyAsync(CancellationToken ct = default) => Subs.AnyAsync(ct);

    private async Task<PushResult> SendAsync(List<PushSubscription> subs, PushMessage m, CancellationToken ct)
    {
        if (subs.Count == 0) return new PushResult(0, 0, null);
        var keys = await KeysAsync(ct);
        var body = m.Body.Length > MaxBody ? m.Body[..MaxBody] + "…" : m.Body;
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { title = m.Title, body, url = m.Url ?? "/", tag = m.Tag, urgent = m.Urgent });
        int sent = 0, failed = 0;
        string? error = null;
        foreach (var s in subs)
        {
            string? problem;
            try
            {
                var auth = WebPushCrypto.Vapid(s.Endpoint, keys, Subject(s), time.GetUtcNow());
                var encrypted = WebPushCrypto.Encrypt(WebPushCrypto.TryDecode(s.P256dh)!, WebPushCrypto.TryDecode(s.Auth)!, payload);
                var (status, detail) = await client.SendAsync(s.Endpoint, auth, encrypted, m.Urgent, m.Tag, ct);
                if (status is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                {
                    // The device unsubscribed or the app was removed: forget it.
                    db.Remove(s);
                    continue;
                }
                problem = (int)status is >= 200 and < 300 ? null : $"Push service answered {(int)status}{(detail.Length > 0 ? $": {detail}" : "")}";
            }
            catch (Exception ex) when (ex is HttpRequestException or CryptographicException || ex is TaskCanceledException && !ct.IsCancellationRequested)
            {
                problem = ex is TaskCanceledException ? "Timed out reaching the push service." : ex.Message;
            }
            s.LastError = problem;
            if (problem is null) { s.LastSentAt = time.GetUtcNow().UtcDateTime; sent++; }
            else { failed++; error ??= problem; }
        }
        await db.SaveChangesAsync(ct);
        return new PushResult(sent, failed, error);
    }

    /// <summary>The VAPID contact: configured, or the https address the app was opened from (Apple rejects anything else).</summary>
    private string Subject(PushSubscription s) =>
        config["Notifications:VapidSubject"] is { Length: > 0 } configured ? configured
        : s.AppUrl is { } url && url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? url
        : "mailto:homestack@example.com";

    private static PushDeviceDto ToDto(PushSubscription s) => new(s.Id, s.Device, s.Endpoint, s.CreatedAt, s.LastSentAt, s.LastError);
}
