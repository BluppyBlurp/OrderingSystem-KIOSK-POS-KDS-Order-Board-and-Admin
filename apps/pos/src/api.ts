import { createApiClient, unwrap, type Schemas } from "@kiosk/api-client";
import { useQuery } from "@tanstack/react-query";
import { config } from "./config";

export type Order = Schemas["OrderDto"];
export type CashConfirmation = Schemas["CashConfirmationDto"];

/** Set once the cashier is signed in (Clerk session token, or the dev token in development). */
let tokenSource: () => Promise<string | null> = async () => null;
export function setTokenSource(source: () => Promise<string | null>) {
  tokenSource = source;
}

export const api = createApiClient(config.apiUrl, () => tokenSource());

export const PENDING_KEY = ["pos", "pending"];

/** Live queue of orders waiting for cash. SignalR triggers refetches; the interval is the safety net. */
export function usePendingOrders() {
  return useQuery({
    queryKey: PENDING_KEY,
    queryFn: () => unwrap(api.GET("/api/pos/orders")),
    refetchInterval: 15_000,
  });
}

/** A scanned slip QR token or a typed order number ("A-101", "101"). */
export const lookupOrder = (code: string) =>
  unwrap(api.GET("/api/pos/orders/lookup", { params: { query: { code } } }));

export const confirmCash = (id: string, amountTendered: number) =>
  unwrap(api.POST("/api/pos/orders/{id}/confirm-cash", { params: { path: { id } }, body: { amountTendered } }));

export const cancelOrder = (id: string, reason: string) =>
  unwrap(api.POST("/api/pos/orders/{id}/cancel", { params: { path: { id } }, body: { reason } }));
