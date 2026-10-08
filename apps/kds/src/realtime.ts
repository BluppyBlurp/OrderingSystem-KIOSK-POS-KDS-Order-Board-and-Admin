import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { useEffect, useRef, useState } from "react";
import { config } from "./config";

export type HubStatus = "connecting" | "live" | "offline";

/** Events the API sends to the `kitchen` group (docs §9). */
const KITCHEN_EVENTS = ["OrderPaid", "OrderPreparing", "OrderReady", "OrderCompleted"];

/**
 * Keeps the KDS live: any kitchen event calls `onChange` (refetch), and a reconnect does too, because pushes sent
 * while offline are gone. `onNewOrder` fires for OrderPaid so the screen can beep.
 */
export function useKitchenHub(getToken: () => Promise<string | null>, onChange: () => void, onNewOrder: () => void): HubStatus {
  const [status, setStatus] = useState<HubStatus>("connecting");
  const refs = useRef({ getToken, onChange, onNewOrder });
  refs.current = { getToken, onChange, onNewOrder };

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      // Browsers can't set WebSocket headers, so the token travels as ?access_token= (the API reads it there).
      .withUrl(`${config.apiUrl}/hubs/orders`, { accessTokenFactory: async () => (await refs.current.getToken()) ?? "" })
      .withAutomaticReconnect({ nextRetryDelayInMilliseconds: (ctx) => Math.min(30_000, 1000 * 2 ** ctx.previousRetryCount) })
      .configureLogging(LogLevel.Warning)
      .build();

    for (const event of KITCHEN_EVENTS) connection.on(event, () => refs.current.onChange());
    connection.on("OrderPaid", () => refs.current.onNewOrder());
    connection.onreconnecting(() => setStatus("connecting"));
    connection.onreconnected(() => {
      setStatus("live");
      refs.current.onChange();
    });
    connection.onclose(() => setStatus("offline"));

    let stopped = false;
    connection
      .start()
      .then(() => !stopped && setStatus("live"))
      .catch(() => !stopped && setStatus("offline"));

    return () => {
      stopped = true;
      void connection.stop();
    };
  }, []);

  return status;
}
