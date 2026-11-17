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
        services.AddScoped<PeopleService>();
        services.AddScoped<BillsService>();
        services.AddScoped<ExpenseNotifier>();
        services.AddScoped<HomeStack.Core.Notifications.IDigestSource, ExpensesDigest>();
        services.AddScoped<HomeStack.Core.Notifications.IDigestSource, BillsDigest>();
        services.AddHostedService<RecurringWorker>();
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
            e.Property(x => x.PackUnit).HasMaxLength(20);
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
            e.HasMany(x => x.Split).WithOne().HasForeignKey(x => x.ReceiptId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Shares).WithOne().HasForeignKey(x => x.ReceiptId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Person>().WithMany().HasForeignKey(x => x.PaidByPersonId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Recurring>().WithMany().HasForeignKey(x => x.RecurringId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.HouseholdId, x.PurchasedAt });
        });
        mb.Entity<Person>(e =>
        {
            e.ToTable("expenses_people");
            e.Property(x => x.Name).HasMaxLength(80);
            e.HasIndex(x => x.HouseholdId);
        });
        mb.Entity<SplitPart>(e =>
        {
            e.ToTable("expenses_split_parts");
            e.HasOne<Person>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.ReceiptId);
            e.HasIndex(x => x.LineId);
            e.HasIndex(x => x.RecurringId);
        });
        mb.Entity<Share>(e =>
        {
            e.ToTable("expenses_shares");
            e.HasOne<Person>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.ReceiptId);
            e.HasIndex(x => x.PersonId);
        });
        mb.Entity<Recurring>(e =>
        {
            e.ToTable("expenses_recurring");
            e.Property(x => x.Name).HasMaxLength(80);
            e.Property(x => x.Payee).HasMaxLength(80);
            e.Property(x => x.Category).HasMaxLength(32);
            e.Property(x => x.Note).HasMaxLength(500);
            e.Property(x => x.Frequency).HasConversion<string>().HasMaxLength(16);
            e.HasMany(x => x.Split).WithOne().HasForeignKey(x => x.RecurringId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.HouseholdId, x.NextDate });
        });
        mb.Entity<ReceiptLine>(e =>
        {
            e.ToTable("expenses_receipt_lines");
            e.HasMany(x => x.Split).WithOne().HasForeignKey(x => x.LineId).OnDelete(DeleteBehavior.Cascade);
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

        // Splitting: people, who paid and who owes what
        g.MapGet("/people", (PeopleService s) => s.ListAsync());
        g.MapPost("/people", (PersonRequest req, PeopleService s) => s.AddAsync(req)).Validate<PersonRequest>();
        g.MapPut("/people/{id:int}", (int id, PersonRequest req, PeopleService s) => s.RenameAsync(id, req)).Validate<PersonRequest>();
        g.MapDelete("/people/{id:int}", async (int id, PeopleService s) => { await s.ArchiveAsync(id); return Results.NoContent(); });
        g.MapPut("/receipts/{id:int}/split", (int id, SetSplitRequest req, ExpensesService s) => s.SetSplitAsync(id, req)).Validate<SetSplitRequest>();
        g.MapGet("/balances", (PeopleService s) => s.BalancesAsync());
        g.MapPost("/remind", (RemindRequest req, ExpenseNotifier n, CancellationToken ct) => n.RemindAsync(req, ct)).Validate<RemindRequest>();
        g.MapPut("/shares/{id:int}/received", async (int id, MarkReceivedRequest req, PeopleService s) =>
        {
            await s.MarkReceivedAsync(id, req.Received);
            return Results.NoContent();
        });

        // Bills: read a PDF or photos, save, attach the original, mark paid
        g.MapPost("/bills/read", async (IFormFileCollection form, BillsService bills, CancellationToken ct) =>
        {
            var uploads = form.GetFiles("files");
            if (uploads.Count is 0 or > BillsService.MaxImages) throw new BadRequestException($"Send a PDF or 1 to {BillsService.MaxImages} photos.");
            var files = new List<(byte[], string)>();
            foreach (var f in uploads)
            {
                if (f.Length > BillsService.MaxFileBytes) throw new BadRequestException("Files must be under 15 MB.");
                using var ms = new MemoryStream();
                await f.CopyToAsync(ms, ct);
                var data = ms.ToArray();
                if (!Pdf.Looks(data) && !ReceiptReader.ImageTypes.Contains(f.ContentType))
                    throw new BadRequestException("Bills must be a PDF, or JPEG, PNG or WebP photos.");
                files.Add((data, f.ContentType));
            }
            return await bills.ReadAsync(files, ct);
        }).DisableAntiforgery();
        g.MapPost("/bills", (SaveBillRequest req, BillsService s) => s.SaveAsync(req)).Validate<SaveBillRequest>();
        g.MapPost("/receipts/{id:int}/document", async (int id, IFormFile? file, ExpensesService s, CancellationToken ct) =>
        {
            if (file is null || file.Length == 0) throw new BadRequestException("Choose a file to attach.");
            if (file.Length > BillsService.MaxFileBytes) throw new BadRequestException("Files must be under 15 MB.");
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            return await s.AttachDocumentAsync(id, ms.ToArray(), Path.GetFileName(file.FileName), ct);
        }).DisableAntiforgery();
        g.MapPut("/receipts/{id:int}/bill-paid", (int id, BillPaidRequest req, ExpensesService s) => s.SetBillPaidAsync(id, req.PaidOn));

        // Recurring payments and expected bills
        g.MapGet("/recurring", (BillsService s) => s.ListRecurringAsync());
        g.MapPost("/recurring", (RecurringRequest req, BillsService s) => s.CreateRecurringAsync(req)).Validate<RecurringRequest>();
        g.MapPut("/recurring/{id:int}", (int id, RecurringRequest req, BillsService s) => s.UpdateRecurringAsync(id, req)).Validate<RecurringRequest>();
        g.MapDelete("/recurring/{id:int}", async (int id, BillsService s) => { await s.DeleteRecurringAsync(id); return Results.NoContent(); });
        g.MapGet("/upcoming", (int? days, BillsService s) => s.UpcomingAsync(days ?? 30));
    }
}
