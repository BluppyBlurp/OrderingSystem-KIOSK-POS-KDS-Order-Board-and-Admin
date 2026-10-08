import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from "@microsoft/signalr";
import { useQueryClient } from "@tanstack/react-query";
import { useEffect } from "react";
import type { KioskOrder } from "./api";
import { config, getDeviceToken } from "./config";

/** Order events the API sends to kiosk-{orderId} once the kiosk has called WatchOrder (docs §9). */
const ORDER_EVENTS = [
  "OrderAwaitingPayment",
  "OrderPaymentPending",
  "OrderPaid",
  "OrderFailed",
  "OrderExpired",
  "OrderCancelled",
];

let connection: HubConnection | null = null;
let watched: string | null = null;

/**
 * One SignalR connection for the kiosk's lifetime. The API puts every kiosk in the `kiosks` group (MenuChanged)
 * and, after WatchOrder, in `kiosk-{orderId}` for the order being paid.
 *
 * Realtime is only a shortcut: the menu is still refetched for every new customer and the payment screen still
 * polls, so a dropped connection only costs speed. After a reconnect everything is refetched, because pushes sent
 * while offline are gone.
 */
export function useKioskHub() {
  const queryClient = useQueryClient();

  useEffect(() => {
    if (!getDeviceToken()) return;
    const hub = new HubConnectionBuilder()
      // Browsers can't set WebSocket headers, so the token travels as ?access_token= (the API reads it there).
      .withUrl(`${config.apiUrl}/hubs/orders`, { accessTokenFactory: () => getDeviceToken() ?? "" })
      .withAutomaticReconnect({ nextRetryDelayInMilliseconds: (ctx) => Math.min(30_000, 1000 * 2 ** ctx.previousRetryCount) })
      .configureLogging(LogLevel.Warning)
      .build();
    connection = hub;

    hub.on("MenuChanged", () => void queryClient.invalidateQueries({ queryKey: ["menu"] }));
    for (const event of ORDER_EVENTS) {
      // The pushed body is the OrderDto; the poll endpoint adds the slip token / checkout URL, so refetch that.
      hub.on(event, (order: KioskOrder["order"]) => void queryClient.invalidateQueries({ queryKey: ["order", order.id] }));
    }
    hub.onreconnected(() => {
      if (watched) void hub.invoke("WatchOrder", watched).catch(() => undefined);
      void queryClient.invalidateQueries({ queryKey: ["menu"] });
      void queryClient.invalidateQueries({ queryKey: ["order"] });
    });

    let stopped = false;
    // automatic reconnect only covers drops after a successful start, so retry the first start by hand
    const start = (attempt: number) => {
      hub.start().then(
        () => {
          if (watched) void hub.invoke("WatchOrder", watched).catch(() => undefined);
        },
        () => {
          if (!stopped) setTimeout(() => start(attempt + 1), Math.min(30_000, 1000 * 2 ** attempt));
        },
      );
    };
    start(0);

    return () => {
      stopped = true;
      connection = null;
      void hub.stop();
    };
  }, [queryClient]);
}

/** Subscribes to live status for the order on screen; the server only allows orders this kiosk created. */
export function useWatchOrder(orderId: string | undefined) {
  useEffect(() => {
    if (!orderId) return;
    watched = orderId;
    if (connection?.state === HubConnectionState.Connected) void connection.invoke("WatchOrder", orderId).catch(() => undefined);
    return () => {
      if (watched === orderId) watched = null;
    };
  }, [orderId]);
}
