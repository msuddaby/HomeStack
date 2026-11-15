using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HomeStack.Core.Ai;

/// <summary>Shared HTTP plumbing: posts JSON, turns transport and HTTP errors into readable <see cref="AiException"/>s.</summary>
public abstract class AiProviderBase(HttpClient http)
{
    protected async Task<JsonNode> PostAsync(string url, JsonObject body, Action<HttpRequestHeaders> headers, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        headers(req.Headers);
        HttpResponseMessage res;
        try { res = await http.SendAsync(req, ct); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new AiException("The AI service took too long to answer."); }
        catch (HttpRequestException ex) { throw new AiException($"Couldn't reach {new Uri(url).Host}: {ex.Message}"); }

        using (res)
        {
            var text = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
                throw new AiException(res.StatusCode switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => $"The AI service rejected the API key ({(int)res.StatusCode}).",
                    HttpStatusCode.NotFound => $"Not found (404). Check the base URL and model name. {ErrorDetail(text)}",
                    HttpStatusCode.TooManyRequests => $"Rate limited or out of credit (429). {ErrorDetail(text)}",
                    _ => $"The AI service answered {(int)res.StatusCode}. {ErrorDetail(text)}",
                });
            try { return JsonNode.Parse(text) ?? throw new AiException("Empty response from the AI service."); }
            catch (JsonException) { throw new AiException($"Unexpected response from the AI service: {AiService.Shorten(text)}"); }
        }
    }

    /// <summary>Most services put the reason at error.message (or error as a string).</summary>
    private static string ErrorDetail(string body)
    {
        try
        {
            var n = JsonNode.Parse(body);
            var msg = n?["error"] is JsonObject o ? o["message"]?.ToString() : n?["error"]?.ToString() ?? n?["message"]?.ToString();
            return AiService.Shorten(msg ?? body, 240);
        }
        catch (JsonException) { return AiService.Shorten(body, 240); }
    }

    protected static string Url(AiSettings s, string path) => s.BaseUrl.TrimEnd('/') + path;
}

/// <summary>
/// OpenAI chat-completions format: DeepSeek (https://api.deepseek.com), OpenAI (https://api.openai.com/v1),
/// Ollama (http://host:11434/v1), OpenRouter, LM Studio, Groq, vLLM and so on.
/// </summary>
public class OpenAiCompatibleProvider(HttpClient http) : AiProviderBase(http), IAiProvider
{
    public AiProviderKind Kind => AiProviderKind.OpenAiCompatible;

    public async Task<string> CompleteAsync(AiSettings s, string model, AiPrompt p, bool json, CancellationToken ct)
    {
        JsonNode userContent = p.HasImages
            ? new JsonArray([
                .. p.Images!.Select(i => (JsonNode)new JsonObject
                {
                    ["type"] = "image_url",
                    ["image_url"] = new JsonObject { ["url"] = $"data:{i.MediaType};base64,{Convert.ToBase64String(i.Data)}" },
                }),
                new JsonObject { ["type"] = "text", ["text"] = p.User },
            ])
            : JsonValue.Create(p.User);

        var body = new JsonObject
        {
            ["model"] = model,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = p.System },
                new JsonObject { ["role"] = "user", ["content"] = userContent }),
            ["max_tokens"] = p.MaxTokens,
            ["temperature"] = 0.1,
        };
        if (json) body["response_format"] = new JsonObject { ["type"] = "json_object" };

        var res = await PostAsync(Url(s, "/chat/completions"), body, h =>
        {
            if (!string.IsNullOrEmpty(s.ApiKey)) h.Authorization = new AuthenticationHeaderValue("Bearer", s.ApiKey);
        }, ct);
        return res["choices"]?[0]?["message"]?["content"]?.ToString() ?? "";
    }
}

/// <summary>Anthropic Messages API (Claude), https://api.anthropic.com.</summary>
public class AnthropicProvider(HttpClient http) : AiProviderBase(http), IAiProvider
{
    public AiProviderKind Kind => AiProviderKind.Anthropic;

    public async Task<string> CompleteAsync(AiSettings s, string model, AiPrompt p, bool json, CancellationToken ct)
    {
        var content = new JsonArray();
        foreach (var i in p.Images ?? [])
            content.Add(new JsonObject
            {
                ["type"] = "image",
                ["source"] = new JsonObject { ["type"] = "base64", ["media_type"] = i.MediaType, ["data"] = Convert.ToBase64String(i.Data) },
            });
        content.Add(new JsonObject { ["type"] = "text", ["text"] = p.User + (json ? "\n\nReply with only the JSON object." : "") });

        var body = new JsonObject
        {
            ["model"] = model,
            // Current Claude models always think, and thinking counts toward max_tokens: leave room.
            ["max_tokens"] = Math.Max(p.MaxTokens, 16000),
            ["system"] = p.System,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = content }),
        };

        var res = await PostAsync(Url(s, "/v1/messages"), body, h =>
        {
            if (!string.IsNullOrEmpty(s.ApiKey)) h.Add("x-api-key", s.ApiKey);
            h.Add("anthropic-version", "2023-06-01");
        }, ct);
        if (res["stop_reason"]?.ToString() == "refusal") throw new AiException("Claude declined this request.");
        return string.Concat((res["content"] as JsonArray ?? [])
            .Where(b => b?["type"]?.ToString() == "text").Select(b => b!["text"]?.ToString()));
    }
}
