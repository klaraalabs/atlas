import { describe, expect, test } from "bun:test";
import { AtlasHttpError, createAtlasClient } from "../src/index.js";

function jsonResponse(payload: unknown, status = 200): Response {
    return new Response(JSON.stringify(payload), {
        status,
        headers: { "content-type": "application/json" },
    });
}

describe("AtlasClient", () => {
    test("sends queries to the expected URL with the body and custom headers", async () => {
        let requestedUrl: string | undefined;
        let requestedInit: RequestInit | undefined;
        const fetch = async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
            requestedUrl = input.toString();
            requestedInit = init;
            return jsonResponse({ data: [{ id: "user-1" }], offset: 0, limit: 1 });
        };
        const client = createAtlasClient({
            baseUrl: "https://atlas.example/",
            fetch,
            headers: { authorization: "Bearer token" },
        });
        const query = {
            entity: "users",
            select: ["id"],
            limit: 1,
        };

        await client.query(query);

        expect(requestedUrl).toBe("https://atlas.example/api/query");
        expect(requestedInit?.method).toBe("POST");
        expect(requestedInit?.body).toBe(JSON.stringify(query));
        const headers = new Headers(requestedInit?.headers);
        expect(headers.get("content-type")).toBe("application/json");
        expect(headers.get("authorization")).toBe("Bearer token");
    });

    test("normalizes the base URL and uses custom endpoint paths", async () => {
        const requestedUrls: string[] = [];
        const fetch = async (input: RequestInfo | URL): Promise<Response> => {
            requestedUrls.push(input.toString());
            return jsonResponse({});
        };
        const client = createAtlasClient({
            baseUrl: "https://atlas.example///",
            fetch,
            entitiesPath: "v1/entities",
            schemaPath: "/v1/schema",
            cacheStatsPath: "v1/cache",
        });

        await client.getEntities();
        await client.getEntitySchema("sales/orders");
        await client.getCacheStats();

        expect(requestedUrls).toEqual([
            "https://atlas.example/v1/entities",
            "https://atlas.example/v1/schema/sales%2Forders",
            "https://atlas.example/v1/cache",
        ]);
    });

    test("throws AtlasHttpError with a JSON error payload", async () => {
        const client = createAtlasClient({
            baseUrl: "https://atlas.example",
            fetch: async () => jsonResponse({ error: "Invalid query", code: "invalid_query" }, 400),
        });

        try {
            await client.getSchema();
            throw new Error("Expected getSchema to fail");
        } catch (error) {
            expect(error).toBeInstanceOf(AtlasHttpError);
            expect(error).toMatchObject({
                message: "Invalid query",
                status: 400,
                payload: { error: "Invalid query", code: "invalid_query" },
            });
        }
    });

    test("uses a text response as the HTTP error message", async () => {
        const client = createAtlasClient({
            baseUrl: "https://atlas.example",
            fetch: async () => new Response("Service unavailable", { status: 503 }),
        });

        await expect(client.getCacheStats()).rejects.toMatchObject({
            message: "Service unavailable",
            status: 503,
            payload: "Service unavailable",
        });
    });
});
