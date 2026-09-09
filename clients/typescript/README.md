# Atlas TypeScript Client

Small TypeScript client for Atlas HTTP endpoints.

## Install

```bash
bun install
```

## Build

```bash
bun run build
```

## Usage

```ts
import { createAtlasClient } from "@klara-labs/atlas-client";

const client = createAtlasClient({
    baseUrl: "https://localhost:5001",
});

const result = await client.query<{ id: string; name: string }>({
    entity: "users",
    select: ["id", "name"],
    where: {
        op: "eq",
        field: "isActive",
        value: true,
    },
    orderBy: [{ field: "name", direction: "asc" }],
    limit: 10,
});

const schema = await client.getSchema();
const entities = await client.getEntities();
const userSchema = await client.getEntitySchema("users");
const cacheStats = await client.getCacheStats();

console.log(result.data, schema, entities, userSchema, cacheStats);
```

## API

- `query(query)` posts to `/api/query`
- `getEntities()` reads `/api/entities`
- `getSchema()` reads `/api/schema`
- `getEntitySchema(entity)` reads `/api/schema/{entity}`
- `getCacheStats()` reads `/api/cache/stats`

Pass a custom `fetch` implementation if you are targeting an older Node runtime.
