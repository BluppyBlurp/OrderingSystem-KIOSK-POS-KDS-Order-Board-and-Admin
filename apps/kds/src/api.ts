import { createApiClient, unwrap, type Schemas } from "@kiosk/api-client";
import { useQuery } from "@tanstack/react-query";
import { config } from "./config";

export type Order = Schemas["OrderDto"];

/** Set once the cook is signed in (Clerk session token, or the dev token in development). */
let tokenSource: () => Promise<string | null> = async () => null;
export function setTokenSource(source: () => Promise<string | null>) {
  tokenSource = source;
}

export const api = createApiClient(config.apiUrl, () => tokenSource());

export const TICKETS_KEY = ["kds", "tickets"];

/** Paid, Preparing and Ready tickets. SignalR triggers refetches; the interval is the safety net. */
export function useTickets() {
  return useQuery({
    queryKey: TICKETS_KEY,
    queryFn: () => unwrap(api.GET("/api/kds/orders")),
    refetchInterval: 15_000,
  });
}

export type Step = "preparing" | "ready" | "complete";

export const advance = (id: string, step: Step) => {
  const params = { params: { path: { id } } };
  if (step === "preparing") return unwrap(api.POST("/api/kds/orders/{id}/preparing", params));
  if (step === "ready") return unwrap(api.POST("/api/kds/orders/{id}/ready", params));
  return unwrap(api.POST("/api/kds/orders/{id}/complete", params));
};
