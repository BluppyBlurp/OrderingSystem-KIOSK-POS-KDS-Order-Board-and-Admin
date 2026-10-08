import { ApiError } from "@kiosk/api-client";
import { useQueryClient } from "@tanstack/react-query";
import { useCallback, useState } from "react";
import { PENDING_KEY, setTokenSource, SHIFT_KEY, usePendingOrders, type Order } from "./api";
import { useSession } from "./auth";
import { OrderPanel } from "./components/OrderPanel";
import { PendingList } from "./components/PendingList";
import { ScanBox } from "./components/ScanBox";
import { ShiftSummaryPanel } from "./components/ShiftSummary";
import { PendingApproval } from "./components/PendingApproval";
import { Button } from "./components/ui";
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
    <div className="flex h-full flex-col">
      <header className="flex min-h-16 items-center justify-between gap-4 border-b-4 border-black px-4">
        <h1 className="text-2xl font-black uppercase">Cashier POS</h1>
        <div className="flex items-center gap-4">
          <span className="flex items-center gap-2 text-sm font-bold uppercase" title="Live updates">
            <span className={`inline-block size-3 rounded-full border-2 border-black ${hub === "live" ? "bg-black" : "bg-white"}`} />
            {hub === "live" ? "Live" : hub === "connecting" ? "Connecting…" : "Offline (refreshing every 15 s)"}
          </span>
          <span className="font-bold">{session.name}</span>
          <Button onClick={() => setShowShift(true)}>Shift</Button>
          <Button onClick={session.signOut}>Sign out</Button>
        </div>
      </header>

      <div className="flex min-h-0 flex-1">
        <aside className="flex w-96 shrink-0 flex-col border-r-4 border-black">
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
            <p className="p-10 text-center text-2xl">Scan the customer's slip, type the order number, or pick an order from the list.</p>
          )}
        </main>
      </div>
      {showShift && <ShiftSummaryPanel cashier={session.name} onClose={() => setShowShift(false)} />}
    </div>
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
