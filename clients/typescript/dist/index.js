export class AtlasHttpError extends Error {
    status;
    payload;
    constructor(message, status, payload) {
        super(message);
        this.name = "AtlasHttpError";
        this.status = status;
        this.payload = payload;
    }
}
export class AtlasClient {
    baseUrl;
    fetchImpl;
    headers;
    queryPath;
    entitiesPath;
    schemaPath;
    cacheStatsPath;
    constructor(options) {
        if (!options.baseUrl) {
            throw new Error("AtlasClient requires a baseUrl.");
        }
        const fetchImpl = options.fetch ?? globalThis.fetch;
        if (!fetchImpl) {
            throw new Error("AtlasClient requires a fetch implementation.");
        }
        this.baseUrl = options.baseUrl.replace(/\/+$/, "");
        this.fetchImpl = fetchImpl;
        this.headers = options.headers;
        this.queryPath = options.queryPath ?? "/api/query";
        this.entitiesPath = options.entitiesPath ?? "/api/entities";
        this.schemaPath = options.schemaPath ?? "/api/schema";
        this.cacheStatsPath = options.cacheStatsPath ?? "/api/cache/stats";
    }
    async query(query) {
        return this.request(this.queryPath, {
            method: "POST",
            body: JSON.stringify(query),
        });
    }
    async getEntities() {
        return this.request(this.entitiesPath, {
            method: "GET",
        });
    }
    async getSchema() {
        return this.request(this.schemaPath, {
            method: "GET",
        });
    }
    async getEntitySchema(entity) {
        return this.request(`${this.schemaPath}/${encodeURIComponent(entity)}`, {
            method: "GET",
        });
    }
    async getCacheStats() {
        return this.request(this.cacheStatsPath, {
            method: "GET",
        });
    }
    async request(path, init) {
        const response = await this.fetchImpl(this.toUrl(path), {
            ...init,
            headers: {
                "content-type": "application/json",
                ...this.headers,
                ...init.headers,
            },
        });
        const payload = await this.readPayload(response);
        if (!response.ok) {
            const errorMessage = this.resolveErrorMessage(payload, response.status);
            throw new AtlasHttpError(errorMessage, response.status, payload);
        }
        return payload;
    }
    toUrl(path) {
        return `${this.baseUrl}${path.startsWith("/") ? path : `/${path}`}`;
    }
    async readPayload(response) {
        const contentType = response.headers.get("content-type") ?? "";
        if (contentType.includes("application/json")) {
            return response.json();
        }
        const text = await response.text();
        return text.length > 0 ? text : undefined;
    }
    resolveErrorMessage(payload, status) {
        if (payload && typeof payload === "object" && "error" in payload) {
            const { error } = payload;
            if (typeof error === "string" && error.length > 0) {
                return error;
            }
        }
        if (typeof payload === "string" && payload.length > 0) {
            return payload;
        }
        return `Atlas request failed with status ${status}.`;
    }
}
export function createAtlasClient(options) {
    return new AtlasClient(options);
}
