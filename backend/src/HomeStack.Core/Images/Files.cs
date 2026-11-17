using HomeStack.Core.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace HomeStack.Core.Images;

/// <summary>
/// An original document kept as uploaded (a bill's PDF), in the database so backups include it.
/// Photos go through <see cref="ImageService"/> instead, which shrinks them.
/// </summary>
public class StoredFile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int HouseholdId { get; set; }
    public required string ContentType { get; set; }
    public required string FileName { get; set; }
    public required byte[] Data { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    internal static void Configure(ModelBuilder mb) => mb.Entity<StoredFile>(e =>
    {
        e.ToTable("files");
        e.Property(x => x.ContentType).HasMaxLength(80);
        e.Property(x => x.FileName).HasMaxLength(200);
        e.HasOne<Household>().WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => x.HouseholdId);
    });

    public static string Src(Guid id) => $"/api/files/{id}";
}

public static class Pdf
{
    public const string ContentType = "application/pdf";
    public const int MaxPages = 4;

    /// <summary>True when the bytes start like a PDF ("%PDF"), whatever the upload claimed.</summary>
    public static bool Looks(byte[] data) => data.Length > 4 && data[0] == '%' && data[1] == 'P' && data[2] == 'D' && data[3] == 'F';

    /// <summary>
    /// Renders the first pages as JPEGs for an AI to read, so text PDFs and scanned ones work the same with any provider.
    /// Throws <see cref="BadRequestException"/> for a PDF that can't be opened (damaged or password-protected).
    /// </summary>
    public static List<byte[]> RenderPages(byte[] pdf, int maxPages = MaxPages, int maxEdge = 1800)
    {
        try
        {
            var pages = new List<byte[]>();
            var count = Math.Min(PDFtoImage.Conversion.GetPageCount(pdf), maxPages);
            for (var i = 0; i < count; i++)
            {
                // ~150 dpi keeps small print readable; then fit inside maxEdge.
                using var page = PDFtoImage.Conversion.ToImage(pdf, page: i, options: new PDFtoImage.RenderOptions(Dpi: 150, WithAnnotations: true, BackgroundColor: SKColors.White));
                var scale = Math.Min(1.0, (double)maxEdge / Math.Max(page.Width, page.Height));
                using var sized = scale < 1
                    ? page.Resize(new SKImageInfo((int)(page.Width * scale), (int)(page.Height * scale)), new SKSamplingOptions(SKCubicResampler.Mitchell))
                    : page.Copy();
                using var image = SKImage.FromBitmap(sized);
                using var data = image.Encode(SKEncodedImageFormat.Jpeg, 85);
                pages.Add(data.ToArray());
            }
            if (pages.Count == 0) throw new BadRequestException("That PDF has no pages.");
            return pages;
        }
        catch (Exception ex) when (ex is not BadRequestException)
        {
            throw new BadRequestException("Couldn't open that PDF. Is it password-protected? Try a photo or screenshot of the bill instead.");
        }
    }
}

public class FileService(HomeStackDbContext db)
{
    public async Task<StoredFile> SaveAsync(int householdId, byte[] data, string contentType, string fileName, CancellationToken ct = default)
    {
        var file = new StoredFile { HouseholdId = householdId, Data = data, ContentType = contentType, FileName = fileName.Length <= 200 ? fileName : fileName[..200] };
        db.Add(file);
        await db.SaveChangesAsync(ct);
        return file;
    }

    public async Task DeleteAsync(Guid? id, CancellationToken ct = default)
    {
        if (id is { } g) await db.Set<StoredFile>().Where(f => f.Id == g).ExecuteDeleteAsync(ct);
    }
}

public static class FileEndpoints
{
    public static void MapFileEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/files/{id:guid}", async (Guid id, HomeStackDbContext db, ICurrentUser user, HttpResponse response, CancellationToken ct) =>
        {
            var hid = user.HouseholdId;
            var file = await db.Set<StoredFile>().Where(f => f.Id == id && f.HouseholdId == hid)
                .Select(f => new { f.Data, f.ContentType, f.FileName }).SingleOrDefaultAsync(ct);
            if (file is null) return Results.NotFound();
            response.Headers.CacheControl = "private, max-age=31536000, immutable";
            // "inline": opens in the browser's PDF viewer rather than downloading.
            response.Headers.ContentDisposition = new System.Net.Mime.ContentDisposition { Inline = true, FileName = file.FileName }.ToString();
            return Results.Bytes(file.Data, file.ContentType);
        }).WithTags("Core").RequireAuthorization();
    }
}
