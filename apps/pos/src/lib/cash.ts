/** Cash-entry helpers. The server computes the real change; these only drive the keypad and quick buttons. */

const round2 = (n: number) => Math.round(n * 100) / 100;

/**
 * Buttons a cashier taps most: the exact amount, then the next round amounts a customer is likely to hand over
 * (e.g. ₱285 → 285, 300, 500, 1000).
 */
export function quickAmounts(total: number): number[] {
  const amounts = new Set<number>([round2(total)]);
  for (const step of [50, 100, 500, 1000]) amounts.add(Math.ceil(total / step) * step);
  return [...amounts].filter((a) => a >= total).sort((a, b) => a - b).slice(0, 5);
}

const MAX_DIGITS = 7; // up to ₱9,999,999

/** Applies one keypad press to the typed amount ("1", "0", "00", ".", "⌫", "C"). */
export function pressKey(current: string, key: string): string {
  if (key === "C") return "";
  if (key === "⌫") return current.slice(0, -1);
  if (key === ".") return current.includes(".") ? current : (current || "0") + ".";

  const next = current === "0" ? key.replace(/^0+/, "") || "0" : current + key;
  const [whole = "", cents] = next.split(".");
  if (cents !== undefined && cents.length > 2) return current; // centavos: 2 places max
  if (whole.length > MAX_DIGITS) return current;
  return next;
}

export const parseAmount = (typed: string): number => (typed === "" || typed === "." ? 0 : Number(typed));
