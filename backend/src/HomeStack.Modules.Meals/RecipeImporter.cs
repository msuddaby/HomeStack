using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml;
using HomeStack.Core;
using Microsoft.Extensions.Logging;

namespace HomeStack.Modules.Meals;

/// <summary>Imports a recipe from a web page's schema.org Recipe JSON-LD (what most recipe sites publish).</summary>
public partial class RecipeImporter(HttpClient http, ILogger<RecipeImporter> log)
{
    private const int MaxBytes = 4 * 1024 * 1024;

    public async Task<RecipeRequest> ImportAsync(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new BadRequestException("Enter a full http(s) link to a recipe page.");
        await EnsurePublicHostAsync(uri, ct);

        string html;
        try
        {
            using var res = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!res.IsSuccessStatusCode) throw new BadRequestException($"That page returned {(int)res.StatusCode}.");
            if (res.Content.Headers.ContentLength > MaxBytes) throw new BadRequestException("That page is too large.");
            await using var stream = await res.Content.ReadAsStreamAsync(ct);
            using var limited = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buffer, ct)) > 0)
            {
                limited.Write(buffer, 0, read);
                if (limited.Length > MaxBytes) throw new BadRequestException("That page is too large.");
            }
            html = System.Text.Encoding.UTF8.GetString(limited.ToArray());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            log.LogWarning(ex, "Recipe import failed for {Url}", uri);
            throw new BadRequestException("Couldn't load that page.");
        }

        return Parse(html, uri.ToString()) ?? throw new BadRequestException("Couldn't find a recipe on that page. You can still add it by hand.");
    }

    /// <summary>Server-side request hygiene: only fetch public internet hosts.</summary>
    private static async Task EnsurePublicHostAsync(Uri uri, CancellationToken ct)
    {
        IPAddress[] addresses;
        try { addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, ct); }
        catch (SocketException) { throw new BadRequestException("Couldn't find that website."); }
        if (addresses.Length == 0 || addresses.Any(IsPrivate))
            throw new BadRequestException("Only public websites can be imported.");
    }

    internal static bool IsPrivate(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip) || ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6UniqueLocal) return true;
        if (ip.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = ip.GetAddressBytes();
        return b[0] is 10 or 127 or 0 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254) || (b[0] == 100 && b[1] is >= 64 and <= 127);
    }

    [GeneratedRegex(@"<script[^>]*type\s*=\s*[""']application/ld\+json[""'][^>]*>(?<json>.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex JsonLdScript();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tags();

    /// <summary>Extracts the first schema.org Recipe from the page's JSON-LD blocks.</summary>
    public static RecipeRequest? Parse(string html, string? sourceUrl)
    {
        foreach (Match m in JsonLdScript().Matches(html))
        {
            JsonNode? root;
            try { root = JsonNode.Parse(m.Groups["json"].Value, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }); }
            catch (JsonException) { continue; }
            if (FindRecipe(root) is JsonObject r) return ToRequest(r, sourceUrl);
        }
        return null;
    }

    private static JsonObject? FindRecipe(JsonNode? node)
    {
        switch (node)
        {
            case JsonArray arr:
                foreach (var n in arr) if (FindRecipe(n) is { } found) return found;
                return null;
            case JsonObject obj:
                if (IsType(obj["@type"], "Recipe")) return obj;
                if (obj["@graph"] is { } graph && FindRecipe(graph) is { } g) return g;
                if (obj["mainEntity"] is { } main && FindRecipe(main) is { } me) return me;
                return null;
            default:
                return null;
        }
    }

    private static bool IsType(JsonNode? type, string name) => type switch
    {
        JsonValue v => string.Equals(v.ToString(), name, StringComparison.OrdinalIgnoreCase),
        JsonArray a => a.Any(t => IsType(t, name)),
        _ => false,
    };

    private static RecipeRequest ToRequest(JsonObject r, string? sourceUrl)
    {
        var steps = new List<string>();
        CollectSteps(r["recipeInstructions"], steps);
        var ingredients = Strings(r["recipeIngredient"] ?? r["ingredients"]).Select(s => Clean(s)).Where(s => s.Length > 0).ToList();
        var tags = Strings(r["recipeCategory"]).Concat(Strings(r["recipeCuisine"])).Concat(Strings(r["keywords"]))
            .SelectMany(t => t.Split(','))
            .Select(t => Clean(t).ToLowerInvariant()).Where(t => t.Length is > 0 and <= 30).Distinct().Take(8).ToList();

        return new RecipeRequest(
            Title: Truncate(Clean(Text(r["name"]) ?? "Imported recipe"), 150),
            Description: Text(r["description"]) is { } d ? Truncate(Clean(d), 1000) : null,
            Servings: Servings(r["recipeYield"]),
            PrepMinutes: Minutes(r["prepTime"]),
            CookMinutes: Minutes(r["cookTime"]) ?? (Minutes(r["totalTime"]) is int t && Minutes(r["prepTime"]) is int p && t > p ? t - p : Minutes(r["totalTime"])),
            SourceUrl: sourceUrl,
            ImageUrl: Image(r["image"]),
            Ingredients: ingredients,
            Steps: steps,
            Tags: tags,
            Notes: null);
    }

    private static void CollectSteps(JsonNode? node, List<string> steps)
    {
        switch (node)
        {
            case JsonValue v:
                // Some sites put all steps in one string separated by newlines.
                foreach (var line in Clean(v.ToString(), keepNewlines: true).Split('\n'))
                    if (line.Trim().Length > 0) steps.Add(line.Trim());
                break;
            case JsonArray a:
                foreach (var n in a) CollectSteps(n, steps);
                break;
            case JsonObject o when IsType(o["@type"], "HowToSection"):
                if (Text(o["name"]) is { Length: > 0 } section) steps.Add($"## {Clean(section)}");
                CollectSteps(o["itemListElement"], steps);
                break;
            case JsonObject o:
                if ((Text(o["text"]) ?? Text(o["name"])) is { } text && Clean(text) is { Length: > 0 } t) steps.Add(t);
                else CollectSteps(o["itemListElement"], steps);
                break;
        }
    }

    private static IEnumerable<string> Strings(JsonNode? node) => node switch
    {
        JsonValue v => [v.ToString()],
        JsonArray a => a.SelectMany(Strings),
        JsonObject o when Text(o["name"]) is { } n => [n],
        _ => [],
    };

    private static string? Text(JsonNode? node) => node switch
    {
        JsonValue v => v.ToString(),
        JsonArray a => a.Select(Text).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)),
        _ => null,
    };

    private static string? Image(JsonNode? node) => node switch
    {
        JsonValue v => v.ToString(),
        JsonArray a => a.Select(Image).FirstOrDefault(i => i is not null),
        JsonObject o => Text(o["url"]) ?? Text(o["contentUrl"]),
        _ => null,
    } is { } url && Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme is "http" or "https" ? Truncate(url, 500) : null;

    private static int Servings(JsonNode? node)
    {
        foreach (var s in Strings(node))
            if (Regex.Match(s, @"\d+") is { Success: true } m && int.TryParse(m.Value, out var n) && n is > 0 and <= 100) return n;
        return 4;
    }

    private static int? Minutes(JsonNode? node)
    {
        var s = Text(node);
        if (string.IsNullOrWhiteSpace(s)) return null;
        try { var ts = XmlConvert.ToTimeSpan(s.Trim()); return ts.TotalMinutes is > 0 and < 10000 ? (int)Math.Round(ts.TotalMinutes) : null; }
        catch (FormatException) { return null; }
    }

    private static string Clean(string s, bool keepNewlines = false)
    {
        s = WebUtility.HtmlDecode(Tags().Replace(s, keepNewlines ? "\n" : " "));
        s = keepNewlines ? Regex.Replace(s, @"[ \t\r\f\v]+", " ") : Regex.Replace(s, @"\s+", " ");
        return s.Trim();
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
