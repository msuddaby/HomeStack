using HomeStack.Core.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace HomeStack.Core.Images;

/// <summary>
/// A small local copy of a picture: a photo someone uploaded, or a product/recipe image from the web.
/// Kept in the database (not on disk) so the nightly backup includes uploads.
/// </summary>
public class StoredImage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int HouseholdId { get; set; }
    public required string ContentType { get; set; }
    public required byte[] Data { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    /// <summary>Where it was downloaded from; null for uploads.</summary>
    public string? SourceUrl { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    internal static void Configure(ModelBuilder mb) => mb.Entity<StoredImage>(e =>
    {
        e.ToTable("images");
        e.Property(x => x.ContentType).HasMaxLength(40);
        e.Property(x => x.SourceUrl).HasMaxLength(2000);
        e.HasOne<Household>().WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => x.HouseholdId);
    });

    /// <summary>What an &lt;img&gt; should load: the local copy when there is one, otherwise the original link.</summary>
    public static string? Src(Guid? id, string? url) => id is { } g ? $"/api/images/{g}" : url;
}

/// <summary>Shrinks pictures to small WebP copies.</summary>
public static class Thumbnails
{
    /// <summary>Largest upload or download accepted, before shrinking.</summary>
    public const long MaxBytes = 10 * 1024 * 1024;
    private const long MaxPixels = 50_000_000;
    // Only the common web formats get decoded; anything else is refused before its pixels are read.
    private static readonly SKEncodedImageFormat[] Allowed =
        [SKEncodedImageFormat.Jpeg, SKEncodedImageFormat.Png, SKEncodedImageFormat.Webp, SKEncodedImageFormat.Gif];

    public record Result(byte[] Data, int Width, int Height)
    {
        public const string ContentType = "image/webp";
    }

    /// <summary>
    /// Decodes a JPEG, PNG, WebP or GIF, turns it upright (phone photos store rotation separately), fits it inside
    /// <paramref name="maxEdge"/> pixels and re-encodes as WebP, which also drops metadata such as GPS location.
    /// Returns null for anything that isn't one of those formats or can't be read.
    /// </summary>
    public static Result? Shrink(byte[] input, int maxEdge)
    {
        using var codec = SKCodec.Create(new SKMemoryStream(input));
        if (codec is null || !Allowed.Contains(codec.EncodedFormat)) return null;
        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > MaxPixels) return null;

        using var decoded = SKBitmap.Decode(codec, info.WithColorType(SKColorType.Rgba8888).WithAlphaType(SKAlphaType.Premul));
        if (decoded is null) return null;

        var scale = Math.Min(1.0, (double)maxEdge / Math.Max(decoded.Width, decoded.Height));
        int w = Math.Max(1, (int)Math.Round(decoded.Width * scale)), h = Math.Max(1, (int)Math.Round(decoded.Height * scale));
        using var resized = scale < 1
            ? decoded.Resize(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul), new SKSamplingOptions(SKCubicResampler.Mitchell))
            : decoded.Copy();
        if (resized is null) return null;

        using var upright = Orient(resized, codec.EncodedOrigin);
        using var image = SKImage.FromBitmap(upright);
        using var data = image.Encode(SKEncodedImageFormat.Webp, 80);
        return data is null ? null : new Result(data.ToArray(), upright.Width, upright.Height);
    }

    /// <summary>Applies an EXIF orientation so the pixels themselves are the right way up.</summary>
    private static SKBitmap Orient(SKBitmap src, SKEncodedOrigin origin)
    {
        if (origin == SKEncodedOrigin.TopLeft) return src.Copy();
        float w = src.Width, h = src.Height;
        // Maps a source pixel (x, y) to where it belongs in the upright picture.
        var (m, swap) = origin switch
        {
            SKEncodedOrigin.TopRight => (new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1), false),     // mirrored
            SKEncodedOrigin.BottomRight => (new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1), false), // upside down
            SKEncodedOrigin.BottomLeft => (new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1), false),   // flipped
            SKEncodedOrigin.LeftTop => (new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1), true),        // transposed
            SKEncodedOrigin.RightTop => (new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1), true),      // needs 90° clockwise
            SKEncodedOrigin.RightBottom => (new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1), true),  // transverse
            SKEncodedOrigin.LeftBottom => (new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1), true),    // needs 90° anticlockwise
            _ => (SKMatrix.Identity, false),
        };
        var result = new SKBitmap(swap ? src.Height : src.Width, swap ? src.Width : src.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.Transparent);
        canvas.SetMatrix(m);
        canvas.DrawBitmap(src, 0, 0);
        return result;
    }
}

/// <summary>Saves, reads and deletes stored images.</summary>
public class ImageService(HomeStackDbContext db)
{
    /// <summary>Shrinks and stores a picture. Throws <see cref="BadRequestException"/> if it isn't a usable image.</summary>
    public async Task<StoredImage> SaveAsync(int householdId, byte[] data, int maxEdge, string? sourceUrl = null, CancellationToken ct = default)
    {
        var small = Thumbnails.Shrink(data, maxEdge) ?? throw new BadRequestException("That file isn't a JPEG, PNG, WebP or GIF picture.");
        var image = new StoredImage
        {
            HouseholdId = householdId, ContentType = Thumbnails.Result.ContentType, Data = small.Data,
            Width = small.Width, Height = small.Height, SourceUrl = sourceUrl,
        };
        db.Add(image);
        await db.SaveChangesAsync(ct);
        return image;
    }

    /// <summary>Reads an uploaded file, refusing anything over <see cref="Thumbnails.MaxBytes"/>.</summary>
    public static async Task<byte[]> ReadUploadAsync(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) throw new BadRequestException("Choose a photo to upload.");
        if (file.Length > Thumbnails.MaxBytes) throw new BadRequestException("Photos must be under 10 MB.");
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        return ms.ToArray();
    }

    public async Task DeleteAsync(Guid? id, CancellationToken ct = default)
    {
        if (id is { } g) await db.Set<StoredImage>().Where(i => i.Id == g).ExecuteDeleteAsync(ct);
    }
}

public static class ImageEndpoints
{
    public static void MapImageEndpoints(this IEndpointRouteBuilder app)
    {
        // An image's id changes whenever the picture does, so browsers can keep it forever.
        app.MapGet("/api/images/{id:guid}", async (Guid id, HomeStackDbContext db, ICurrentUser user, HttpResponse response, CancellationToken ct) =>
        {
            var hid = user.HouseholdId;
            var image = await db.Set<StoredImage>().Where(i => i.Id == id && i.HouseholdId == hid)
                .Select(i => new { i.Data, i.ContentType }).SingleOrDefaultAsync(ct);
            if (image is null) return Results.NotFound();
            response.Headers.CacheControl = "private, max-age=31536000, immutable";
            return Results.File(image.Data, image.ContentType);
        }).WithTags("Core").RequireAuthorization();
    }
}
