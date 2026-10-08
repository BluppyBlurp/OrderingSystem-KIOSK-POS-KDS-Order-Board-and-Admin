import { createApiClient, unwrap, type Schemas } from "@kiosk/api-client";
import { useQuery } from "@tanstack/react-query";
import { config, getDeviceToken } from "./config";

export const api = createApiClient(config.apiUrl, getDeviceToken);

export type Menu = Schemas["MenuDto"];
export type KioskOrder = Schemas["KioskOrderDto"];
export type Order = Schemas["OrderDto"];
export type PaymentMethod = Schemas["PayRequest"]["method"];

export function useMenu() {
  return useQuery({
    queryKey: ["menu"],
    queryFn: () => unwrap(api.GET("/api/kiosk/menu")),
    staleTime: 30_000,
  });
}

export const createOrder = (body: Schemas["CreateOrderRequest"]) =>
  unwrap(api.POST("/api/kiosk/orders", { body }));

export const payOrder = (id: string, method: PaymentMethod) =>
  unwrap(api.POST("/api/kiosk/orders/{id}/pay", { params: { path: { id } }, body: { method } }));

export const cancelCheckout = (id: string) =>
  unwrap(api.POST("/api/kiosk/orders/{id}/cancel-checkout", { params: { path: { id } } }));

export const getOrder = (id: string) =>
  unwrap(api.GET("/api/kiosk/orders/{id}", { params: { path: { id } } }));

export const getTableStatus = (tableNumber: number) =>
  unwrap(api.GET("/api/kiosk/tables/{tableNumber}", { params: { path: { tableNumber } } }));

/** Development only: completes the stubbed online payment as if PayMongo's webhook had arrived. */
export async function simulatePayment(id: string) {
  const res = await fetch(`${config.apiUrl}/api/dev/orders/${id}/simulate-payment`, {
    method: "POST",
    headers: { Authorization: `Bearer ${getDeviceToken() ?? ""}` },
  });
  if (!res.ok) throw new Error(`Simulated payment failed (${res.status})`);
}
