import type { Menu } from "../api";
import type { CartLine } from "./cart";
import type { Product } from "./customize";

export interface UpsellGroup {
  kind: UpsellKind;
  prompt: string;
  categoryName: string;
  products: Product[];
}

type UpsellKind = "drink" | "side" | "dessert";

/**
 * What "covers" each kind: a product from a matching category, or a picked option in a matching question
 * (a meal's "Choose your drink" and "Upsize your fries?" already include a drink and a side).
 */
const KINDS: { kind: UpsellKind; prompt: string; words: string[] }[] = [
  { kind: "drink", prompt: "Something to drink?", words: ["drink"] },
  { kind: "side", prompt: "Add a side?", words: ["side", "fries"] },
  { kind: "dessert", prompt: "Something sweet?", words: ["dessert", "sundae"] },
];

const PER_KIND = 3;

const matches = (name: string, words: string[]) => words.some((w) => name.toLowerCase().includes(w));

/**
 * The "anything else?" prompt before checkout: for each kind the cart doesn't cover yet, a few of the cheapest
 * available products from that category. Empty when there's nothing worth asking.
 */
export function suggestUpsells(menu: Menu, cart: CartLine[]): UpsellGroup[] {
  const products = new Map(menu.categories.flatMap((c) => c.products.map((p) => [p.id, { product: p, category: c.name }] as const)));

  const covered = (words: string[]) =>
    cart.some((line) => {
      const entry = products.get(line.productId);
      if (!entry) return false;
      if (matches(entry.category, words)) return true;
      const picked = new Set(line.modifiers.map((m) => m.id));
      return entry.product.modifierGroups.some((g) => matches(g.name, words) && g.modifiers.some((m) => picked.has(m.id)));
    });

  return KINDS.flatMap(({ kind, prompt, words }) => {
    if (covered(words)) return [];
    const category = menu.categories.find((c) => matches(c.name, words));
    const choices = (category?.products ?? [])
      .filter((p) => !p.isSoldOut)
      .sort((a, b) => a.price - b.price)
      .slice(0, PER_KIND);
    return category && choices.length > 0 ? [{ kind, prompt, categoryName: category.name, products: choices }] : [];
  });
}
