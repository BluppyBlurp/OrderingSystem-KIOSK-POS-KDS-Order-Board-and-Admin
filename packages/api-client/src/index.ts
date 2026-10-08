import createClient from "openapi-fetch";
import type { components, paths } from "./schema";

export type { paths };
export type Schemas = components["schemas"];

/** RFC 7807 body the API returns for every handled error. `code` is set for business-rule violations. */
export interface Problem {
  title?: string;
  status?: number;
  code?: string;
  errors?: Record<string, string[]>;
}

export class ApiError extends Error {
  readonly status: number;
  readonly code?: string;
  readonly problem: Problem;

  constructor(status: number, problem: Problem) {
    const firstFieldError = problem.errors ? Object.values(problem.errors)[0]?.[0] : undefined;
    super(firstFieldError ?? problem.title ?? `Request failed (${status})`);
    this.status = status;
    this.code = problem.code;
    this.problem = problem;
  }
}

/**
 * Typed client for the Kiosk API. Every request carries `Authorization: Bearer <token>` when a token exists.
 * `getToken` may be async (Clerk refreshes session tokens on demand).
 */
export function createApiClient(baseUrl: string, getToken: () => string | null | Promise<string | null>) {
  const client = createClient<paths>({ baseUrl });
  client.use({
    async onRequest({ request }) {
      const token = await getToken();
      if (token) request.headers.set("Authorization", `Bearer ${token}`);
      return request;
    },
  });
  return client;
}

export type ApiClient = ReturnType<typeof createApiClient>;

/** Turns openapi-fetch's `{ data, error, response }` into data-or-throw. */
export async function unwrap<T>(call: Promise<{ data?: T; error?: unknown; response: Response }>): Promise<T> {
  const { data, error, response } = await call;
  if (!response.ok || data === undefined) {
    throw new ApiError(response.status, (error ?? {}) as Problem);
  }
  return data;
}
