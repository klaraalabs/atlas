using Atlas.Example.Data;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Example.Endpoints;

public static class UsersEndpoint
{
    public static void MapUsersEndpoints(this WebApplication app)
    {
        // GET /api/users - Simple list all (for comparison)
        app.MapGet("/api/users", async (ExampleDbContext db, CancellationToken ct) =>
        {
            var users = await db.Users
                .Include(u => u.Balance)
                .Select(u => new
                {
                    u.Id,
                    u.Name,
                    u.Email,
                    u.CreatedAt,
                    u.IsActive,
                    Balance = u.Balance == null ? null : new { u.Balance.Amount, u.Balance.Currency }
                })
                .ToArrayAsync(ct);

            return Results.Ok(users);
        })
        .WithName("GetAllUsers");
    }
}
