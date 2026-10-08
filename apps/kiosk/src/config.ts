const TOKEN_KEY = "kiosk.deviceToken";

export const config = {
  apiUrl: import.meta.env.VITE_API_URL ?? "http://localhost:5202",
  tableMin: Number(import.meta.env.VITE_TABLE_MIN ?? 1),
  tableMax: Number(import.meta.env.VITE_TABLE_MAX ?? 60),
  isDev: import.meta.env.DEV,
};

/** The kiosk's device token: entered once on the setup screen; in dev, falls back to the seeded dev token. */
export function getDeviceToken(): string | null {
  try {
    return localStorage.getItem(TOKEN_KEY) ?? import.meta.env.VITE_DEV_DEVICE_TOKEN ?? null;
  } catch {
    return import.meta.env.VITE_DEV_DEVICE_TOKEN ?? null;
  }
}

/** Pasted tokens often pick up spaces, line breaks or quotes; real tokens never contain any of those. */
export function cleanToken(input: string): string {
  return input.replace(/[\s"'`]/g, "");
}

/** Last 4 characters of the saved token, so staff can compare it with the registered one without showing it all. */
export function savedTokenHint(): string | null {
  try {
    return localStorage.getItem(TOKEN_KEY)?.slice(-4) ?? null;
  } catch {
    return null;
  }
}

export function saveDeviceToken(token: string) {
  try {
    localStorage.setItem(TOKEN_KEY, cleanToken(token));
  } catch {
    /* storage blocked: the token lasts until reload */
  }
}
