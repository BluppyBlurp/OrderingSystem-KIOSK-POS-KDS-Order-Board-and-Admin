import { ApiError } from "@kiosk/api-client";
import { useRef, useState } from "react";
import { createOrder, payOrder, type KioskOrder, type PaymentMethod } from "../api";
import { Button, Modal, money, Screen } from "../components/ui";
import { cartTotal, toOrderLines } from "../lib/cart";
import { useKiosk } from "../store";
import { OrderSummaryBadge } from "./MenuScreen";

export function CheckoutScreen() {
  const { cart, diningOption, orderType, tableNumber, setPlacedOrder, go } = useKiosk();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<{ message: string; backToCart: boolean } | null>(null);
  // If paying fails after the order was created, retry with the same order instead of reserving stock twice.
  const created = useRef<KioskOrder | null>(null);

  const place = async (method: PaymentMethod) => {
    if (!diningOption || !orderType) return go("dining");
    setBusy(true);
    setError(null);
    try {
      created.current ??= await createOrder({ diningOption, orderType, tableNumber, items: toOrderLines(cart) });
      const paid = await payOrder(created.current.order.id, method);
      setPlacedOrder(paid, method === "Cash" ? "cashSlip" : "onlinePay");
    } catch (e) {
      const soldOut = e instanceof ApiError && e.status === 409 && !created.current;
      setError({
        message: e instanceof ApiError ? e.message : "We couldn't reach the counter system. Please try again.",
        backToCart: soldOut || (e instanceof ApiError && e.status === 400),
      });
    } finally {
      setBusy(false);
    }
  };

  return (
    <Screen title="How will you pay?" onBack={() => go("cart")} footer={<div className="flex justify-center"><OrderSummaryBadge /></div>}>
      <div className="flex flex-col items-center gap-8 p-8">
        <p className="text-5xl font-black">Total {money(cartTotal(cart))}</p>

        <Button size="xl" className="w-full max-w-3xl" disabled={busy} onClick={() => place("Cash")}>
          Pay at counter
          <span className="block text-xl normal-case">Cash — show your slip to the cashier</span>
        </Button>

        <div className="w-full max-w-3xl">
          <p className="mb-3 text-2xl font-black uppercase">Pay here</p>
          <div className="grid grid-cols-2 gap-6">
            <Button size="xl" disabled={busy} onClick={() => place("QrPh")}>
              QR Ph
              <span className="block text-xl normal-case">GCash, Maya or any bank app</span>
            </Button>
            <Button size="xl" disabled={busy} onClick={() => place("Card")}>
              Card
              <span className="block text-xl normal-case">Visa or Mastercard</span>
            </Button>
          </div>
        </div>
        {busy && <p className="text-2xl font-bold">Placing your order…</p>}
      </div>

      {error && (
        <Modal>
          <p className="text-3xl font-black">Sorry!</p>
          <p className="mt-4 text-2xl">{error.message}</p>
          <div className="mt-8 grid grid-cols-2 gap-4">
            {error.backToCart ? (
              <Button variant="solid" size="lg" className="col-span-2" onClick={() => go("cart")}>
                Back to my order
              </Button>
            ) : (
              <>
                <Button size="lg" onClick={() => go("cart")}>
                  Back
                </Button>
                <Button variant="solid" size="lg" onClick={() => setError(null)}>
                  Try again
                </Button>
              </>
            )}
          </div>
        </Modal>
      )}
    </Screen>
  );
}
