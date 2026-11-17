using HomeStack.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace HomeStack.Core;

/// <summary>
/// Single DbContext shared by all modules. Core owns households/users; each module adds
/// its own entities via <see cref="IModule.ConfigureModel"/> and queries them with Set&lt;T&gt;().
/// </summary>
public class HomeStackDbContext(DbContextOptions<HomeStackDbContext> options, IEnumerable<IModule> modules)
    : DbContext(options)
{
    public DbSet<Household> Households => Set<Household>();
    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Household>(e =>
        {
            e.ToTable("households");
            e.Property(x => x.Name).HasMaxLength(80);
        });
        mb.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasIndex(x => x.Username).IsUnique();
            e.Property(x => x.Name).HasMaxLength(80);
            e.Property(x => x.Username).HasMaxLength(40);
            e.HasOne(x => x.Household).WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Cascade);
        });

        Notifications.NotificationSettings.Configure(mb);
        Ai.AiSettings.Configure(mb);
        Images.StoredImage.Configure(mb);

        foreach (var module in modules) module.ConfigureModel(mb);
    }
}
