# Atlas

A query compilation engine that maps structured JSON queries to Entity Framework Core `IQueryable` pipelines. Build flexible, secure APIs without writing custom endpoints for every query pattern.

## Features

-   **Dynamic field selection** - Clients choose which fields to return
-   **Nested projections** - Navigate relationships with dot notation (`user.balance.amount`)
-   **Filtering** - Rich WHERE clauses with AND/OR/NOT logic
-   **Aggregations** - SUM, AVG, MIN, MAX, COUNT with optional GROUP BY
-   **Collection queries** - ANY/ALL/NONE predicates for collection navigation
-   **Distinct queries** - Deduplicate results across projected fields
-   **Security policies** - Whitelist allowed fields, operators, and row-level filters
-   **Schema introspection** - API endpoints for client discovery
-   **Query caching** - Expression caching for improved performance
-   **Auto-registration** - Scan your DbContext to register all entities automatically

## Installation

```bash
dotnet add package QueryAtlas
```

## Quick Start

### 1. Register your entities

```csharp
// Option 1: Auto-register all entities from DbContext
var engine = new AtlasEngine()
    .RegisterFromDbContext<MyDbContext>();

// Option 2: Custom configuration per entity
var engine = new AtlasEngine()
    .Register<User>("users", builder => builder
        .AllowFields(u => u.Id, u => u.Name, u => u.Email, u => u.Balance!.Amount)
        .AllowAllOperators()
        .MaxLimit(100)
        .DefaultLimit(25));
```

### 2. Create an endpoint

```csharp
app.MapPost("/api/query", async (AtlasQuery query, MyDbContext db, CancellationToken ct) =>
{
    var result = await engine.ExecuteAsync(db, query, ct);
    return Results.Ok(result);
});
```

### 3. Query from clients

```bash
curl -X POST /api/query \
  -H "Content-Type: application/json" \
  -d '{
    "entity": "users",
    "select": ["id", "name", "balance.amount"],
    "where": { "op": "eq", "field": "isActive", "value": true },
    "orderBy": [{ "field": "name", "direction": "asc" }],
    "limit": 10
  }'
```

## Query Structure

```typescript
interface AtlasQuery {
    entity: string; // Entity to query ("users", "orders")
    select: string[]; // Fields to return
    where?: WhereClause; // Filter conditions
    groupBy?: string[]; // GROUP BY fields
    orderBy?: { field: string; direction: "asc" | "desc" }[];
    offset?: number; // Skip N records
    limit?: number; // Max records to return
    distinct?: boolean; // Remove duplicate rows (default: false)
}
```

## Field Selection

Select scalar fields and navigate relationships with dot notation:

```json
{
    "entity": "users",
    "select": ["id", "name", "email", "balance.amount", "balance.currency"]
}
```

**Response:**

```json
{
    "data": [
        {
            "id": "...",
            "name": "Alice",
            "email": "alice@example.com",
            "balance": {
                "amount": 150.0,
                "currency": "USD"
            }
        }
    ]
}
```

### Collection Fields

Select fields from collection navigation properties:

```json
{
    "entity": "users",
    "select": ["name", "orders.amount", "orders.status"]
}
```

**Response:**

```json
{
    "data": [
        {
            "name": "Alice",
            "orders": [
                { "amount": 99.99, "status": "completed" },
                { "amount": 49.99, "status": "pending" }
            ]
        }
    ]
}
```

## Filtering (WHERE)

### Comparison Operators

| Operator     | Description           |
| ------------ | --------------------- |
| `eq`         | Equals                |
| `neq`        | Not equals            |
| `gt`         | Greater than          |
| `gte`        | Greater than or equal |
| `lt`         | Less than             |
| `lte`        | Less than or equal    |
| `contains`   | String contains       |
| `startswith` | String starts with    |
| `endswith`   | String ends with      |
| `in`         | Value in array        |
| `isnull`     | Is null check         |
| `isnotnull`  | Is not null check     |

### Simple Filter

```json
{
    "where": { "op": "eq", "field": "status", "value": "active" }
}
```

### Compound Filters (AND/OR)

