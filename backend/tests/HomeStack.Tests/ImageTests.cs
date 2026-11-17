using System.Net.Http.Json;
using System.Net;
using System.Net.Http.Headers;
using HomeStack.Core.Images;
using HomeStack.Modules.Inventory;
using HomeStack.Modules.Meals;
using SkiaSharp;

namespace HomeStack.Tests;

public class ImageTests
{
    /// <summary>A solid-colour PNG of the given size.</summary>
    private static byte[] Png(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.OrangeRed);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static async Task<(int Width, int Height, string? Type, CacheControlHeaderValue? Cache)> Fetch(HttpClient c, string src)
    {
        var res = await c.GetAsync(src);
        res.EnsureSuccessStatusCode();
        using var bitmap = SKBitmap.Decode(await res.Content.ReadAsByteArrayAsync());
        return (bitmap.Width, bitmap.Height, res.Content.Headers.ContentType?.MediaType, res.Headers.CacheControl);
    }

    private static MultipartFormDataContent Upload(byte[] bytes, string type = "image/png")
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(type);
        return new MultipartFormDataContent { { file, "image", "photo.png" } };
    }

    private static async Task<int> RunCache(TestApp app)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ImageCache>().CacheMissingAsync(CancellationToken.None);
    }

    [Fact]
    public void Shrink_fits_the_longest_edge_and_refuses_non_images()
    {
        var small = Thumbnails.Shrink(Png(2000, 1000), 400)!;
        Assert.Equal((400, 200), (small.Width, small.Height));

        var tiny = Thumbnails.Shrink(Png(50, 80), 400)!; // never enlarged
        Assert.Equal((50, 80), (tiny.Width, tiny.Height));

        Assert.Null(Thumbnails.Shrink("<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray(), 400));
        Assert.Null(Thumbnails.Shrink([1, 2, 3], 400));
    }

    [Theory]
    [InlineData(6, "red", "blue")]  // phone held upright: stored sideways, should turn 90° clockwise
    [InlineData(8, "blue", "red")]  // the other way: 90° anticlockwise
    public void Shrink_turns_phone_photos_upright(int orientation, string top, string bottom)
    {
        // A 200×100 JPEG: left half red, right half blue, with an EXIF orientation tag.
        using var bitmap = new SKBitmap(200, 100);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.DrawRect(0, 0, 100, 100, new SKPaint { Color = SKColors.Red });
            canvas.DrawRect(100, 0, 100, 100, new SKPaint { Color = SKColors.Blue });
        }
        using var jpeg = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Jpeg, 95);
        byte[] exif =
        [
            0xFF, 0xE1, 0x00, 0x22, (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0,
            (byte)'I', (byte)'I', 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00, // little-endian TIFF, first IFD at 8
            0x01, 0x00,                                               // one entry
            0x12, 0x01, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00, (byte)orientation, 0x00, 0x00, 0x00, // Orientation, SHORT
            0x00, 0x00, 0x00, 0x00,                                   // no next IFD
        ];
        var bytes = jpeg.ToArray();
        var withExif = bytes[..2].Concat(exif).Concat(bytes[2..]).ToArray();

        var result = Thumbnails.Shrink(withExif, 400)!;
        Assert.Equal((100, 200), (result.Width, result.Height));
        using var upright = SKBitmap.Decode(result.Data);
        string Colour(SKColor c) => c.Red > c.Blue ? "red" : "blue";
        Assert.Equal(top, Colour(upright.GetPixel(50, 50)));
        Assert.Equal(bottom, Colour(upright.GetPixel(50, 150)));
    }

    [Fact]
    public async Task Uploaded_product_photo_is_shrunk_served_and_replaces_the_web_link()
    {
        using var app = new TestApp();
        var c = await app.SetupClientAsync();
        var p = await (await c.Post("/api/inventory/products",
            new { name = "Oat milk", category = "beverages", imageUrl = "https://example.com/oat.png" })).Read<ProductDto>();
        Assert.Equal("https://example.com/oat.png", p.Image);

        var updated = await (await c.PostAsync($"/api/inventory/products/{p.Id}/image", Upload(Png(1200, 1600)))).Read<ProductDto>();
        Assert.Null(updated.ImageUrl);
        Assert.StartsWith("/api/images/", updated.Image);

        var img = await Fetch(c, updated.Image!);
        Assert.Equal((288, 384), (img.Width, img.Height));
        Assert.Equal("image/webp", img.Type);
        Assert.True(img.Cache?.Private);

        // Saving the product settings (which sends back imageUrl: null) keeps the upload.
        var saved = await (await c.PutAsJsonAsync($"/api/inventory/products/{p.Id}",
            new { name = "Oat milk", category = "beverages", imageUrl = (string?)null }, TestApp.Json)).Read<ProductDto>();
        Assert.Equal(updated.Image, saved.Image);

        // A new upload replaces the old picture, which is deleted.
        var second = await (await c.PostAsync($"/api/inventory/products/{p.Id}/image", Upload(Png(100, 100)))).Read<ProductDto>();
        Assert.NotEqual(updated.Image, second.Image);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync(updated.Image)).StatusCode);

        var removed = await (await c.DeleteAsync($"/api/inventory/products/{p.Id}/image")).Read<ProductDto>();
        Assert.Null(removed.Image);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync(second.Image)).StatusCode);
    }

    [Fact]
    public async Task Upload_rejects_files_that_arent_pictures()
    {
        using var app = new TestApp();
        var c = await app.SetupClientAsync();
        var p = await (await c.Post("/api/inventory/products", new { name = "Rice", category = "dry-goods" })).Read<ProductDto>();

        var res = await c.PostAsync($"/api/inventory/products/{p.Id}/image", Upload("not a picture"u8.ToArray()));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsync("/api/inventory/products/9999/image", Upload(Png(10, 10)))).StatusCode);
    }

    [Fact]
    public async Task Web_pictures_get_a_local_copy_and_a_changed_link_is_fetched_again()
    {
        using var app = new TestApp();
        var c = await app.SetupClientAsync();
        app.Web.Binary = Png(800, 800);
        var p = await (await c.Post("/api/inventory/products",
            new { name = "Peanut butter", category = "dry-goods", imageUrl = "https://images.example.com/pb.png" })).Read<ProductDto>();

        Assert.Equal(1, await RunCache(app));
        Assert.Equal("https://images.example.com/pb.png", app.Web.Requests.Single().Request.RequestUri!.ToString());
        var cached = await (await c.GetAsync($"/api/inventory/products/{p.Id}")).Read<ProductDto>();
        Assert.StartsWith("/api/images/", cached.Image);
        Assert.Equal("https://images.example.com/pb.png", cached.ImageUrl);
        Assert.Equal((384, 384), ((await Fetch(c, cached.Image!)).Width, (await Fetch(c, cached.Image!)).Height));

        Assert.Equal(0, await RunCache(app)); // nothing left to do

        // A different link drops the old copy and shows the new link until it's fetched.
        var changed = await (await c.PutAsJsonAsync($"/api/inventory/products/{p.Id}",
            new { name = "Peanut butter", category = "dry-goods", imageUrl = "https://images.example.com/pb2.png" }, TestApp.Json)).Read<ProductDto>();
        Assert.Equal("https://images.example.com/pb2.png", changed.Image);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync(cached.Image)).StatusCode);
        Assert.Equal(1, await RunCache(app));
    }

    [Fact]
    public async Task Failed_downloads_keep_the_web_link_and_arent_retried_straight_away()
    {
        using var app = new TestApp();
        var c = await app.SetupClientAsync();
        app.Web.Status = HttpStatusCode.NotFound;
        var p = await (await c.Post("/api/inventory/products",
            new { name = "Jam", category = "dry-goods", imageUrl = "https://images.example.com/gone.png" })).Read<ProductDto>();

        Assert.Equal(0, await RunCache(app));
        Assert.Equal(0, await RunCache(app));
        Assert.Single(app.Web.Requests);
        Assert.Equal("https://images.example.com/gone.png", (await (await c.GetAsync($"/api/inventory/products/{p.Id}")).Read<ProductDto>()).Image);

        // Six hours later it tries again.
        app.Web.Status = HttpStatusCode.OK;
        app.Web.Binary = Png(10, 10);
        app.Time.Now = app.Time.Now.AddHours(7);
        Assert.Equal(1, await RunCache(app));
    }

    [Fact]
    public async Task Recipe_pictures_are_cached_for_the_recipe_list_and_meal_plan()
    {
        using var app = new TestApp();
        var c = await app.SetupClientAsync();
        app.Web.Binary = Png(3000, 2000);
        var r = await (await c.Post("/api/meals/recipes", new
        {
            title = "Chili", servings = 4, imageUrl = "https://example.com/chili.png",
            ingredients = new[] { "1 can beans" }, steps = Array.Empty<string>(), tags = Array.Empty<string>(),
        })).Read<RecipeDto>();
        await c.Post("/api/meals/plan", new { date = app.Today, slot = "dinner", recipeId = r.Id });

        Assert.Equal(1, await RunCache(app));
        var recipe = await (await c.GetAsync($"/api/meals/recipes/{r.Id}")).Read<RecipeDto>();
        Assert.StartsWith("/api/images/", recipe.Image);
        Assert.Equal((1000, 667), ((await Fetch(c, recipe.Image!)).Width, (await Fetch(c, recipe.Image!)).Height));
        Assert.Equal(recipe.Image, (await (await c.GetAsync("/api/meals/recipes")).Read<List<RecipeSummaryDto>>()).Single().Image);
        var plan = await (await c.GetAsync($"/api/meals/plan?from={app.Today:yyyy-MM-dd}&to={app.Today:yyyy-MM-dd}")).Read<List<PlanEntryDto>>();
        Assert.Equal(recipe.Image, plan.Single().Image);

        // Deleting the recipe deletes its picture.
        (await c.DeleteAsync($"/api/meals/recipes/{r.Id}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync(recipe.Image)).StatusCode);
    }

    [Fact]
    public async Task Uploaded_recipe_photo_replaces_the_link_and_survives_saving_the_recipe()
    {
        using var app = new TestApp();
        var c = await app.SetupClientAsync();
        object Body(string? imageUrl) => new
        {
            title = "Chili", servings = 4, imageUrl,
            ingredients = new[] { "1 can beans" }, steps = Array.Empty<string>(), tags = Array.Empty<string>(),
        };
        var r = await (await c.Post("/api/meals/recipes", Body("https://example.com/chili.png"))).Read<RecipeDto>();

        var withPhoto = await (await c.PostAsync($"/api/meals/recipes/{r.Id}/image", Upload(Png(3000, 2000)))).Read<RecipeDto>();
        Assert.Null(withPhoto.ImageUrl);
        Assert.StartsWith("/api/images/", withPhoto.Image);
        Assert.Equal((1000, 667), ((await Fetch(c, withPhoto.Image!)).Width, (await Fetch(c, withPhoto.Image!)).Height));

        // The edit page sends back an empty photo link: the upload stays.
        var saved = await (await c.PutAsJsonAsync($"/api/meals/recipes/{r.Id}", Body(null), TestApp.Json)).Read<RecipeDto>();
        Assert.Equal(withPhoto.Image, saved.Image);

        var removed = await (await c.DeleteAsync($"/api/meals/recipes/{r.Id}/image")).Read<RecipeDto>();
        Assert.Null(removed.Image);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync(withPhoto.Image)).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync($"/api/meals/recipes/{r.Id}/image", Upload("nope"u8.ToArray()))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsync("/api/meals/recipes/9999/image", Upload(Png(10, 10)))).StatusCode);
    }

    [Fact]
    public async Task Other_households_cannot_load_your_pictures()
    {
        using var app = new TestApp();
        var c = await app.SetupClientAsync();
        var p = await (await c.Post("/api/inventory/products", new { name = "Secret sauce", category = "condiments" })).Read<ProductDto>();
        var withPhoto = await (await c.PostAsync($"/api/inventory/products/{p.Id}/image", Upload(Png(50, 50)))).Read<ProductDto>();

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HomeStack.Core.HomeStackDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.IPasswordHasher<HomeStack.Core.Data.User>>();
            var h = new HomeStack.Core.Data.Household { Name = "Neighbours" };
            db.Households.Add(h);
            await db.SaveChangesAsync();
            var u = new HomeStack.Core.Data.User { HouseholdId = h.Id, Name = "N", Username = "neighbour" };
            u.PasswordHash = hasher.HashPassword(u, "password123");
            db.Users.Add(u);
            await db.SaveChangesAsync();
        }
        var other = app.CreateClient();
        (await other.Post("/api/auth/login", new { username = "neighbour", password = "password123" })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(withPhoto.Image)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.CreateClient().GetAsync(withPhoto.Image)).StatusCode);
    }
}
