import { ApiError } from "@kiosk/api-client";
import { useQueryClient } from "@tanstack/react-query";
import { useCallback, useEffect, useRef, useState } from "react";
import { advance, setTokenSource, TICKETS_KEY, useTickets, type Order, type Step } from "./api";
import { useSession } from "./auth";
import { Button, whereLabel } from "./components/ui";
import { ALLOWED_ROLES } from "./config";
import { beep, soundEnabled, unlockSound } from "./lib/beep";
import { ageLevel, ageSeconds, formatAge, newIds } from "./lib/tickets";
import { useKitchenHub } from "./realtime";

export function App() {
  const session = useSession();
  setTokenSource(session.getToken); // before any query below runs

  if (session.role && !ALLOWED_ROLES.includes(session.role)) {
    return (
      <NoAccess
        message={`Signed in as ${session.name} (${session.role}). The kitchen display is for kitchen staff, managers and admins.`}
        onSignOut={session.signOut}
      />
    );
  }
  return <KitchenScreen />;
}

const COLUMNS: { status: Order["status"]; title: string; action: { step: Step; label: string } }[] = [
  { status: "Paid", title: "New", action: { step: "preparing", label: "Start" } },
  { status: "Preparing", title: "Preparing", action: { step: "ready", label: "Ready" } },
  { status: "Ready", title: "Ready", action: { step: "complete", label: "Handed over" } },
];

const FLASH_MS = 10_000;

function KitchenScreen() {
  const session = useSession();
  const queryClient = useQueryClient();
  const tickets = useTickets();
  const [now, setNow] = useState(() => Date.now());
  const [sound, setSound] = useState(soundEnabled);
  const [flashing, setFlashing] = useState<Set<string>>(new Set());
  const [error, setError] = useState<string | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);
  const known = useRef<Set<string> | null>(null);

  const refresh = useCallback(() => void queryClient.invalidateQueries({ queryKey: TICKETS_KEY }), [queryClient]);
  const hub = useKitchenHub(session.getToken, refresh, () => beep());

  // Age timers tick every second.
  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(timer);
  }, []);

  // Flash tickets that just arrived, whether the push or a poll brought them in.
  useEffect(() => {
    if (!tickets.data) return;
    const ids = tickets.data.filter((t) => t.status === "Paid").map((t) => t.id);
    const arrived = newIds(known.current, ids);
    known.current = new Set([...(known.current ?? []), ...ids]);
    if (arrived.length === 0) return;
    setFlashing((f) => new Set([...f, ...arrived]));
    // Not cleared on the next refetch: each batch of arrivals stops flashing on its own schedule.
    setTimeout(() => setFlashing((f) => new Set([...f].filter((id) => !arrived.includes(id)))), FLASH_MS);
  }, [tickets.data]);

  if (tickets.error instanceof ApiError && (tickets.error.status === 401 || tickets.error.status === 403)) {
    return (
      <NoAccess
        message={
          tickets.error.status === 403
            ? `The API doesn't see a kitchen role for ${session.name}. In Clerk, set this user's public metadata to {"role": "kitchen"} and add "role": "{{user.public_metadata.role}}" to the session token (docs.md §11).`
            : "The API didn't accept this sign-in. Sign out and in again."
        }
        onSignOut={session.signOut}
      />
    );
  }

  const move = async (order: Order, step: Step) => {
    setBusyId(order.id);
    setError(null);
    try {
      await advance(order.id, step);
    } catch (e) {
      setError(`${order.orderNumber}: ${e instanceof Error ? e.message : "could not update"}`);
    } finally {
      setBusyId(null);
      refresh();
    }
  };

  return (
    <div className="flex h-full flex-col">
      <header className="flex min-h-16 items-center justify-between gap-4 border-b-4 border-black px-4">
        <h1 className="text-2xl font-black uppercase">Kitchen</h1>
        <div className="flex items-center gap-4">
          <span className="flex items-center gap-2 text-sm font-bold uppercase" title="Live updates">
            <span className={`inline-block size-3 rounded-full border-2 border-black ${hub === "live" ? "bg-black" : "bg-white"}`} />
            {hub === "live" ? "Live" : hub === "connecting" ? "Connecting…" : "Offline (refreshing every 15 s)"}
          </span>
          {!sound && (
            <Button
              variant="solid"
              onClick={() => {
                setSound(unlockSound());
                beep();
              }}
            >
              Enable sound
            </Button>
          )}
          <span className="font-bold">{session.name}</span>
          <Button onClick={session.signOut}>Sign out</Button>
        </div>
      </header>

      {error && (
        <button type="button" className="border-b-4 border-black bg-black p-3 text-left text-lg font-bold text-white" onClick={() => setError(null)}>
          {error} (tap to dismiss)
        </button>
      )}

      <div className="grid min-h-0 flex-1 grid-cols-3">
        {COLUMNS.map((column) => {
          const list = (tickets.data ?? []).filter((t) => t.status === column.status);
          return (
            <section key={column.status} className="flex min-h-0 flex-col border-r-4 border-black last:border-r-0">
              <h2 className="border-b-4 border-black p-3 text-xl font-black uppercase">
                {column.title} ({list.length})
              </h2>
              <div className="flex flex-1 flex-col gap-3 overflow-y-auto p-3">
                {tickets.isLoading && <p className="text-lg">Loading…</p>}
                {list.map((order) => (
                  <Ticket
                    key={order.id}
                    order={order}
                    now={now}
                    flash={flashing.has(order.id)}
                    busy={busyId === order.id}
                    actionLabel={column.action.label}
                    onAction={() => void move(order, column.action.step)}
                  />
                ))}
              </div>
            </section>
          );
        })}
      </div>
    </div>
  );
}