```json
{
    "where": {
        "op": "and",
        "nodes": [
            { "op": "eq", "field": "isActive", "value": true },
            { "op": "gte", "field": "balance.amount", "value": 100 }
        ]
    }
}
```

### Negation

```json
{
    "where": {
        "op": "not",
        "nodes": [{ "op": "eq", "field": "status", "value": "banned" }]
    }
}
```

### Type Hints

For ambiguous types (like GUIDs), use the `type` property:

```json
{
    "where": {
        "op": "eq",
        "field": "id",
        "value": "550e8400-e29b-41d4-a716-446655440000",
        "type": "guid"
    }
}
```

## Collection Predicates (ANY/ALL/NONE)

Filter based on related collections:

### ANY - At least one match

```json
{
    "entity": "users",
    "select": ["name"],
    "where": {
        "op": "any",
        "field": "orders",
        "condition": { "op": "gt", "field": "amount", "value": 100 }
    }
}
```

_Users who have at least one order over $100_

### ALL - Every item matches

```json
{
    "where": {
        "op": "all",
        "field": "orders",
        "condition": { "op": "eq", "field": "status", "value": "completed" }
    }
}
```

_Users where all orders are completed_

### NONE - No items match

```json
{
    "where": {
        "op": "none",
        "field": "orders",
        "condition": { "op": "eq", "field": "status", "value": "cancelled" }
    }
}
```

_Users with no cancelled orders_

## Aggregations

Use aggregate functions with dot notation: `field.function`

```json
{
    "entity": "orders",
    "select": ["amount.sum", "amount.avg", "id.count"]
}
```

### Supported Functions

| Function | Description      |
| -------- | ---------------- |
| `sum`    | Sum of values    |
| `avg`    | Average          |
| `min`    | Minimum          |
| `max`    | Maximum          |
| `count`  | Count of records |

### GROUP BY

```json
{
    "entity": "orders",
    "select": ["status", "amount.sum", "id.count"],
    "groupBy": ["status"]
}
```

**Response:**

```json
{
    "data": [
        { "status": "completed", "amount": { "sum": 5420.0 }, "id": { "count": 42 } },
        { "status": "pending", "amount": { "sum": 1230.5 }, "id": { "count": 15 } }
    ]
}
```

## Distinct Queries

Remove duplicate rows from results with the `distinct` option:

```json
{
    "entity": "orders",
    "select": ["status"],
    "distinct": true
}
```

**Response:**

```json
{
    "data": [{ "status": "completed" }, { "status": "pending" }, { "status": "cancelled" }]
}
```

Distinct works with multiple fields and nested projections:

```json
{
    "entity": "users",
    "select": ["isActive", "balance.currency"],
    "distinct": true
}
```

## Security

Atlas enforces security policies to control what clients can query.

### Field Whitelisting

```csharp
Atlas.For<User>()
    .AllowFields(
        u => u.Id,
        u => u.Name,
        u => u.Email,
        u => u.Balance!.Amount
    )
    // String paths for collections/dynamic fields
    .AllowFields("orders", "orders.amount", "orders.status")
```

Queries for non-whitelisted fields return an error:

```json
{ "error": "Field 'password' is not allowed." }
```

### Operator Restrictions

```csharp
Atlas.For<User>()
    .AllowOperators(CompareOp.Eq, CompareOp.In)  // Only equality checks
```

### Limits

```csharp
Atlas.For<User>()
    .MaxLimit(100)      // Cap client-requested limits
    .DefaultLimit(25)   // Default when not specified
```

### Row-Level Security (Global Filters)

Apply automatic WHERE clauses to every query for a given entity. Perfect for multi-tenant isolation or soft deletes:

```csharp
// Multi-tenant isolation
Atlas.For<User>()
    .AllowAllFields()
    .WhereAlways(u => u.TenantId == currentTenantId);

// Soft delete filtering
Atlas.For<Order>()
    .AllowAllFields()
    .WhereAlways(o => !o.IsDeleted);
```

Multiple global filters are combined with AND:

