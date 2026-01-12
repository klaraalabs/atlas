using System.Text.Json;
using Atlas;
using Atlas.Compilation;
using Atlas.Example.Data;
using Atlas.Example.Entities;
using Atlas.Query;

namespace Atlas.Example.Endpoints;

public static class AtlasEndpoint
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static void MapAtlasEndpoints(this WebApplication app)
    {
        // Option 1: Auto-register all entities from DbContext (simple, permissive)
        // var engine = new AtlasEngine()
        //     .RegisterFromDbContext<ExampleDbContext>();

        // Option 2: Auto-register with custom defaults
        // var engine = new AtlasEngine()
        //     .RegisterFromDbContext<ExampleDbContext>(options =>
        //     {
        //         options.DefaultMaxLimit = 100;
        //         options.DefaultLimit = 25;
        //         options.EntityNameTransform = name => name.ToLowerInvariant(); // "Users" -> "users"
        //     });

        // Option 3: Mix auto-registration with custom overrides
        // Register specific entities first, then auto-register the rest
        var engine = new AtlasEngine()
            // Custom configuration for users (with explicit field security)
            .Register<User>("users", builder => builder
                .AllowFields(
                    u => u.Id,
                    u => u.Name,
                    u => u.Email,
                    u => u.CreatedAt,
                    u => u.IsActive,
                    u => u.Balance!.Amount,
                    u => u.Balance!.Currency
                )
                .AllowFields("orders", "orders.id", "orders.amount", "orders.status", "orders.createdat")
                .AllowAllOperators()
                .MaxLimit(100)
                .DefaultLimit(25))
            // Auto-register remaining entities (orders, balances) with permissive defaults
            .RegisterFromDbContext<ExampleDbContext>();

        // POST /api/query - Universal Atlas query endpoint
        app.MapPost("/api/query", async (AtlasQuery query, ExampleDbContext db, CancellationToken ct) =>
        {
            try
            {
                var result = await engine.ExecuteAsync(db, query, ct);
                return Results.Ok(new
                {
                    result.Data,
                    result.Offset,
                    result.Limit,
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
        .WithName("AtlasQuery");

        // GET /api/entities - List available entities
        app.MapGet("/api/entities", () => Results.Ok(new { entities = engine.RegisteredEntities }))
            .WithName("ListEntities");

        // GET /api/schema - Full schema introspection
        app.MapGet("/api/schema", () => Results.Ok(engine.GetSchema()))
            .WithName("GetSchema");

        // GET /api/schema/{entity} - Schema for a specific entity
        app.MapGet("/api/schema/{entity}", (string entity) =>
        {
            var schema = engine.GetEntitySchema(entity);
            if (schema is null)
            {
                return Results.NotFound(new { error = $"Entity '{entity}' not found." });
            }
            return Results.Ok(schema);
        })
        .WithName("GetEntitySchema");

        // GET /api/cache/stats - Expression cache statistics
        app.MapGet("/api/cache/stats", () =>
        {
            var stats = GlobalExpressionCache.Instance.GetStats();
            return Results.Ok(new
            {
                hits = stats.Hits,
                misses = stats.Misses,
                size = stats.Size,
                hitRate = stats.HitRate
            });
        })
        .WithName("GetCacheStats");
    }
}
