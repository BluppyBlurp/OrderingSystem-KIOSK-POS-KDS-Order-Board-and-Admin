import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { useEffect, useRef, useState } from "react";
import { config } from "./config";

export type HubStatus = "connecting" | "live" | "offline";

/** Events the API sends to the `pos` group (docs §9). Any of them can change the pending-cash queue. */
const POS_EVENTS = ["OrderAwaitingPayment", "OrderPaid", "OrderExpired", "OrderCancelled"];

/**
 * Keeps the POS live: new cash orders, payments from other tills, expiries and cancellations trigger `onChange`.
 * After a reconnect it also calls `onChange`, so anything missed while offline is refetched.
 */
export function usePosHub(getToken: () => Promise<string | null>, onChange: () => void): HubStatus {
  const [status, setStatus] = useState<HubStatus>("connecting");
  const tokenRef = useRef(getToken);
  const changeRef = useRef(onChange);
  tokenRef.current = getToken;
  changeRef.current = onChange;

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      // Browsers can't set WebSocket headers, so the token travels as ?access_token= (the API reads it there).
      .withUrl(`${config.apiUrl}/hubs/orders`, { accessTokenFactory: async () => (await tokenRef.current()) ?? "" })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    for (const event of POS_EVENTS) connection.on(event, () => changeRef.current());
    connection.onreconnecting(() => setStatus("connecting"));
    connection.onreconnected(() => {
      setStatus("live");
      changeRef.current();
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
