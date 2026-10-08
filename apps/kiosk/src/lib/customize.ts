import type { Schemas } from "@kiosk/api-client";

export type Product = Schemas["MenuProductDto"];
export type ModifierGroup = Schemas["MenuModifierGroupDto"];
export type Modifier = Schemas["MenuModifierDto"];

/** Selected modifier ids per modifier group id. */
export type Selection = Record<string, string[]>;

/**
 * Pre-answers questions that have an obvious default: a required single-choice group whose first option
 * costs nothing (e.g. "Upsize your fries?" → Regular). The customer can still change it.
 */
export function initialSelection(product: Product): Selection {
  const selection: Selection = {};
  for (const group of product.modifierGroups) {
    const first = group.modifiers.find((m) => m.isAvailable);
    const obviousDefault = group.isRequired && group.maxSelect === 1 && first !== undefined && first.priceDelta === 0;
    selection[group.id] = obviousDefault ? [first.id] : [];
  }
  return selection;
}

/** Single-choice groups swap the pick; multi-choice groups toggle it, up to the group's max. */
export function toggle(selection: Selection, group: ModifierGroup, modifierId: string): Selection {
  const current = selection[group.id] ?? [];
  let next: string[];
  if (group.maxSelect === 1) {
    next = current[0] === modifierId && !group.isRequired ? [] : [modifierId];
  } else if (current.includes(modifierId)) {
    next = current.filter((id) => id !== modifierId);
  } else if (current.length < group.maxSelect) {
    next = [...current, modifierId];
  } else {
    next = current;
  }
  return { ...selection, [group.id]: next };
}

/** Mirrors the server's rule (min already includes "required ⇒ at least 1"). */
export function isGroupValid(group: ModifierGroup, selection: Selection): boolean {
  const count = selection[group.id]?.length ?? 0;
  return count >= group.minSelect && count <= group.maxSelect;
}

export function selectedModifiers(product: Product, selection: Selection): Modifier[] {
  return product.modifierGroups.flatMap((g) => g.modifiers.filter((m) => selection[g.id]?.includes(m.id)));
}

/** Display price for one unit. The server recomputes the real price when the order is placed. */
export function unitPrice(product: Product, selection: Selection): number {
  return round2(product.price + selectedModifiers(product, selection).reduce((sum, m) => sum + m.priceDelta, 0));
}

/** "Pick 1", "Pick up to 6", "Optional — pick up to 2" … */
export function pickHint(group: ModifierGroup): string {
  if (group.minSelect === 0) return group.maxSelect === 1 ? "Optional" : `Optional — pick up to ${group.maxSelect}`;
  if (group.minSelect === group.maxSelect) return `Pick ${group.minSelect}`;
  return `Pick ${group.minSelect} to ${group.maxSelect}`;
}

export const round2 = (n: number) => Math.round(n * 100) / 100;
