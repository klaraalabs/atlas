using System.Text.Json;
using Atlas.Compilation;
using Atlas.Example.Data;
using Atlas.Example.Entities;
using Atlas.Query;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Example.Endpoints;

public static class UsersEndpoint
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static void MapUsersEndpoints(this WebApplication app)
    {
        // Configure Atlas for User entity with security policy
        var executor = Atlas.For<User>()
            .AllowFields(
                u => u.Id,
                u => u.Name,
                u => u.Email,
                u => u.CreatedAt,
                u => u.IsActive,
                u => u.Balance!.Amount,
                u => u.Balance!.Currency
            )
            // Allow orders collection for collection navigation queries
            // Also allow order fields for filtering within collection
            .AllowFields("orders", "orders.amount", "orders.status", "orders.createdat")
            .AllowAllOperators()
            .MaxLimit(100)
            .DefaultLimit(25)
            .BuildExecutor();

        // POST /api/users/query - Atlas query endpoint
        app.MapPost("/api/users/query", async (AtlasQuery query, ExampleDbContext db, CancellationToken ct) =>
        {
            try
            {
                var result = await executor.ExecuteWithCountAsync(db, query, ct);
                return Results.Ok(new
                {
                    result.Data,
                    result.Offset,
                    result.Limit,
                    result.TotalCount
                });
            }
            catch (AtlasSecurityException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (AtlasCompilationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (AtlasQueryException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("QueryUsers");

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
