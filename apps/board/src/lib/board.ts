import type { BoardEntry } from "../api";

/** Order numbers that are on "Now serving" now but weren't last time: these get the chime and a highlight. */
export function newlyReady(previous: Set<string> | null, ready: BoardEntry[]): string[] {
  if (previous === null) return []; // first load after a reload: don't chime for the whole list
  return ready.map((e) => e.orderNumber).filter((n) => !previous.has(n));
}

/** "A-101" → "101": the letter prefix is the same all day, the digits are what people look for. */
export const shortNumber = (orderNumber: string) => orderNumber.replace(/^[A-Z]+-/, "");
