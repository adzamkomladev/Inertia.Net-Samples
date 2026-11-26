// A small stand-in for Laravel Wayfinder's generated route helpers.
// It keeps the same shape the demo pages use: callable route objects with .url() and .form()
// helpers, positional or named parameters, and query / mergeQuery options.
export type RouteMethod = 'get' | 'post' | 'put' | 'patch' | 'delete';

export type RouteResult = {
    url: string;
    method: RouteMethod;
};

export type FormResult = {
    action: string;
    method: RouteMethod;
};

export type QueryOptions = {
    query?: Record<string, unknown>;
    mergeQuery?: Record<string, unknown>;
};

export type RouteFn = ((...args: any[]) => RouteResult) & {
    url: (...args: any[]) => string;
    form: (...args: any[]) => FormResult;
};

type Param = {
    name: string;
    optional?: boolean;
};

const isObject = (value: unknown): value is Record<string, unknown> =>
    typeof value === 'object' && value !== null && !Array.isArray(value);

const isOptions = (value: unknown): value is QueryOptions =>
    isObject(value) && ('query' in value || 'mergeQuery' in value);

const appendQuery = (
    search: URLSearchParams,
    values: Record<string, unknown> | undefined,
) => {
    if (!values) return;

    for (const [key, value] of Object.entries(values)) {
        if (value === undefined) continue;

        if (value === null) {
            search.delete(key);
            continue;
        }

        if (Array.isArray(value)) {
            if (value.length === 0) {
                search.set(`${key}[]`, '');

                continue;
            }

            for (const item of value) {
                search.append(`${key}[]`, String(item));
            }

            continue;
        }

        search.set(key, String(value));
    }
};

const currentSearch = () => {
    if (typeof window === 'undefined') return new URLSearchParams();

    return new URLSearchParams(window.location.search);
};

const resolveParam = (
    param: Param,
    index: number,
    args: unknown[],
    named: Record<string, unknown> | null,
) => {
    if (named && param.name in named) return named[param.name];
    if (named && index === 0 && 'id' in named) return named.id;

    return args[index];
};

export const route = (
    method: RouteMethod,
    pattern: string,
    params: Param[] = [],
): RouteFn => {
    const build = (...args: any[]): RouteResult => {
        const options: QueryOptions | undefined = isOptions(args.at(-1))
            ? (args.at(-1) as QueryOptions)
            : undefined;
        const rest = options ? args.slice(0, -1) : args;
        const named =
            rest.length === 1 && isObject(rest[0]) && !isOptions(rest[0])
                ? rest[0]
                : null;
        const positional = named ? [] : rest;

        let url = pattern;

        for (const [index, param] of params.entries()) {
            const value = resolveParam(param, index, positional, named);

            if (value === undefined || value === null) {
                if (param.optional) {
                    url = url.replace(`/{${param.name}?}`, '');

                    continue;
                }

                throw new Error(
                    `Missing route parameter "${param.name}" for ${method.toUpperCase()} ${pattern}.`,
                );
            }

            const stringValue = isObject(value) && 'id' in value
                ? String(value.id)
                : String(value);

            url = url
                .replace(`/{${param.name}?}`, `/${encodeURIComponent(stringValue)}`)
                .replace(`/{${param.name}}`, `/${encodeURIComponent(stringValue)}`);
        }

        if (url.includes('{')) {
            url = url.replace(/\/\{[^}]+\}/g, '');
        }

        const search = options?.mergeQuery ? currentSearch() : new URLSearchParams();
        appendQuery(search, options?.mergeQuery);
        appendQuery(search, options?.query);

        const query = search.toString();

        return { url: query ? `${url}?${query}` : url, method };
    };

    build.url = (...args: any[]) => build(...args).url;
    build.form = (...args: any[]) => {
        const result = build(...args);

        return { action: result.url, method: result.method };
    };

    return build as RouteFn;
};
