export type AtlasPrimitive = string | number | boolean | null;

export type AtlasValue = AtlasPrimitive | AtlasValue[] | { [key: string]: AtlasValue };

export type AtlasCompareOperator = "eq" | "neq" | "gt" | "gte" | "lt" | "lte" | "contains" | "startswith" | "endswith" | "in" | "isnull" | "isnotnull";

export type AtlasLogicalOperator = "and" | "or" | "not";

export type AtlasCollectionOperator = "any" | "all" | "none";

export type AtlasWhereOperator = AtlasCompareOperator | AtlasLogicalOperator | AtlasCollectionOperator;

export interface AtlasWhereClause {
    op: AtlasWhereOperator;
    nodes?: AtlasWhereClause[];
    field?: string;
    type?: string;
    value?: AtlasValue;
    condition?: AtlasWhereClause;
}

export interface AtlasOrderByClause {
    field: string;
    direction?: "asc" | "desc";
}

export interface AtlasQuery {
    entity: string;
    select: string[];
    where?: AtlasWhereClause;
    groupBy?: string[];
    orderBy?: AtlasOrderByClause[];
    offset?: number;
    limit?: number;
    distinct?: boolean;
}

export interface AtlasQueryResult<TData = Record<string, unknown>> {
    data: TData[];
    offset: number;
    limit: number;
}

export interface AtlasEntitiesResponse {
    entities: string[];
}

export interface AtlasFieldSchema {
    path: string;
    type: string;
    isNullable: boolean;
    isCollection: boolean;
    isNavigation: boolean;
}

export interface AtlasEntitySchema {
    typeName: string;
    fields: AtlasFieldSchema[];
    allowedOperators: string[];
    maxLimit: number;
    defaultLimit: number;
    maxWhereDepth: number;
    maxSelectFields: number;
    maxNavigationDepth: number;
}

export interface AtlasSchema {
    entities: Record<string, AtlasEntitySchema>;
}

export interface AtlasCacheStats {
    hits: number;
    misses: number;
    size: number;
    hitRate: number;
}

export interface AtlasErrorResponse {
    error?: string;
}

export interface AtlasClientOptions {
    baseUrl: string;
    fetch?: typeof globalThis.fetch;
    headers?: HeadersInit;
    queryPath?: string;
    entitiesPath?: string;
    schemaPath?: string;
    cacheStatsPath?: string;
}

export class AtlasHttpError extends Error {
    readonly status: number;
    readonly payload?: unknown;

    constructor(message: string, status: number, payload?: unknown) {
        super(message);
        this.name = "AtlasHttpError";
        this.status = status;
        this.payload = payload;
    }
}

export class AtlasClient {
    private readonly baseUrl: string;
    private readonly fetchImpl: typeof globalThis.fetch;
    private readonly headers?: HeadersInit;
    private readonly queryPath: string;
    private readonly entitiesPath: string;
    private readonly schemaPath: string;
    private readonly cacheStatsPath: string;

    constructor(options: AtlasClientOptions) {
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

    async query<TData = Record<string, unknown>>(query: AtlasQuery): Promise<AtlasQueryResult<TData>> {
        return this.request<AtlasQueryResult<TData>>(this.queryPath, {
            method: "POST",
            body: JSON.stringify(query),
        });
    }

    async getEntities(): Promise<AtlasEntitiesResponse> {
        return this.request<AtlasEntitiesResponse>(this.entitiesPath, {
            method: "GET",
        });
    }

    async getSchema(): Promise<AtlasSchema> {
        return this.request<AtlasSchema>(this.schemaPath, {
            method: "GET",
        });
    }

    async getEntitySchema(entity: string): Promise<AtlasEntitySchema> {
        return this.request<AtlasEntitySchema>(`${this.schemaPath}/${encodeURIComponent(entity)}`, {
            method: "GET",
        });
    }

    async getCacheStats(): Promise<AtlasCacheStats> {
        return this.request<AtlasCacheStats>(this.cacheStatsPath, {
            method: "GET",
        });
    }

    private async request<T>(path: string, init: RequestInit): Promise<T> {
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

        return payload as T;
    }

    private toUrl(path: string): string {
        return `${this.baseUrl}${path.startsWith("/") ? path : `/${path}`}`;
    }

    private async readPayload(response: Response): Promise<unknown> {
        const contentType = response.headers.get("content-type") ?? "";
        if (contentType.includes("application/json")) {
            return response.json();
        }

        const text = await response.text();
        return text.length > 0 ? text : undefined;
    }

    private resolveErrorMessage(payload: unknown, status: number): string {
        if (payload && typeof payload === "object" && "error" in payload) {
            const { error } = payload as AtlasErrorResponse;
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

export function createAtlasClient(options: AtlasClientOptions): AtlasClient {
    return new AtlasClient(options);
}
