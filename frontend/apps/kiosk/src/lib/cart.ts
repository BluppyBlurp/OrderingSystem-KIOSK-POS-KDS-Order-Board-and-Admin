import type { Schemas } from "@kiosk/api-client";
import { round2, selectedModifiers, unitPrice, type Product, type Selection } from "./customize";

export interface CartLine {
  /** Local id so identical-looking lines can still be edited separately. */
  key: string;
  productId: string;
  name: string;
  modifiers: { id: string; name: string; priceDelta: number }[];
  unitPrice: number;
  quantity: number;
}

export const MAX_QUANTITY = 20;

export function makeLine(product: Product, selection: Selection, quantity: number): CartLine {
  return {
    key: crypto.randomUUID(),
    productId: product.id,
    name: product.name,
    modifiers: selectedModifiers(product, selection).map(({ id, name, priceDelta }) => ({ id, name, priceDelta })),
    unitPrice: unitPrice(product, selection),
    quantity,
  };
}

export const lineTotal = (line: CartLine) => round2(line.unitPrice * line.quantity);

export const cartTotal = (lines: CartLine[]) => round2(lines.reduce((sum, l) => sum + lineTotal(l), 0));

export const cartCount = (lines: CartLine[]) => lines.reduce((sum, l) => sum + l.quantity, 0);

export function setQuantity(lines: CartLine[], key: string, quantity: number): CartLine[] {
  if (quantity <= 0) return lines.filter((l) => l.key !== key);
  return lines.map((l) => (l.key === key ? { ...l, quantity: Math.min(quantity, MAX_QUANTITY) } : l));
}

/** Only ids and quantities go to the server — never prices. */
export function toOrderLines(lines: CartLine[]): Schemas["CreateOrderLine"][] {
  return lines.map((l) => ({ productId: l.productId, quantity: l.quantity, modifierIds: l.modifiers.map((m) => m.id), notes: null }));
}
