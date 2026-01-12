using Atlas.Example.Entities;

namespace Atlas.Example.Data;

public static class DataSeeder
{
    public static void Seed(ExampleDbContext db)
    {
        if (db.Users.Any())
        {
            return;
        }

        var users = new[]
        {
            CreateUser("Alice Johnson", "alice@example.com", 1500.00m, true),
            CreateUser("Bob Smith", "bob@example.com", 250.50m, true),
            CreateUser("Charlie Brown", "charlie@example.com", 0.00m, false),
            CreateUser("Diana Prince", "diana@example.com", 10000.00m, true),
            CreateUser("Eve Williams", "eve@example.com", 75.25m, true),
            CreateUser("Frank Miller", "frank@example.com", 500.00m, false),
            CreateUser("Grace Lee", "grace@example.com", 3200.00m, true),
            CreateUser("Henry Wilson", "henry@example.com", 890.00m, true),
            CreateUser("Ivy Chen", "ivy@example.com", 150.00m, true),
            CreateUser("Jack Davis", "jack@example.com", 4500.00m, false),
            CreateUser("Kathy Brown", "kathy@example.com", 0.00m, true),
        };

        db.Users.AddRange(users);
        db.SaveChanges();

        // Add some orders for collection navigation testing
        var statuses = new[] { "pending", "completed", "cancelled", "shipped" };
        var orders = new List<Order>();
        var random = new Random(42); // Fixed seed for reproducibility

        foreach (var user in users.Take(6)) // First 6 users get orders
        {
            var orderCount = random.Next(1, 5);
            for (var i = 0; i < orderCount; i++)
            {
                orders.Add(new Order
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    Amount = random.Next(10, 500) + random.Next(0, 100) / 100m,
                    Status = statuses[random.Next(statuses.Length)],
                    CreatedAt = DateTime.UtcNow.AddDays(-random.Next(1, 90))
                });
            }
        }

        db.Orders.AddRange(orders);
        db.SaveChanges();
    }

    private static User CreateUser(string name, string email, decimal balance, bool isActive)
    {
        var userId = Guid.NewGuid();
        return new User
        {
            Id = userId,
            Name = name,
            Email = email,
            CreatedAt = DateTime.UtcNow.AddDays(-Random.Shared.Next(1, 365)),
            IsActive = isActive,
            Balance = balance > 0 ? new Balance
            {
                Id = Guid.NewGuid(),
                Amount = balance,
                Currency = "USD",
                UserId = userId
            } : null
        };
    }
}
