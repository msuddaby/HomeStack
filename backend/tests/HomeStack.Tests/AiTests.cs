using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using HomeStack.Core.Ai;
using HomeStack.Modules.Inventory;

namespace HomeStack.Tests;

public class AiTests
{
    /// <summary>An OpenAI-format chat completion whose message content is <paramref name="content"/>.</summary>
    private static string OpenAiReply(object content, string finish = "stop") => new JsonObject
    {
        ["choices"] = new JsonArray(new JsonObject
        {
            ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = content as string ?? JsonSerializer.Serialize(content) },
            ["finish_reason"] = finish,
        }),
    }.ToJsonString();

    private static string AnthropicReply(string text, string stop = "end_turn") => new JsonObject
    {
        ["content"] = new JsonArray(new JsonObject { ["type"] = "thinking", ["thinking"] = "" }, new JsonObject { ["type"] = "text", ["text"] = text }),
        ["stop_reason"] = stop,
    }.ToJsonString();

    private static Task<HttpResponseMessage> SaveAi(HttpClient c, object body) => c.PutAsJsonAsync("/api/ai/settings", body, TestApp.Json);

    private static object DeepSeek(string? key = "sk-test") => new { provider = "openAiCompatible", baseUrl = "https://api.deepseek.com", apiKey = key, model = "deepseek-flash" };

    [Fact]
    public async Task Settings_default_to_deepseek_and_hide_the_key()
    {
        using var app = new TestApp();
        var c = await app.SetupClientAsync();

        var initial = await (await c.GetAsync("/api/ai/settings")).Read<AiSettingsDto>();
        Assert.False(initial.Configured);
        Assert.Equal("https://api.deepseek.com", initial.BaseUrl);
        Assert.Equal("deepseek-flash", initial.Model);

        var saved = await (await SaveAi(c, DeepSeek())).Read<AiSettingsDto>();
        Assert.True(saved.Configured);
        Assert.True(saved.HasApiKey);
        Assert.DoesNotContain("sk-test", await (await c.GetAsync("/api/ai/settings")).Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, (await SaveAi(c, new { provider = "openAiCompatible", baseUrl = "nope", model = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync("/api/ai/settings")).StatusCode);
        Assert.False((await (await c.GetAsync("/api/ai/settings")).Read<AiSettingsDto>()).Configured);
    }

    [Fact]
    public async Task Test_checks_text_and_vision_and_sends_openai_format()
    {
        using var app = new TestApp();
        var c = await app.SetupClientAsync();
        app.Ai.Bodies.Enqueue(OpenAiReply(new { ok = true }));
        app.Ai.Bodies.Enqueue(OpenAiReply("```json\n{\"color\": \"Red\"}\n```"));

        var r = await (await c.Post("/api/ai/test", DeepSeek())).Read<AiTestResult>();
        Assert.True(r.Text);
        Assert.True(r.Vision);

        var (req, body) = app.Ai.Requests[1];
        Assert.Equal("https://api.deepseek.com/chat/completions", req.RequestUri!.ToString());
        Assert.Equal("Bearer sk-test", req.Headers.Authorization!.ToString());
        var json = JsonNode.Parse(body)!;
        Assert.Equal("deepseek-flash", json["model"]!.GetValue<string>());
        Assert.Equal("json_object", json["response_format"]!["type"]!.GetValue<string>());
        // Thinking models count reasoning toward max_tokens, so none is sent; the service's own maximum applies.
        Assert.Null(json["max_tokens"]);
        Assert.StartsWith("data:image/png;base64,", json["messages"]![1]!["content"]![0]!["image_url"]!["url"]!.GetValue<string>());
    }

    [Fact]
    public async Task Test_reports_a_model_that_cannot_see()
    {
        using var app = new TestApp();
        var c = await app.SetupClientAsync();
        app.Ai.Bodies.Enqueue(OpenAiReply(new { ok = true }));
        app.Ai.Bodies.Enqueue(OpenAiReply(new { color = "I can't see images" }));

        var r = await (await c.Post("/api/ai/test", DeepSeek())).Read<AiTestResult>();
        Assert.True(r.Text);
        Assert.False(r.Vision);
        Assert.Contains("can't see images", r.VisionError);
    }

    [Fact]
    public async Task Bad_key_is_a_readable_error()
    {
        using var app = new TestApp();
        var c = await app.SetupClientAsync();
        app.Ai.Status = HttpStatusCode.Unauthorized;

        var res = await c.Post("/api/ai/test", DeepSeek());
        Assert.Equal(HttpStatusCode.BadGateway, res.StatusCode);
        Assert.Contains("rejected the API key", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Anthropic_uses_native_format_and_saved_key()
    {
        using var app = new TestApp();
        var c = await app.SetupClientAsync();
        (await SaveAi(c, new { provider = "anthropic", baseUrl = "https://api.anthropic.com", apiKey = "sk-ant-x", model = "claude-opus-5-5" })).EnsureSuccessStatusCode();
        app.Ai.Bodies.Enqueue(AnthropicReply("""{"items": [{"name": "Eggs", "category": "eggs", "location": "fridge", "quantity": 12}]}"""));

        var result = await (await c.Post("/api/inventory/read/text", new { text = "a dozen eggs" })).Read<ReadResultDto>();
        Assert.Equal("Eggs", Assert.Single(result.Items).Name);

        var (req, body) = Assert.Single(app.Ai.Requests);
        Assert.Equal("https://api.anthropic.com/v1/messages", req.RequestUri!.ToString());
        Assert.Equal("sk-ant-x", req.Headers.GetValues("x-api-key").Single());
        var json = JsonNode.Parse(body)!;
        Assert.Equal("claude-opus-5-5", json["model"]!.GetValue<string>());
        Assert.True(json["max_tokens"]!.GetValue<int>() >= 16000);
        Assert.Contains("a dozen eggs", json["messages"]![0]!["content"]![0]!["text"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("openAiCompatible")]
    [InlineData("anthropic")]
    public async Task A_cut_off_reply_says_so_instead_of_bad_json(string provider)
    {
        using var app = new TestApp();
        var c = await app.SetupClientAsync();
        (await SaveAi(c, new { provider, baseUrl = "https://ai.example.com", apiKey = "k", model = "m" })).EnsureSuccessStatusCode();
        const string partial = """{"items": [{"name": "Eggs", "category": "eg""";
        app.Ai.Bodies.Enqueue(provider == "anthropic" ? AnthropicReply(partial, stop: "max_tokens") : OpenAiReply(partial, finish: "length"));

        var res = await c.Post("/api/inventory/read/text", new { text = "a dozen eggs" });
        Assert.Equal(HttpStatusCode.BadGateway, res.StatusCode);
        Assert.Contains("cut off", await res.Content.ReadAsStringAsync());
        Assert.Single(app.Ai.Requests); // the same request would be cut off again, so no retry
    }

    [Fact]
    public async Task Numbers_written_as_text_are_accepted()
    {
        using var app = new TestApp();
        var c = await app.SetupClientAsync();
        (await SaveAi(c, DeepSeek())).EnsureSuccessStatusCode();
        app.Ai.Bodies.Enqueue(OpenAiReply("""{"items": [{"name": "Eggs", "category": "eggs", "location": "fridge", "quantity": "12"}]}"""));

        var result = await (await c.Post("/api/inventory/read/text", new { text = "a dozen eggs" })).Read<ReadResultDto>();
        Assert.Equal(12, Assert.Single(result.Items).Quantity);
    }

    [Fact]
    public async Task Reading_needs_ai_set_up()
    {
        using var app = new TestApp();
        var c = await app.SetupClientAsync();
        var res = await c.Post("/api/inventory/read/text", new { text = "milk" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("Settings", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Typed_list_is_cleaned_up_matched_and_can_be_added_in_one_go()
    {
        using var app = new TestApp();
        var c = await app.SetupClientAsync();
        var locs = await (await c.GetAsync("/api/inventory/locations")).Read<List<LocationDto>>();
        var fridge = locs.First(l => l.Kind == LocationKind.Fridge).Id;
        var known = await (await c.Post("/api/inventory/items", new { product = new { name = "Milk 2%", category = "dairy" }, locationId = fridge })).Read<ItemDto>();
        (await SaveAi(c, DeepSeek())).EnsureSuccessStatusCode();

        // Blank first reply (DeepSeek can do this in JSON mode) is retried.
        app.Ai.Bodies.Enqueue(OpenAiReply("   "));
        app.Ai.Bodies.Enqueue(OpenAiReply(new
        {
            store = "Superstore",
            date = app.Today.AddDays(-1).ToString("yyyy-MM-dd"),
            items = new object[]
            {
                new { name = "milk 2%", category = "dairy", location = "fridge", quantity = 2, unit = "L", price = 9.98, raw = "GV 2% MLK" },
                new { name = "Frozen peas", category = "not-a-category", location = "freezer", quantity = 1, raw = "PEAS FRZ" },
                new { name = "Chicken thighs", category = "poultry", location = "garage", quantity = -3, raw = "BNLS CHKN THI" },
                new { name = "  ", category = "other" },
            },
            skipped = new[] { "PAPER TOWEL" },
        }));

        var result = await (await c.Post("/api/inventory/read/text", new { text = "2 L milk, frozen peas, chicken thighs" })).Read<ReadResultDto>();

        Assert.Equal(2, app.Ai.Requests.Count);
        Assert.Contains("Milk 2%", app.Ai.Requests[1].Body); // known product names are offered to the model
        Assert.Equal("Superstore", result.Store);
        Assert.Equal(app.Today.AddDays(-1), result.PurchasedAt);
        Assert.Equal(["PAPER TOWEL"], result.Skipped);
        Assert.Equal(3, result.Items.Count);

        var milk = result.Items[0];
        Assert.Equal(known.Product.Id, milk.Product?.Id);
        Assert.Equal("Milk 2%", milk.Name);
        Assert.Equal(2, milk.Quantity);

        var peas = result.Items[1];
        Assert.Null(peas.Product);
        Assert.Equal(LocationKind.Freezer, peas.Location);
        Assert.True(Categories.Exists(peas.Category));

        var chicken = result.Items[2];
        Assert.Equal(1, chicken.Quantity);
        Assert.Equal(LocationKind.Fridge, chicken.Location); // unknown location falls back to the category default

        // Review done: add everything at once; two lines naming the same new product share it.
        var freezer = locs.First(l => l.Kind == LocationKind.Freezer).Id;
        var added = await (await c.Post("/api/inventory/items/batch", new
        {
            items = new object[]
            {
                new { productId = milk.Product!.Id, locationId = fridge, quantity = 2 },
                new { product = new { name = "Frozen peas", category = "vegetables" }, locationId = freezer },
                new { product = new { name = "frozen peas", category = "vegetables" }, locationId = freezer },
            },
        })).Read<List<ItemDto>>();
        Assert.Equal(3, added.Count);
        Assert.Equal(added[1].Product.Id, added[2].Product.Id);
        Assert.Equal(app.Today, added[1].FrozenAt);
    }

    [Fact]
    public async Task Batch_is_all_or_nothing()
    {
        using var app = new TestApp();
        var c = await app.SetupClientAsync();
        var fridge = (await (await c.GetAsync("/api/inventory/locations")).Read<List<LocationDto>>()).First(l => l.Kind == LocationKind.Fridge).Id;

        var res = await c.Post("/api/inventory/items/batch", new
        {
            items = new object[]
            {
                new { product = new { name = "Butter", category = "dairy" }, locationId = fridge },
                new { product = new { name = "Ghost", category = "dairy" }, locationId = 99999 },
            },
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Empty(await (await c.GetAsync("/api/inventory/items")).Read<List<ItemDto>>());

        var invalid = await c.Post("/api/inventory/items/batch", new { items = new object[] { new { locationId = fridge, quantity = -1 } } });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var text = await invalid.Content.ReadAsStringAsync();
        Assert.True(text.Contains("items[0]"), text);
    }

    [Fact]
    public void Extracts_json_from_chatty_replies()
    {
        Assert.Equal("""{"a":1}""", AiService.ExtractJson("Sure! Here you go:\n```json\n{\"a\":1}\n```"));
        Assert.Null(AiService.ExtractJson("   "));
        Assert.Null(AiService.ExtractJson("no json here"));
    }
}
