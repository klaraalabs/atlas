export type AtlasPrimitive = string | number | boolean | null;
export type AtlasValue = AtlasPrimitive | AtlasValue[] | {
    [key: string]: AtlasValue;
};
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
export declare class AtlasHttpError extends Error {
    readonly status: number;
    readonly payload?: unknown;
    constructor(message: string, status: number, payload?: unknown);
}
export declare class AtlasClient {
    private readonly baseUrl;
    private readonly fetchImpl;
    private readonly headers?;
    private readonly queryPath;
    private readonly entitiesPath;
    private readonly schemaPath;
    private readonly cacheStatsPath;
    constructor(options: AtlasClientOptions);
    query<TData = Record<string, unknown>>(query: AtlasQuery): Promise<AtlasQueryResult<TData>>;
    getEntities(): Promise<AtlasEntitiesResponse>;
    getSchema(): Promise<AtlasSchema>;
    getEntitySchema(entity: string): Promise<AtlasEntitySchema>;
    getCacheStats(): Promise<AtlasCacheStats>;
    private request;
    private toUrl;
    private readPayload;
    private resolveErrorMessage;
}
export declare function createAtlasClient(options: AtlasClientOptions): AtlasClient;
//# sourceMappingURL=index.d.ts.map