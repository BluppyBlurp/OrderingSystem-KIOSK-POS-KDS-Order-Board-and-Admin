import { useEffect, useState } from "react";
import type { Order } from "../api";
import { money, whereLabel } from "./ui";

/** An order close to expiring is money about to walk out, so the countdown warms up as it runs down. */
const WARN_MS = 5 * 60_000;
const URGENT_MS = 2 * 60_000;

function countdownStyle(left: number, selected: boolean) {
  if (selected) return "text-paper";
  if (left <= URGENT_MS) return "text-brand";
  if (left <= WARN_MS) return "text-accent-deep";
  return "text-ink-soft";
}

/** Orders waiting for cash, oldest first, with how long until each one expires. */
export function PendingList({
  orders,
  loading,
  selectedId,
  onSelect,
}: {
  orders: Order[];
  loading: boolean;
  selectedId?: string;
  onSelect: (order: Order) => void;
}) {
  const now = useNow();

  return (
    <div className="min-h-0 flex-1 overflow-y-auto">
      <h2 className="sticky top-0 z-10 flex items-baseline gap-2 border-b-3 border-line bg-card px-4 py-2">
        <span className="display text-[length:var(--text-step-1)]">Waiting for cash</span>
        <span className="text-[length:var(--text-step-0)] tabular-nums text-ink-soft">{orders.length}</span>
      </h2>

      {loading && <p className="p-4 text-[length:var(--text-step-0)] text-ink-soft">Loading…</p>}
      {!loading && orders.length === 0 && (
        <p className="p-4 text-[length:var(--text-step-0)] text-ink-soft">Nobody waiting to pay.</p>
      )}

      <ul>
        {orders.map((order) => {
          const left = Math.max(0, new Date(order.expiresAt).getTime() - now);
          const selected = order.id === selectedId;
          return (
            <li key={order.id}>
              <button
                type="button"
                onClick={() => onSelect(order)}
                className={`press flex w-full items-center justify-between gap-3 border-b-2 border-paper-deep px-4 py-3 text-left ${
                  selected ? "bg-ink text-paper" : "bg-card"
                }`}
              >
                <span className="min-w-0">
                  <span className="display block text-[length:var(--text-step-2)] leading-tight">{order.orderNumber}</span>
                  <span className={`text-[length:var(--text-step-0)] ${selected ? "text-paper/75" : "text-ink-soft"}`}>
                    {whereLabel(order)}
                  </span>
                </span>
                <span className="shrink-0 text-right">
                  <span className="display block text-[length:var(--text-step-1)]">{money(order.total)}</span>
                  <span className={`text-[length:var(--text-step-0)] tabular-nums ${countdownStyle(left, selected)}`}>
                    {left > 0 ? `${formatLeft(left)} left` : "expiring"}
                  </span>
                </span>
              </button>
            </li>
          );
        })}
      </ul>
    </div>
  );
}

function formatLeft(ms: number) {
  const s = Math.floor(ms / 1000);
  return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, "0")}`;
}

function useNow() {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const id = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(id);
  }, []);
  return now;
}
