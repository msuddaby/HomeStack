using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HomeStack.Core;
using HomeStack.Core.Auth;
using HomeStack.Core.Notifications;
using HomeStack.Modules.Inventory;
using Microsoft.Extensions.DependencyInjection;

namespace HomeStack.Tests;

public class PushTests
{
    /// <summary>A browser's side of a subscription: its key pair and auth secret.</summary>
    internal sealed class Device : IDisposable
    {
        public ECDiffieHellman Key { get; } = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        public byte[] Auth { get; } = RandomNumberGenerator.GetBytes(16);
        public string Endpoint { get; } = $"https://fcm.googleapis.com/fcm/send/{Guid.NewGuid():N}";
        public byte[] Public { get { var p = Key.ExportParameters(false); return [0x04, .. p.Q.X!, .. p.Q.Y!]; } }
        public object Subscription(string device = "Pixel · Chrome") => new
        {
            endpoint = Endpoint, p256dh = WebPushCrypto.Encode(Public), auth = WebPushCrypto.Encode(Auth), device, appUrl = "https://home.example.ts.net",
        };

        /// <summary>What the browser does with a push message (RFC 8291 / RFC 8188).</summary>
        public JsonElement Decrypt(byte[] body)
        {
            var salt = body[..16];
            Assert.Equal(4096u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(body.AsSpan(16)));
            var idLen = body[20];
            var asPublic = body[21..(21 + idLen)];
            var cipher = body[(21 + idLen)..^16];
            var tag = body[^16..];
            using var server = ECDiffieHellman.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = asPublic[1..33], Y = asPublic[33..65] },
            });
            var shared = Key.DeriveRawSecretAgreement(server.PublicKey);
            var ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, shared, 32, Auth, [.. "WebPush: info\0"u8, .. Public, .. asPublic]);
            var cek = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 16, salt, "Content-Encoding: aes128gcm\0"u8.ToArray());
            var nonce = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 12, salt, "Content-Encoding: nonce\0"u8.ToArray());
            var plain = new byte[cipher.Length];
            using (var aes = new AesGcm(cek, 16)) aes.Decrypt(nonce, cipher, tag, plain);
            Assert.Equal(0x02, plain[^1]); // last record, no padding
            return JsonDocument.Parse(plain.AsMemory(..^1)).RootElement;
        }

        public void Dispose() => Key.Dispose();
    }

    private static Task<HttpResponseMessage> Subscribe(HttpClient c, Device d) => c.Post("/api/notifications/push/subscriptions", d.Subscription());

    private static async Task<HttpClient> WithSomethingToSay(TestApp app)
    {
        var c = await app.SetupClientAsync();
        var fridge = (await (await c.GetAsync("/api/inventory/locations")).Read<List<LocationDto>>()).First(l => l.Kind == LocationKind.Fridge).Id;
        await c.Post("/api/inventory/items", new { product = new { name = "Chicken thighs", category = "meat" }, locationId = fridge, expiresAt = app.Today.AddDays(1) });
        return c;
    }

    [Fact]
    public async Task Devices_subscribe_only_to_real_push_services_and_switch_the_reminder_on()
    {
        using var app = new TestApp();
        var c = await app.SetupClientAsync();
        using var d = new Device();

        var bad = await c.Post("/api/notifications/push/subscriptions", new { endpoint = "http://192.168.1.1/admin", p256dh = "AAAA", auth = "AAAA" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var errors = (await bad.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("endpoint", out _));
        Assert.True(errors.TryGetProperty("p256dh", out _));
        Assert.True(errors.TryGetProperty("auth", out _));

        var status = await (await c.GetAsync("/api/notifications/push")).Read<PushStatusDto>();
        Assert.Equal(65, WebPushCrypto.TryDecode(status.PublicKey)!.Length);
        Assert.Empty(status.Devices);

        var device = await (await Subscribe(c, d)).Read<PushDeviceDto>();
        Assert.Equal("Pixel · Chrome", device.Device);
        (await Subscribe(c, d)).EnsureSuccessStatusCode(); // again (fresh keys after a reinstall): still one device
        status = await (await c.GetAsync("/api/notifications/push")).Read<PushStatusDto>();
        Assert.Equal(device.Id, Assert.Single(status.Devices).Id);
        Assert.Equal(status.PublicKey, (await (await c.GetAsync("/api/notifications/push")).Read<PushStatusDto>()).PublicKey); // the key is kept

        // The first device turns the daily reminder on, without ntfy.
        var settings = await (await c.GetAsync("/api/notifications/settings")).Read<NotificationSettingsDto>();
        Assert.True(settings.Saved);
        Assert.True(settings.Enabled);
        Assert.Equal("", settings.Topic);

        (await c.Post("/api/notifications/push/unsubscribe", new { endpoint = d.Endpoint })).EnsureSuccessStatusCode();
        Assert.Empty((await (await c.GetAsync("/api/notifications/push")).Read<PushStatusDto>()).Devices);
    }

    [Fact]
    public async Task Test_send_is_encrypted_for_the_device_and_signed_with_the_servers_key()
    {
        using var app = new TestApp();
        var c = await WithSomethingToSay(app);
        using var d = new Device();
        (await Subscribe(c, d)).EnsureSuccessStatusCode();
        var publicKey = (await (await c.GetAsync("/api/notifications/push")).Read<PushStatusDto>()).PublicKey;

        (await c.PostAsync("/api/notifications/test", null)).EnsureSuccessStatusCode();

        var (req, _) = Assert.Single(app.Push.Requests);
        Assert.Equal(d.Endpoint, req.RequestUri!.ToString());
        Assert.Equal("aes128gcm", Assert.Single(req.Content!.Headers.ContentEncoding));
        Assert.Equal("43200", req.Headers.GetValues("TTL").Single());
        Assert.Equal("digest", req.Headers.GetValues("Topic").Single());

        var message = d.Decrypt(app.Push.RawBodies.Single());
        Assert.Equal("1 to freeze", message.GetProperty("title").GetString());
        Assert.Contains("Chicken thighs", message.GetProperty("body").GetString());
        Assert.Equal("/", message.GetProperty("url").GetString());

        // Authorization: vapid t=<ES256 JWT for the push service's origin>, k=<our public key>
        var auth = req.Headers.GetValues("Authorization").Single();
        Assert.StartsWith("vapid t=", auth);
        var jwt = auth["vapid t=".Length..auth.IndexOf(',')];
        Assert.EndsWith($", k={publicKey}", auth);
        var parts = jwt.Split('.');
        var claims = JsonDocument.Parse(WebPushCrypto.TryDecode(parts[1])!).RootElement;
        Assert.Equal("https://fcm.googleapis.com", claims.GetProperty("aud").GetString());
        Assert.Equal("https://home.example.ts.net", claims.GetProperty("sub").GetString());
        Assert.InRange(claims.GetProperty("exp").GetInt64() - app.Time.Now.ToUnixTimeSeconds(), 1, 24 * 3600);
        var key = WebPushCrypto.TryDecode(publicKey)!;
        using var verifier = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = key[1..33], Y = key[33..65] } });
        Assert.True(verifier.VerifyData(Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"), WebPushCrypto.TryDecode(parts[2])!, HashAlgorithmName.SHA256));
    }

    [Fact]
    public async Task Test_send_needs_somewhere_to_go_and_forgets_devices_that_are_gone()
    {
        using var app = new TestApp();
        var c = await WithSomethingToSay(app);
        using var d = new Device();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync("/api/notifications/test", null)).StatusCode);

        (await Subscribe(c, d)).EnsureSuccessStatusCode();
        app.Push.Status = HttpStatusCode.Forbidden;
        var failed = await c.PostAsync("/api/notifications/test", null);
        Assert.Equal(HttpStatusCode.BadRequest, failed.StatusCode);
        Assert.Contains("403", await failed.Content.ReadAsStringAsync());
        Assert.Contains("403", Assert.Single((await (await c.GetAsync("/api/notifications/push")).Read<PushStatusDto>()).Devices).LastError);

        // Unsubscribed or the app was deleted: the push service says 410, and the device is dropped.
        app.Push.Status = HttpStatusCode.Gone;
        await c.PostAsync("/api/notifications/test", null);
        Assert.Empty((await (await c.GetAsync("/api/notifications/push")).Read<PushStatusDto>()).Devices);
    }

    [Fact]
    public async Task Daily_send_reaches_every_members_devices_once_even_when_ntfy_retries()
    {
        using var app = new TestApp();
        var c = await WithSomethingToSay(app);
        var me = await (await c.GetAsync("/api/auth/me")).Read<MeDto>();
        using var mine = new Device();
        using var theirs = new Device();
        (await Subscribe(c, mine)).EnsureSuccessStatusCode();
        (await c.Post("/api/household/users", new { name = "Sam", username = "sam", password = "password123" })).EnsureSuccessStatusCode();
        var sam = app.CreateClient();
        (await sam.Post("/api/auth/login", new { username = "sam", password = "password123" })).EnsureSuccessStatusCode();
        (await Subscribe(sam, theirs)).EnsureSuccessStatusCode();
        (await c.PutAsJsonAsync("/api/notifications/settings", new { enabled = true, serverUrl = "https://ntfy.example", topic = "homestack-abc", digestHour = 8 }, TestApp.Json))
            .EnsureSuccessStatusCode();

        app.Ntfy.Status = HttpStatusCode.BadGateway;
        async Task Daily()
        {
            await using var scope = app.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<HouseholdScope>().Enter(me.Household.Id);
            await scope.ServiceProvider.GetRequiredService<DigestService>().SendDailyAsync();
        }
        await Daily();
        await Daily(); // ntfy failed, so the worker tries again; the devices already have it
        Assert.Equal(2, app.Ntfy.Requests.Count);
        var sent = app.Push.Requests.Select(r => r.Request.RequestUri!.ToString()).ToList();
        Assert.Equal(new[] { mine.Endpoint, theirs.Endpoint }.Order(), sent.Order());
        Assert.Contains("Chicken thighs", theirs.Decrypt(app.Push.RawBodies[sent.IndexOf(theirs.Endpoint)]).GetProperty("body").GetString());
    }
}
