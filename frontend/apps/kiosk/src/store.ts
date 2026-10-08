import { create } from "zustand";
import type { KioskOrder } from "./api";
import { setQuantity, type CartLine } from "./lib/cart";

export type DiningOption = "DineIn" | "TakeOut";
export type OrderType = "CounterPickup" | "ServeToTable";

/** The kiosk is one linear flow, so a screen enum replaces a router. */
export type Screen =
  | "attract"
  | "dining" // Dine in / Take out
  | "service" // (dine in) Pick up at counter / Serve to my table
  | "table" // table stand number keypad
  | "menu"
  | "customize"
  | "cart"
  | "checkout"
  | "cashSlip" // pay at counter: slip with QR + number (printed)
  | "onlinePay" // QR Ph / card: scan with phone, wait for payment
  | "receipt"; // paid: receipt (printed)

interface KioskState {
  screen: Screen;
  diningOption: DiningOption | null;
  orderType: OrderType | null;
  tableNumber: number | null;
  cart: CartLine[];
  customizingProductId: string | null;
  activeCategoryId: string | null;
  placedOrder: KioskOrder | null;

  go: (screen: Screen) => void;
  chooseDining: (option: DiningOption) => void;
  chooseService: (type: OrderType) => void;
  setTable: (table: number) => void;
  setCategory: (id: string) => void;
  customize: (productId: string) => void;
  addToCart: (line: CartLine) => void;
  setLineQuantity: (key: string, quantity: number) => void;
  setPlacedOrder: (order: KioskOrder, screen: Screen) => void;
  reset: () => void;
}

const initial = {
  screen: "attract" as Screen,
  diningOption: null,
  orderType: null,
  tableNumber: null,
  cart: [],
  customizingProductId: null,
  activeCategoryId: null,
  placedOrder: null,
};

export const useKiosk = create<KioskState>((set) => ({
  ...initial,
  go: (screen) => set({ screen }),
  // Take out is always counter pickup, so it skips the service question.
  chooseDining: (diningOption) =>
    set(
      diningOption === "TakeOut"
        ? { diningOption, orderType: "CounterPickup", tableNumber: null, screen: "menu" }
        : { diningOption, screen: "service" },
    ),
  chooseService: (orderType) =>
    set(orderType === "ServeToTable" ? { orderType, screen: "table" } : { orderType, tableNumber: null, screen: "menu" }),
  setTable: (tableNumber) => set({ tableNumber, screen: "menu" }),
  setCategory: (activeCategoryId) => set({ activeCategoryId }),
  customize: (customizingProductId) => set({ customizingProductId, screen: "customize" }),
  addToCart: (line) => set((s) => ({ cart: [...s.cart, line], customizingProductId: null, screen: "menu" })),
  setLineQuantity: (key, quantity) => set((s) => ({ cart: setQuantity(s.cart, key, quantity) })),
  setPlacedOrder: (placedOrder, screen) => set({ placedOrder, screen }),
  reset: () => set({ ...initial }),
}));
