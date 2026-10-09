import { ApiError } from "@kiosk/api-client";
import { useRef, useState } from "react";
import { createOrder, payOrder, type KioskOrder, type PaymentMethod } from "../api";
import { Button, Modal, money, Screen } from "../components/ui";
import { cartTotal, toOrderLines } from "../lib/cart";
import { useKiosk } from "../store";
import { OrderSummaryBadge } from "./MenuScreen";

const line = { stroke: "currentColor", strokeWidth: 3, fill: "none", strokeLinejoin: "round" as const };

function CashIcon() {
  return (
    <svg viewBox="0 0 48 32" className="h-9 w-14" aria-hidden="true">
      <rect x="2" y="4" width="44" height="24" rx="3" {...line} />
      <circle cx="24" cy="16" r="6" {...line} />
    </svg>
  );
}

function PhoneIcon() {
  return (
    <svg viewBox="0 0 32 44" className="h-11 w-8" aria-hidden="true">
      <rect x="2" y="2" width="28" height="40" rx="4" {...line} />
      <path d="M12 36 L20 36" {...line} />
      <rect x="9" y="11" width="14" height="14" {...line} />
    </svg>
  );
}

function CardIcon() {
  return (
    <svg viewBox="0 0 48 32" className="h-9 w-14" aria-hidden="true">
      <rect x="2" y="4" width="44" height="24" rx="3" {...line} />
      <path d="M2 12 L46 12" {...line} />
      <path d="M9 21 L19 21" {...line} />
    </svg>
  );
}

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
    <Screen title="How will you pay?" onBack={() => go("cart")} badge={<OrderSummaryBadge />}>
      <div className="mx-auto flex max-w-4xl flex-col gap-6 p-5 sm:p-8">
        <p className="text-center">
          <span className="block text-[length:var(--text-step-1)] text-ink-soft">Amount due</span>
          <span className="display text-[length:var(--text-step-5)] leading-none">
            {money(cartTotal(cart))}
          </span>
        </p>

        <Button
          variant="choice"
          size="xl"
          className="w-full flex-row items-center justify-start gap-5 text-left"
          disabled={busy}
          onClick={() => place("Cash")}
        >
          <CashIcon />
          <span>
            Pay at the counter
            <span className="block text-[length:var(--text-step-0)] font-normal text-ink-soft">
              Take your printed slip to the cashier
            </span>
          </span>
        </Button>

        <div className="grid gap-4 sm:grid-cols-2">
          <Button
            variant="choice"
            size="xl"
            className="flex-row items-center justify-start gap-5 text-left"
            disabled={busy}
            onClick={() => place("QrPh")}
          >
            <PhoneIcon />
            <span>
              QR Ph
              <span className="block text-[length:var(--text-step-0)] font-normal text-ink-soft">
                GCash, Maya or your bank app
              </span>
            </span>
          </Button>
          <Button
            variant="choice"
            size="xl"
            className="flex-row items-center justify-start gap-5 text-left"
            disabled={busy}
            onClick={() => place("Card")}
          >
            <CardIcon />
            <span>
              Card
              <span className="block text-[length:var(--text-step-0)] font-normal text-ink-soft">Visa or Mastercard</span>
            </span>
          </Button>
        </div>

        {busy && <p className="text-center text-[length:var(--text-step-1)] font-semibold">Placing your order…</p>}
      </div>

      {error && (
        <Modal>
          <p className="display text-[length:var(--text-step-3)]">That didn't go through</p>
          <p className="mt-3 text-[length:var(--text-step-1)] text-ink-soft">{error.message}</p>
          <div className="mt-7 grid grid-cols-2 gap-3">
            {error.backToCart ? (
              <Button variant="primary" size="lg" className="col-span-2" onClick={() => go("cart")}>
                Back to my tray
              </Button>
            ) : (
              <>
                <Button size="lg" onClick={() => go("cart")}>
                  Back to my tray
                </Button>
                <Button variant="primary" size="lg" onClick={() => setError(null)}>
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
