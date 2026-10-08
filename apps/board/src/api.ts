import { createApiClient, unwrap, type Schemas } from "@kiosk/api-client";
import { useQuery } from "@tanstack/react-query";
import { config, getDeviceToken } from "./config";

export type Board = Schemas["BoardDto"];
export type BoardEntry = Schemas["BoardEntryDto"];

export const api = createApiClient(config.apiUrl, async () => getDeviceToken());

export const BOARD_KEY = ["board"];

/** SignalR triggers refetches; the interval keeps the screen right if the push is lost. */
export function useBoard() {
  return useQuery({
    queryKey: BOARD_KEY,
    queryFn: () => unwrap(api.GET("/api/display/board")),
    refetchInterval: 10_000,
  });
}

/** Asks the API whether a token is valid before the setup screen saves it. Allows for Render waking up. */
export async function verifyToken(token: string): Promise<"ok" | "rejected" | "unreachable"> {
  try {
    const res = await fetch(`${config.apiUrl}/api/display/board`, {
      headers: { Authorization: `Bearer ${token}` },
      signal: AbortSignal.timeout(90_000),
    });
    if (res.ok) return "ok";
    return res.status === 401 || res.status === 403 ? "rejected" : "unreachable";
  } catch {
    return "unreachable";
  }
}