```csharp
Atlas.For<Document>()
    .WhereAlways(d => d.TenantId == tenantId)
    .WhereAlways(d => d.Status != "archived")
    .WhereAlways(d => d.AccessLevel <= userAccessLevel);
```

### Navigation Depth Limits

Prevent deeply nested queries that could cause performance issues:

```csharp
Atlas.For<User>()
    .AllowAllFields()
    .MaxNavigationDepth(3);  // Max 3 levels of nesting
```

Queries exceeding the depth limit return an error:

```json
{ "error": "Navigation depth 4 exceeds maximum allowed depth of 3" }
```

## Schema Introspection

Atlas provides endpoints for clients to discover available entities and fields.

### Get Full Schema

```csharp
app.MapGet("/api/schema", () => engine.GetSchema());
```

**Response:**

```json
{
    "entities": {
        "users": {
            "fields": [
                { "name": "id", "type": "Guid" },
                { "name": "name", "type": "String" },
                { "name": "balance.amount", "type": "Decimal" }
            ]
        },
        "orders": {
            "fields": [...]
        }
    },
    "operators": ["eq", "neq", "gt", "gte", "lt", "lte", ...],
    "limits": {
        "defaultLimit": 25,
        "maxLimit": 100
    }
}
```

### Get Single Entity Schema

```csharp
app.MapGet("/api/schema/{entity}", (string entity) => engine.GetEntitySchema(entity));
```

## Performance

### Expression Caching

Atlas caches compiled expression trees to avoid recompilation on repeated queries. Monitor cache performance with:

```csharp
app.MapGet("/api/cache/stats", () =>
{
    var stats = GlobalExpressionCache.GetStats();
    return new
    {
        hits = stats.Hits,
        misses = stats.Misses,
        size = stats.Size,
        hitRate = stats.Size > 0 ? (double)stats.Hits / (stats.Hits + stats.Misses) : 0
    };
});
```

### Cache Management

```csharp
// Clear all cached expressions
GlobalExpressionCache.Clear();
```

## Configuration Options

### AtlasEngine Options

```csharp
var engine = new AtlasEngine()
    .RegisterFromDbContext<MyDbContext>(options =>
    {
        // Transform entity names (default: lowercase)
        options.EntityNameTransform = name => name.ToLowerInvariant();

        // Default limits for all entities
        options.DefaultMaxLimit = 100;
        options.DefaultLimit = 25;
    });
```

### Per-Entity Configuration

```csharp
var engine = new AtlasEngine()
    // Custom config for sensitive entities
    .Register<User>("users", builder => builder
        .AllowFields(u => u.Id, u => u.Name)
        .AllowOperators(CompareOp.Eq)
        .MaxLimit(50))

    // Permissive config for public data
    .Register<Product>("products", builder => builder
        .AllowAllFields()
        .AllowAllOperators()
        .MaxLimit(1000))

    // Auto-register remaining entities
    .RegisterFromDbContext<MyDbContext>();
```

## Response Format

```typescript
interface AtlasResult {
    data: object[]; // Query results
    offset: number; // Current offset
    limit: number; // Applied limit
    totalCount?: number; // Total matching records (with ExecuteWithCountAsync)
}
```

### With Total Count

```csharp
var result = await engine.ExecuteWithCountAsync(db, query, ct);
// result.TotalCount = total matching records (ignoring pagination)
```

## Error Handling

Atlas throws typed exceptions:

| Exception                   | Cause                                              |
| --------------------------- | -------------------------------------------------- |
| `AtlasQueryException`       | Invalid query structure                            |
| `AtlasSecurityException`    | Policy violation (forbidden field/operator)        |
| `AtlasCompilationException` | Failed to compile query (invalid field path, etc.) |

```csharp
try
{
    var result = await engine.ExecuteAsync(db, query, ct);
    return Results.Ok(result);
}
catch (AtlasSecurityException ex)
{
    return Results.Forbid();
}
catch (AtlasQueryException ex)
{
    return Results.BadRequest(new { error = ex.Message });
}
```

## Requirements

-   .NET 10.0+
-   Entity Framework Core 10.0+

## License

MIT
