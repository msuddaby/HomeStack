using HomeStack.Core;
using HomeStack.Core.Ai;
using HomeStack.Core.Images;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HomeStack.Modules.Expenses;

/// <summary>Spending: scanned receipts and manual expenses, what was bought where, and how often.</summary>
public class ExpensesModule : IModule
{
    public string Id => "expenses";
    public string Name => "Spending";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ExpensesService>();
        services.AddScoped<ReceiptReader>();
        services.AddScoped<HomeStack.Core.Notifications.IDigestSource, ExpensesDigest>();
    }

    public void ConfigureModel(ModelBuilder mb)
    {
        mb.Entity<Store>(e =>
        {
            e.ToTable("expenses_stores");
            e.Property(x => x.Name).HasMaxLength(80);
            e.HasIndex(x => new { x.HouseholdId, x.Name });
        });
        mb.Entity<TrackedItem>(e =>
        {
            e.ToTable("expenses_items");
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.Category).HasMaxLength(32);
            e.HasIndex(x => new { x.HouseholdId, x.Name });
        });
        mb.Entity<ItemCode>(e =>
        {
            e.ToTable("expenses_item_codes");
            e.Property(x => x.Code).HasMaxLength(40);
            e.HasIndex(x => new { x.HouseholdId, x.StoreId, x.Code }).IsUnique();
            e.HasIndex(x => x.ItemId);
            e.HasOne<Store>().WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<TrackedItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
        });
        mb.Entity<Receipt>(e =>
        {
            e.ToTable("expenses_receipts");
            e.Property(x => x.Note).HasMaxLength(500);
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(16);
            e.HasOne(x => x.Store).WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.SetNull);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.ReceiptId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.HouseholdId, x.PurchasedAt });
        });
        mb.Entity<ReceiptLine>(e =>
        {
            e.ToTable("expenses_receipt_lines");
            e.Property(x => x.Code).HasMaxLength(40);
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.Raw).HasMaxLength(200);
            e.Property(x => x.Category).HasMaxLength(32);
            e.Property(x => x.Unit).HasMaxLength(20);
            e.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.ItemId);
        });
    }

    public void MapEndpoints(RouteGroupBuilder g)
    {
        g.MapGet("/categories", () => ExpenseCategories.All);

        // Read receipt photos into a draft to review (nothing saved).
        g.MapPost("/read", async (IFormFileCollection files, ReceiptReader reader, CancellationToken ct) =>
        {
            var photos = files.GetFiles("images");
            if (photos.Count is 0 or > ReceiptReader.MaxImages)
                throw new BadRequestException($"Send 1 to {ReceiptReader.MaxImages} photos.");
            var list = new List<AiImage>();
            foreach (var f in photos)
            {
                if (!ReceiptReader.ImageTypes.Contains(f.ContentType)) throw new BadRequestException("Photos must be JPEG, PNG or WebP.");
                if (f.Length > ReceiptReader.MaxImageBytes) throw new BadRequestException("Each photo must be under 8 MB.");
                using var ms = new MemoryStream();
                await f.CopyToAsync(ms, ct);
                list.Add(new(ms.ToArray(), f.ContentType));
            }
            return await reader.ReadAsync(list, ct);
        }).DisableAntiforgery();

        // Receipts
        g.MapGet("/receipts", (DateOnly? from, DateOnly? to, int? storeId, ExpensesService s) => s.ListReceiptsAsync(from, to, storeId));
        g.MapGet("/receipts/{id:int}", (int id, ExpensesService s) => s.GetReceiptAsync(id));
        g.MapPost("/receipts", (SaveReceiptRequest req, ExpensesService s) => s.SaveReceiptAsync(req)).Validate<SaveReceiptRequest>();
        g.MapPost("/receipts/manual", (ManualExpenseRequest req, ExpensesService s) => s.AddManualAsync(req)).Validate<ManualExpenseRequest>();
        g.MapPut("/receipts/{id:int}", (int id, UpdateReceiptRequest req, ExpensesService s) => s.UpdateReceiptAsync(id, req)).Validate<UpdateReceiptRequest>();
        g.MapDelete("/receipts/{id:int}", async (int id, ExpensesService s) => { await s.DeleteReceiptAsync(id); return Results.NoContent(); });
        g.MapPost("/receipts/{id:int}/photos", async (int id, IFormFileCollection files, ExpensesService s, CancellationToken ct) =>
        {
            var photos = new List<byte[]>();
            foreach (var f in files.GetFiles("images")) photos.Add(await ImageService.ReadUploadAsync(f, ct));
            return await s.AddPhotosAsync(id, photos, ct);
        }).DisableAntiforgery();

        // Tracked items
        g.MapGet("/items", (string? q, ExpensesService s) => s.ListItemsAsync(q));
        g.MapGet("/items/due", (ExpensesService s) => s.GetDueAsync());
        g.MapGet("/items/{id:int}", (int id, ExpensesService s) => s.GetItemAsync(id));
        g.MapPut("/items/{id:int}", (int id, UpdateItemRequest req, ExpensesService s) => s.UpdateItemAsync(id, req)).Validate<UpdateItemRequest>();
        g.MapPost("/items/{id:int}/merge", (int id, MergeItemsRequest req, ExpensesService s) => s.MergeItemsAsync(id, req)).Validate<MergeItemsRequest>();

        g.MapGet("/summary", (string? month, int? months, ExpensesService s) => s.GetSummaryAsync(month, months ?? 6));
    }
}
