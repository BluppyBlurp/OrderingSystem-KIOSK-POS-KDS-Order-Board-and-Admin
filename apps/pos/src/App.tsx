import { ApiError } from "@kiosk/api-client";
import { useQueryClient } from "@tanstack/react-query";
import { useCallback, useState } from "react";
import { PENDING_KEY, setTokenSource, SHIFT_KEY, usePendingOrders, type Order } from "./api";
import { useSession } from "./auth";
import { BRAND } from "./brand";
import { OrderPanel } from "./components/OrderPanel";
import { PendingList } from "./components/PendingList";
import { ScanBox } from "./components/ScanBox";
import { ShiftSummaryPanel } from "./components/ShiftSummary";
import { PendingApproval } from "./components/PendingApproval";
import { Button, Mark } from "./components/ui";
import { POS_ROLES } from "./config";
import { usePosHub } from "./realtime";

export function App() {
  const session = useSession();
  setTokenSource(session.getToken); // before any query below runs

  if (!session.role) return <PendingApproval name={session.name} onRefresh={session.refresh} onSignOut={session.signOut} />;

  if (session.role && !POS_ROLES.includes(session.role)) {
    return (
      <NoAccess
        message={`Signed in as ${session.name} (${session.role}). The POS is for cashiers, managers and admins.`}
        onSignOut={session.signOut}
      />
    );
  }
  return <PosScreen />;
}

function PosScreen() {
  const session = useSession();
  const queryClient = useQueryClient();
  const pending = usePendingOrders();
  const [selected, setSelected] = useState<Order | null>(null);
  const [showShift, setShowShift] = useState(false);

  // Any payment or void (here or at another till) changes both the queue and the shift totals.
  const refresh = useCallback(() => {
    void queryClient.invalidateQueries({ queryKey: PENDING_KEY });
    void queryClient.invalidateQueries({ queryKey: SHIFT_KEY });
  }, [queryClient]);
  const hub = usePosHub(session.getToken, refresh);

  // Signed in with Clerk, but the API didn't see a POS role: the role claim isn't in the session token yet.
  if (pending.error instanceof ApiError && (pending.error.status === 401 || pending.error.status === 403)) {
    return (
      <NoAccess
        message={
          pending.error.status === 403
            ? `The API doesn't see a POS role for ${session.name}. In Clerk, set this user's public metadata to {"role": "cashier"} and add "role": "{{user.public_metadata.role}}" to the session token (docs.md §11).`
            : "The API didn't accept this sign-in. Sign out and in again."
        }
        onSignOut={session.signOut}
      />
    );
  }

  return (
    <div className="flex h-full flex-col bg-paper">
      <header className="flex min-h-14 flex-wrap items-center gap-x-5 gap-y-2 bg-brand px-4 py-2 text-paper">
        <Mark className="h-6 w-6 shrink-0" />
        <span className="display text-[length:var(--text-step-1)]">{BRAND.name} till</span>
        <span className="flex items-center gap-2 text-[length:var(--text-step-0)]">
          <span
            className={`inline-block size-3 rounded-full ${hub === "live" ? "bg-accent" : "border-2 border-paper/70"}`}
            aria-hidden="true"
          />
          {hub === "live" ? "Live" : hub === "connecting" ? "Connecting…" : "Offline — refreshing every 15s"}
        </span>
        <div className="ml-auto flex items-center gap-3">
          <span className="text-[length:var(--text-step-0)]">{session.name}</span>
          <Button onClick={() => setShowShift(true)}>Shift</Button>
          <Button onClick={session.signOut}>Sign out</Button>
        </div>
      </header>

      {/* The queue sits beside the sale on a till; it moves above it on a narrow screen. */}
      <div className="flex min-h-0 flex-1 flex-col lg:flex-row">
        <aside className="flex max-h-[45vh] shrink-0 flex-col border-b-3 border-line lg:max-h-none lg:w-96 lg:border-r-3 lg:border-b-0">
          <ScanBox onFound={setSelected} focusKey={selected?.id ?? "none"} />
          <PendingList
            orders={pending.data ?? []}
            loading={pending.isLoading}
            selectedId={selected?.id}
            onSelect={setSelected}
          />
        </aside>
        <main className="min-w-0 flex-1 overflow-y-auto">
          {selected ? (
            <OrderPanel
              key={selected.id}
              order={selected}
              cashier={session.name}
              onDone={() => {
                setSelected(null);
                refresh();
              }}
              onChanged={refresh}
            />
          ) : (
            <div className="flex h-full flex-col items-center justify-center gap-3 p-8 text-center">
              <p className="display text-[length:var(--text-step-2)]">Ready for the next customer</p>
              <p className="max-w-md text-[length:var(--text-step-1)] text-ink-soft">
                Scan their slip, type the order number, or pick one from the queue.
              </p>
            </div>
          )}
        </main>
      </div>
      {showShift && <ShiftSummaryPanel cashier={session.name} onClose={() => setShowShift(false)} />}
    </div>
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
