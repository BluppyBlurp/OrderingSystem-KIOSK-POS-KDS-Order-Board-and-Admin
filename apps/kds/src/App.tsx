import { ApiError } from "@kiosk/api-client";
import { useQueryClient } from "@tanstack/react-query";
import { useCallback, useEffect, useRef, useState } from "react";
import { advance, setTokenSource, TICKETS_KEY, useTickets, type Order, type Step } from "./api";
import { useSession } from "./auth";
import { BRAND } from "./brand";
import { PendingApproval } from "./components/PendingApproval";
import { Button, Mark, whereLabel } from "./components/ui";
import { ALLOWED_ROLES } from "./config";
import { beep, soundEnabled, unlockSound } from "./lib/beep";
import { ageLevel, ageSeconds, formatAge, newIds } from "./lib/tickets";
import { useKitchenHub } from "./realtime";

export function App() {
  const session = useSession();
  setTokenSource(session.getToken); // before any query below runs

  if (!session.role) return <PendingApproval name={session.name} onRefresh={session.refresh} onSignOut={session.signOut} />;

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
  { status: "Preparing", title: "Cooking", action: { step: "ready", label: "Ready" } },
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
    <div className="flex h-full flex-col bg-paper">
      <header className="flex min-h-14 flex-wrap items-center gap-x-5 gap-y-2 bg-brand px-4 py-2 text-paper">
        <Mark className="h-6 w-6 shrink-0" />
        <span className="display text-[length:var(--text-step-1)]">{BRAND.name} kitchen</span>

        <span className="flex items-center gap-2 text-[length:var(--text-step-0)]">
          <span
            className={`inline-block size-3 rounded-full ${hub === "live" ? "bg-accent" : "border-2 border-paper/70"}`}
            aria-hidden="true"
          />
          {hub === "live" ? "Live" : hub === "connecting" ? "Connecting…" : "Offline — refreshing every 15s"}
        </span>

        <div className="ml-auto flex items-center gap-3">
          {!sound && (
            <Button
              onClick={() => {
                setSound(unlockSound());
                beep();
              }}
            >
              Turn on sound
            </Button>
          )}
          <span className="text-[length:var(--text-step-0)]">{session.name}</span>
          <Button onClick={session.signOut}>Sign out</Button>
        </div>
      </header>

      {error && (
        <button
          type="button"
          className="border-b-3 border-line bg-brand-deep p-3 text-left text-[length:var(--text-step-1)] font-semibold text-paper"
          onClick={() => setError(null)}
        >
          {error} — tap to dismiss
        </button>
      )}

      {/* Three lanes side by side on a kitchen screen; stacked if someone opens this on a tablet. */}
      <div className="grid min-h-0 flex-1 grid-cols-1 md:grid-cols-3">
        {COLUMNS.map((column) => {
          const list = (tickets.data ?? []).filter((t) => t.status === column.status);
          return (
            <section key={column.status} className="flex min-h-0 flex-col border-line md:border-r-3 md:last:border-r-0">
              <h2 className="flex items-baseline gap-2 border-b-3 border-line bg-card px-4 py-2">
                <span className="display text-[length:var(--text-step-2)]">{column.title}</span>
                <span className="text-[length:var(--text-step-1)] text-ink-soft tabular-nums">{list.length}</span>
              </h2>
              <div className="flex flex-1 flex-col gap-3 overflow-y-auto p-3">
                {tickets.isLoading && <p className="text-[length:var(--text-step-1)] text-ink-soft">Loading…</p>}
                {!tickets.isLoading && list.length === 0 && (
                  <p className="p-4 text-center text-[length:var(--text-step-1)] text-ink-soft">Nothing here</p>
                )}
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

/**
 * Amber at five minutes, red at ten. Here these are status colours, not brand ones — which is why a
 * late ticket's button turns ink: a red button on a red frame stops reading as the thing to press.
 */
const LEVEL = {
  ok: { card: "border-line", head: "bg-card", timer: "text-ink", button: "" },
  warn: { card: "border-accent-deep", head: "bg-accent", timer: "text-ink", button: "" },
  late: {
    card: "border-brand",
    head: "bg-brand",
    timer: "text-paper",
    button: "bg-ink border-ink text-paper shadow-[inset_0_-5px_0_#000]",
  },
} as const;

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
  const skin = LEVEL[level];
  const toTable = order.type === "ServeToTable";

  return (
    <article className={`border-3 bg-card ${skin.card} ${flash ? "arrive" : ""}`}>
      <div className={`flex items-center justify-between gap-3 border-b-3 border-inherit px-3 py-2 ${skin.head}`}>
        <span className={`display text-[length:var(--text-step-3)] leading-none ${level === "late" ? "text-paper" : ""}`}>
          {order.orderNumber}
        </span>
        <span className={`display text-[length:var(--text-step-2)] leading-none tabular-nums ${skin.timer}`}>
          {formatAge(age)}
        </span>
      </div>

      {/* Where it goes, called out: a table number missed here means food walked to the wrong place. */}
      <p
        className={`px-3 py-2 font-semibold ${
          toTable
            ? "bg-paper-deep text-[length:var(--text-step-2)]"
            : "text-[length:var(--text-step-0)] text-ink-soft"
        }`}
      >
        {whereLabel(order)}
      </p>

      <ul className="flex flex-col gap-2.5 px-3 py-3">
        {order.items.map((item, i) => (
          <li key={i}>
            <p className="flex gap-2 text-[length:var(--text-step-1)] font-semibold">
              <span className="display shrink-0 bg-ink px-1.5 text-paper tabular-nums">{item.quantity}</span>
              {item.name}
            </p>
            {item.modifiers.map((m, j) => (
              <p key={j} className="pl-8 text-[length:var(--text-step-0)] text-ink-soft">
                {m.name}
              </p>
            ))}
            {item.notes && (
              <p className="mt-1 ml-8 border-l-4 border-brand bg-paper px-2 py-1 text-[length:var(--text-step-0)] font-semibold">
                {item.notes}
              </p>
            )}
          </li>
        ))}
      </ul>

      <div className="p-3 pt-0">
        <Button variant="primary" size="lg" className={`w-full ${skin.button}`} disabled={busy} onClick={onAction}>
          {busy ? "…" : actionLabel}
        </Button>
      </div>
    </article>
  );
}

function NoAccess({ message, onSignOut }: { message: string; onSignOut: () => void }) {
  return (
    <div className="flex h-full flex-col items-center justify-center gap-6 bg-paper p-10 text-center">
      <p className="max-w-2xl text-[length:var(--text-step-1)] font-semibold">{message}</p>
      <Button variant="primary" size="lg" className="notch-sm" onClick={onSignOut}>
        Sign out
      </Button>
    </div>
  );
}
