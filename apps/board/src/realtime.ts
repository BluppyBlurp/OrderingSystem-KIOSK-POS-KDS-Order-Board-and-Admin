import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { useEffect, useRef, useState } from "react";
import { config, getDeviceToken } from "./config";

export type HubStatus = "connecting" | "live" | "offline";

/** The board group gets only {orderNumber, diningOption, type, tableNumber, status} (docs §9). */
const BOARD_EVENTS = ["OrderPaid", "OrderPreparing", "OrderReady", "OrderCompleted"];

/** Any board event refetches the board; so does a reconnect, since pushes sent while offline are gone. */
export function useBoardHub(onChange: () => void): HubStatus {
  const [status, setStatus] = useState<HubStatus>("connecting");
  const changeRef = useRef(onChange);
  changeRef.current = onChange;

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl(`${config.apiUrl}/hubs/orders`, { accessTokenFactory: () => getDeviceToken() ?? "" })
      // keep retrying forever: a wall screen has nobody to press reload
      .withAutomaticReconnect({ nextRetryDelayInMilliseconds: (ctx) => Math.min(30_000, 1000 * 2 ** ctx.previousRetryCount) })
      .configureLogging(LogLevel.Warning)
      .build();

    for (const event of BOARD_EVENTS) connection.on(event, () => changeRef.current());
    connection.onreconnecting(() => setStatus("connecting"));
    connection.onreconnected(() => {
      setStatus("live");
      changeRef.current();
    });

    let stopped = false;
    const start = (attempt: number) => {
      connection.start().then(
        () => !stopped && setStatus("live"),
        () => {
          if (stopped) return;
          setStatus("offline");
          setTimeout(() => start(attempt + 1), Math.min(30_000, 1000 * 2 ** attempt));
        },
      );
    };
    // automatic reconnect only covers drops after a successful start; after it gives up, start over
    connection.onclose(() => {
      if (stopped) return;
      setStatus("offline");
      start(0);
    });
    start(0);

    return () => {
      stopped = true;
      void connection.stop();
    };
  }, []);

  return status;
}
