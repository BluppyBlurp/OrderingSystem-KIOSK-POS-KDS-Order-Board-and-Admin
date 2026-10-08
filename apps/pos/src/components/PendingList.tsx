import { useEffect, useState } from "react";
import type { Order } from "../api";
import { money, whereLabel } from "./ui";

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
      <h2 className="sticky top-0 border-b-4 border-black bg-white px-4 py-2 text-sm font-bold uppercase">
        Waiting for cash ({orders.length})
      </h2>
      {loading && <p className="p-4">Loading…</p>}
      {!loading && orders.length === 0 && <p className="p-4">No orders waiting.</p>}
      <ul>
        {orders.map((order) => {
          const left = Math.max(0, new Date(order.expiresAt).getTime() - now);
          const selected = order.id === selectedId;
          return (
            <li key={order.id}>
              <button
                type="button"
                onClick={() => onSelect(order)}
                className={`flex w-full items-center justify-between border-b-2 border-black px-4 py-3 text-left ${selected ? "bg-black text-white" : "bg-white"}`}
              >
                <span>
                  <span className="block text-3xl font-black">{order.orderNumber}</span>
                  <span className="text-sm">{whereLabel(order)}</span>
                </span>
                <span className="text-right">
                  <span className="block text-xl font-bold">{money(order.total)}</span>
                  <span className="text-sm tabular-nums">{left > 0 ? `expires in ${formatLeft(left)}` : "expiring…"}</span>
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
