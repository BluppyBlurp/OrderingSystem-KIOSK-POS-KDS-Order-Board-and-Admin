import { describe, expect, it } from "vitest";
import type { Menu } from "../api";
import { makeLine } from "./cart";
import { initialSelection, type ModifierGroup, type Product } from "./customize";
import { suggestUpsells } from "./upsell";

const group = (name: string, min: number, options: string[]): ModifierGroup => ({
  id: name,
  name,
  minSelect: min,
  maxSelect: 1,
  isRequired: min > 0,
  modifiers: options.map((o) => ({ id: `${name}:${o}`, name: o, priceDelta: 0, isAvailable: true })),
});

const product = (id: string, price: number, groups: ModifierGroup[] = [], isSoldOut = false): Product => ({
  id,
  name: id,
  description: null,
  price,
  isSoldOut,
  media: [],
  modifierGroups: groups,
});

const meal = product("Chicken Meal", 189, [group("Choose your drink", 1, ["Coke"]), group("Upsize your fries?", 1, ["Regular fries"])]);
const burger = product("Burger", 99, [group("Add a drink?", 0, ["Coke"]), group("Add a side?", 0, ["Fries"])]);

const menu: Menu = {
  categories: [
    { id: "rice", name: "Rice Meals", products: [meal] },
    { id: "sandwiches", name: "Sandwiches", products: [burger] },
    { id: "sides", name: "Sides", products: [product("Fries", 60), product("Coleslaw", 35)] },
    { id: "drinks", name: "Drinks", products: [product("Iced Tea", 55), product("Coke", 45), product("Shake", 90, [], true)] },
    { id: "desserts", name: "Desserts", products: [product("Pie", 45), product("Sundae", 50)] },
  ],
};

const kinds = (cartProducts: [Product, Record<string, string[]>?][]) =>
  suggestUpsells(
    menu,
    cartProducts.map(([p, selection]) => makeLine(p, selection ?? initialSelection(p), 1)),
  ).map((g) => g.kind);

describe("upsell before checkout", () => {
  it("a meal already includes a drink and a side, so only dessert is suggested", () => {
    expect(kinds([[meal]])).toEqual(["dessert"]);
  });

  it("an à la carte burger with nothing added gets drink, side and dessert suggestions", () => {
    expect(kinds([[burger]])).toEqual(["drink", "side", "dessert"]);
  });

  it("an answered 'Add a drink?' question counts as having a drink", () => {
    expect(kinds([[burger, { "Add a drink?": ["Add a drink?:Coke"], "Add a side?": [] }]])).toEqual(["side", "dessert"]);
  });

  it("products from the category itself count, and nothing is asked when everything is covered", () => {
    expect(kinds([[meal], [product("Pie", 45)]])).toEqual([]);
  });

  it("suggests the cheapest available products and skips sold-out ones", () => {
    const drinks = suggestUpsells(menu, [makeLine(burger, initialSelection(burger), 1)]).find((g) => g.kind === "drink");
    expect(drinks?.products.map((p) => p.name)).toEqual(["Coke", "Iced Tea"]);
  });
});
