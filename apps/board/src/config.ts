const TOKEN_KEY = "board.deviceToken";

export const config = {
  apiUrl: import.meta.env.VITE_API_URL ?? "http://localhost:5202",
};

/** The board's device token, entered once on the setup screen (registered in Admin → Devices as a Board). */
export function getDeviceToken(): string | null {
  try {
    return localStorage.getItem(TOKEN_KEY) ?? import.meta.env.VITE_DEV_DEVICE_TOKEN ?? null;
  } catch {
    return import.meta.env.VITE_DEV_DEVICE_TOKEN ?? null;
  }
}

/** Pasted tokens often pick up spaces, line breaks or quotes; real tokens never contain any of those. */
export const cleanToken = (input: string) => input.replace(/[\s"'`]/g, "");

export function saveDeviceToken(token: string) {
  try {
    localStorage.setItem(TOKEN_KEY, cleanToken(token));
  } catch {
    /* storage blocked: the token lasts until reload */
  }
}

export function clearDeviceToken() {
  try {
    localStorage.removeItem(TOKEN_KEY);
  } catch {
    /* ignore */
  }
}
