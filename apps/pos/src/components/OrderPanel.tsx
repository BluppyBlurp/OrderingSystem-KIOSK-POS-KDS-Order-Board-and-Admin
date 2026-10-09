import { ApiError } from "@kiosk/api-client";
import { useState } from "react";
import { cancelOrder, confirmCash, type CashConfirmation, type Order } from "../api";
import { parseAmount, pressKey, quickAmounts } from "../lib/cash";
import { CashReceipt, usePrintOnce } from "./Receipt";
import { Alert, Button, Modal, money, time, whereLabel } from "./ui";

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
  const short = typed !== "" && tendered < current.total;

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
    <div className="flex flex-col gap-5 p-4 sm:p-6 xl:flex-row">
      <section className="min-w-0 flex-1">
        <div className="flex flex-wrap items-baseline justify-between gap-x-4 gap-y-1">
          <p className="display text-[length:var(--text-step-5)] leading-none">{current.orderNumber}</p>
          <p className="text-[length:var(--text-step-1)] font-semibold">{whereLabel(current)}</p>
        </div>
        <p className="mt-2 text-[length:var(--text-step-0)] text-ink-soft">
          Ordered {time(current.createdAt)}
          {canPay && ` · pay before ${time(current.expiresAt)}`}
        </p>

        <ul className="mt-5 divide-y-2 divide-paper-deep border-y-3 border-line bg-card">
          {current.items.map((item, i) => (
            <li key={i} className="flex justify-between gap-4 px-4 py-3">
              <div className="min-w-0">
                <p className="flex gap-2 text-[length:var(--text-step-1)] font-semibold">
                  <span className="display shrink-0 bg-ink px-1.5 text-paper tabular-nums">{item.quantity}</span>
                  {item.name}
                </p>
                {item.modifiers.map((m, j) => (
                  <p key={j} className="pl-8 text-[length:var(--text-step-0)] text-ink-soft">
                    {m.name} {m.priceDelta ? `(${money(m.priceDelta)})` : ""}
                  </p>
                ))}
              </div>
              <p className="display shrink-0 text-[length:var(--text-step-1)]">{money(item.lineTotal)}</p>
            </li>
          ))}
        </ul>

        <div className="mt-4 flex items-baseline justify-between gap-4">
          <span className="display text-[length:var(--text-step-2)]">Total</span>
          <span className="display text-[length:var(--text-step-4)] leading-none">{money(current.total)}</span>
        </div>
        <p className="text-right text-[length:var(--text-step-0)] text-ink-soft">
          VAT included: {money(current.taxAmount)}
        </p>

        {!canPay && (
          <p className="mt-5 border-3 border-line bg-card p-4 text-[length:var(--text-step-1)]">
            {statusText[current.status] ?? current.status}
          </p>
        )}
        {canPay && (
          <Button variant="danger" className="mt-5" onClick={() => setCancelling(true)}>
            Void this order
          </Button>
        )}
      </section>

      {canPay && (
        <section className="w-full shrink-0 xl:w-96">
          <p className="mb-1 text-[length:var(--text-step-0)] font-semibold text-ink-soft">Cash received</p>
          <p
            className={`display mb-3 min-h-16 border-3 bg-card px-3 text-right text-[length:var(--text-step-4)] leading-[1.4] tabular-nums ${
              short ? "border-brand" : "border-line"
            }`}
          >
            {typed ? money(tendered) : <span className="text-ink-soft/40">—</span>}
          </p>

          <div className="mb-3 grid grid-cols-2 gap-2">
            {quickAmounts(current.total).map((amount, i) => (
              <Button key={amount} onClick={() => setTyped(String(amount))}>
                {i === 0 ? `Exact · ${money(amount)}` : money(amount)}
              </Button>
            ))}
          </div>

          <div className="grid grid-cols-3 gap-2">
            {["7", "8", "9", "4", "5", "6", "1", "2", "3", "00", "0", "."].map((key) => (
              <Button key={key} size="lg" className="display" onClick={() => setTyped((t) => pressKey(t, key))}>
                {key}
              </Button>
            ))}
            <Button size="lg" onClick={() => setTyped((t) => pressKey(t, "C"))}>
              Clear
            </Button>
            <Button size="lg" className="col-span-2" onClick={() => setTyped((t) => pressKey(t, "⌫"))} aria-label="Delete last digit">
              ⌫
            </Button>
          </div>

          {short && (
            <p className="mt-3 text-[length:var(--text-step-1)] font-semibold text-brand">
              Short by {money(current.total - tendered)}
            </p>
          )}
          {error && <div className="mt-3">{<Alert>{error}</Alert>}</div>}

          <Button variant="primary" size="lg" className="mt-3 w-full" disabled={busy || tendered < current.total} onClick={pay}>
            {busy ? "Confirming…" : "Take cash"}
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

/**
 * The one screen where a mistake hands over real money, so change due is the largest thing on it and
 * sits in the amber the rest of the system uses for "this is the thing to act on".
 */
function ChangeView({ result, cashier, onNext }: { result: CashConfirmation; cashier: string; onNext: () => void }) {
  usePrintOnce(`${result.order.id}:receipt`);
  return (
    <div className="flex flex-col items-center gap-5 p-6 text-center sm:p-10">
      <p className="display text-[length:var(--text-step-2)]">Paid · {result.order.orderNumber}</p>

      <div className="w-full max-w-2xl border-3 border-line bg-accent px-6 py-5">
        <p className="text-[length:var(--text-step-1)] font-semibold">Give change</p>
        <p className="display text-[length:var(--text-step-6)] leading-none tabular-nums">{money(result.changeDue)}</p>
      </div>

      <p className="text-[length:var(--text-step-1)] text-ink-soft">
        Received {money(result.amountTendered)} · total {money(result.order.total)}
      </p>
      <p className="text-[length:var(--text-step-0)] text-ink-soft">The order is now in the kitchen queue.</p>

      <div className="flex flex-wrap justify-center gap-3">
        <Button size="lg" onClick={() => window.print()}>
          Reprint receipt
        </Button>
        <Button variant="primary" size="lg" className="notch-sm" onClick={onNext}>
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
        <p className="display text-[length:var(--text-step-2)]">Void {order.orderNumber}?</p>
        <p className="mt-2 mb-4 text-[length:var(--text-step-0)] text-ink-soft">
          The items go back into stock. Pick a reason:
        </p>
        <div className="grid gap-2">
          {REASONS.map((reason) => (
            <Button key={reason} variant="danger" disabled={busy} onClick={() => cancel(reason)}>
              {reason}
            </Button>
          ))}
        </div>
        {error && <div className="mt-3">{<Alert>{error}</Alert>}</div>}
        <Button variant="primary" size="lg" className="mt-4 w-full" disabled={busy} onClick={onClose}>
          Keep the order
        </Button>
      </div>
    </Modal>
  );
}
