import { ApiError } from "@kiosk/api-client";
import { useState } from "react";
import { cancelOrder, confirmCash, type CashConfirmation, type Order } from "../api";
import { parseAmount, pressKey, quickAmounts } from "../lib/cash";
import { CashReceipt, usePrintOnce } from "./Receipt";
import { Button, Modal, money, time, whereLabel } from "./ui";

const statusText: Record<string, string> = {
  Created: "The customer hasn't chosen how to pay yet.",
  PaymentPending: "The customer is paying online (QR Ph / card) at the kiosk.",
  Failed: "The online payment didn't go through. The customer can choose cash at the kiosk.",
  Paid: "Already paid. It's in the kitchen queue.",
  Preparing: "Paid. The kitchen is preparing it.",
  Ready: "Paid and ready for pickup.",
  Completed: "Paid and handed over.",
  Expired: "Expired: not paid in time. The customer needs to order again.",
  Cancelled: "Cancelled.",
};

export function OrderPanel({
  order,
  cashier,
  onDone,
  onChanged,
}: {
  order: Order;
  cashier: string;
  onDone: () => void;
  onChanged: () => void;
}) {
  const [typed, setTyped] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [result, setResult] = useState<CashConfirmation | null>(null);
  const [cancelling, setCancelling] = useState(false);
  const [current, setCurrent] = useState(order);

  if (result) return <ChangeView result={result} cashier={cashier} onNext={onDone} />;

  const tendered = parseAmount(typed);
  const canPay = current.status === "AwaitingPayment";

  const pay = async () => {
    setBusy(true);
    setError(null);
    try {
      const confirmed = await confirmCash(current.id, tendered);
      setResult(confirmed);
      onChanged();
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Couldn't reach the server. Try again.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="flex gap-6 p-6">
      <section className="min-w-0 flex-1">
        <div className="flex items-baseline justify-between gap-4">
          <p className="text-7xl font-black">{current.orderNumber}</p>
          <p className="text-xl font-bold uppercase">{whereLabel(current)}</p>
        </div>
        <p className="mt-1 text-lg">
          Ordered {time(current.createdAt)}
          {canPay && ` · pay before ${time(current.expiresAt)}`}
        </p>

        <ul className="mt-6 divide-y-2 divide-black border-y-4 border-black">
          {current.items.map((item, i) => (
            <li key={i} className="flex justify-between gap-4 py-3">
              <div>
                <p className="text-xl font-bold">
                  {item.quantity} × {item.name}
                </p>
                {item.modifiers.map((m, j) => (
                  <p key={j} className="pl-4">
                    – {m.name} {m.priceDelta ? `(${money(m.priceDelta)})` : ""}
                  </p>
                ))}
              </div>
              <p className="text-xl font-bold">{money(item.lineTotal)}</p>
            </li>
          ))}
        </ul>
        <div className="mt-4 flex justify-between text-3xl font-black">
          <span>Total</span>
          <span>{money(current.total)}</span>
        </div>
        <p className="text-right">VAT included: {money(current.taxAmount)}</p>

        {!canPay && <p className="mt-6 border-4 border-black p-4 text-xl font-bold">{statusText[current.status] ?? current.status}</p>}
        {canPay && (
          <Button className="mt-6" onClick={() => setCancelling(true)}>
            Cancel order
          </Button>
        )}
      </section>

      {canPay && (
        <section className="w-96 shrink-0">
          <p className="text-sm font-bold uppercase">Cash received</p>
          <p className="mb-3 min-h-20 border-4 border-black px-3 text-right text-6xl font-black tabular-nums">
            {typed ? money(tendered) : ""}
          </p>
          <div className="mb-3 grid grid-cols-2 gap-2">
            {quickAmounts(current.total).map((amount, i) => (
              <Button key={amount} onClick={() => setTyped(String(amount))}>
                {i === 0 ? `Exact ${money(amount)}` : money(amount)}
              </Button>
            ))}
          </div>
          <div className="grid grid-cols-3 gap-2">
            {["7", "8", "9", "4", "5", "6", "1", "2", "3", "00", "0", "."].map((key) => (
              <Button key={key} size="lg" onClick={() => setTyped((t) => pressKey(t, key))}>
                {key}
              </Button>
            ))}
            <Button size="lg" onClick={() => setTyped((t) => pressKey(t, "C"))}>
              C
            </Button>
            <Button size="lg" className="col-span-2" onClick={() => setTyped((t) => pressKey(t, "⌫"))}>
              ⌫
            </Button>
          </div>
          {typed && tendered < current.total && (
            <p className="mt-3 font-bold">Short by {money(current.total - tendered)}</p>
          )}
          {error && <p className="mt-3 border-4 border-black p-3 font-bold">{error}</p>}
          <Button variant="solid" size="lg" className="mt-3 w-full" disabled={busy || tendered < current.total} onClick={pay}>
            {busy ? "Confirming…" : "Confirm cash"}
          </Button>
        </section>
      )}

      {cancelling && (
        <CancelDialog
          order={current}
          onClose={() => setCancelling(false)}
          onCancelled={(updated) => {
            setCancelling(false);
            setCurrent(updated);
            onChanged();
          }}
        />
      )}
    </div>
  );
}

function ChangeView({ result, cashier, onNext }: { result: CashConfirmation; cashier: string; onNext: () => void }) {
  usePrintOnce(`${result.order.id}:receipt`);
  return (
    <div className="flex flex-col items-center gap-6 p-10 text-center">
      <p className="text-3xl font-bold uppercase">Paid · {result.order.orderNumber}</p>
      <p className="text-2xl">Give change</p>
      <p className="border-8 border-black px-10 py-4 text-9xl font-black tabular-nums">{money(result.changeDue)}</p>
      <p className="text-2xl">
        Received {money(result.amountTendered)} · Total {money(result.order.total)}
      </p>
      <p className="text-xl">The order is now in the kitchen queue.</p>
      <div className="flex gap-4">
        <Button size="lg" onClick={() => window.print()}>
          Reprint receipt
        </Button>
        <Button variant="solid" size="lg" onClick={onNext}>
          Next customer
        </Button>
      </div>
      <CashReceipt result={result} cashier={cashier} />
    </div>
  );
}

const REASONS = ["Customer left", "Wrong order", "Duplicate order", "Customer changed their mind"];

function CancelDialog({
  order,
  onClose,
  onCancelled,
}: {
  order: Order;
  onClose: () => void;
  onCancelled: (order: Order) => void;
}) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const cancel = async (reason: string) => {
    setBusy(true);
    setError(null);
    try {
      onCancelled(await cancelOrder(order.id, reason));
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Couldn't reach the server. Try again.");
      setBusy(false);
    }
  };

  return (
    <Modal>
      <div data-modal>
        <p className="text-2xl font-black">Cancel {order.orderNumber}?</p>
        <p className="mb-4">The items go back into stock. Pick a reason:</p>
        <div className="grid gap-2">
          {REASONS.map((reason) => (
            <Button key={reason} disabled={busy} onClick={() => cancel(reason)}>
              {reason}
            </Button>
          ))}
        </div>
        {error && <p className="mt-3 font-bold">{error}</p>}
        <Button variant="solid" className="mt-4 w-full" disabled={busy} onClick={onClose}>
          Keep the order
        </Button>
      </div>
    </Modal>
  );
}
