using Atlas.Example.Entities;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Example.Data;

public class ExampleDbContext : DbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Balance> Balances => Set<Balance>();
    public DbSet<Order> Orders => Set<Order>();

    public ExampleDbContext(DbContextOptions<ExampleDbContext> options) : base(options)
    {
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.EnableSensitiveDataLogging();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(u => u.Id);
            entity.Property(u => u.Name).HasMaxLength(100);
            entity.Property(u => u.Email).HasMaxLength(200);

            entity.HasOne(u => u.Balance)
                .WithOne(b => b.User)
                .HasForeignKey<Balance>(b => b.UserId);

            entity.HasMany(u => u.Orders)
                .WithOne(o => o.User)
                .HasForeignKey(o => o.UserId);
        });

        modelBuilder.Entity<Balance>(entity =>
        {
            entity.HasKey(b => b.Id);
            entity.Property(b => b.Amount).HasPrecision(18, 2);
            entity.Property(b => b.Currency).HasMaxLength(3);
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.HasKey(o => o.Id);
            entity.Property(o => o.Amount).HasPrecision(18, 2);
            entity.Property(o => o.Status).HasMaxLength(50);
        });
    }
}