const LEVEL_STYLE = {
  ok: "border-black",
  warn: "border-amber-500 bg-amber-50",
  late: "border-red-600 bg-red-50",
};

function Ticket({
  order,
  now,
  flash,
  busy,
  actionLabel,
  onAction,
}: {
  order: Order;
  now: number;
  flash: boolean;
  busy: boolean;
  actionLabel: string;
  onAction: () => void;
}) {
  const age = ageSeconds(order.paidAt, order.createdAt, now);
  const level = ageLevel(age);

  return (
    <article className={`border-4 ${LEVEL_STYLE[level]} ${flash ? "animate-pulse outline-8 outline-black" : ""}`}>
      <div className="flex items-baseline justify-between gap-2 border-b-4 border-inherit p-3">
        <span className="text-4xl font-black">{order.orderNumber}</span>
        <span className={`text-2xl font-black tabular-nums ${level === "late" ? "text-red-700" : ""}`}>{formatAge(age)}</span>
      </div>
      <p className={`px-3 pt-2 text-lg font-bold uppercase ${order.type === "ServeToTable" ? "text-2xl" : ""}`}>{whereLabel(order)}</p>
      <ul className="flex flex-col gap-2 p-3">
        {order.items.map((item, i) => (
          <li key={i}>
            <p className="text-xl font-bold">
              {item.quantity} × {item.name}
            </p>
            {item.modifiers.map((m, j) => (
              <p key={j} className="pl-6 text-lg">
                – {m.name}
              </p>
            ))}
            {item.notes && <p className="pl-6 text-lg font-bold italic">Note: {item.notes}</p>}
          </li>
        ))}
      </ul>
      <div className="p-3 pt-0">
        <Button variant="solid" size="lg" className="w-full" disabled={busy} onClick={onAction}>
          {busy ? "…" : actionLabel}
        </Button>
      </div>
    </article>
  );
}

function NoAccess({ message, onSignOut }: { message: string; onSignOut: () => void }) {
  return (
    <div className="flex h-full flex-col items-center justify-center gap-6 p-10 text-center">
      <p className="max-w-2xl text-2xl font-bold">{message}</p>
      <Button variant="solid" size="lg" onClick={onSignOut}>
        Sign out
      </Button>
    </div>
  );
}
