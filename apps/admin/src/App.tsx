import { ApiError } from "@kiosk/api-client";
import { useState } from "react";
import { setTokenSource, useCategories } from "./api";
import { useSession } from "./auth";
import { BRAND } from "./brand";
import { PendingApproval } from "./components/PendingApproval";
import { Button, Mark } from "./components/ui";
import { ALLOWED_ROLES, MANAGER_ROLES, ROLE_LABELS } from "./config";
import { DevicesPage } from "./pages/DevicesPage";
import { MenuPage } from "./pages/MenuPage";
import { ModifiersPage } from "./pages/ModifiersPage";
import { ReportsPage } from "./pages/ReportsPage";
import { StaffPage } from "./pages/StaffPage";

/** Assistant managers see the menu (availability and stock only) and reports; the rest is for managers and admins. */
const TABS = [
  { id: "menu", label: "Menu", managersOnly: false },
  { id: "modifiers", label: "Options", managersOnly: true },
  { id: "devices", label: "Devices", managersOnly: true },
  { id: "staff", label: "Staff", managersOnly: true },
  { id: "reports", label: "Reports", managersOnly: false },
] as const;

type Tab = (typeof TABS)[number]["id"];

export function App() {
  const session = useSession();
  setTokenSource(session.getToken); // before any query below runs

  if (!session.role) return <PendingApproval name={session.name} onRefresh={session.refresh} onSignOut={session.signOut} />;

  if (session.role && !ALLOWED_ROLES.includes(session.role)) {
    return (
      <NoAccess
        message={`Signed in as ${session.name} (${session.role}). The admin app is for managers and admins.`}
        onSignOut={session.signOut}
      />
    );
  }
  return <AdminScreen />;
}

function AdminScreen() {
  const session = useSession();
  const [tab, setTab] = useState<Tab>("menu");
  const isManager = MANAGER_ROLES.includes(session.role ?? "");
  const tabs = TABS.filter((t) => isManager || !t.managersOnly);
  const probe = useCategories(); // also tells us early whether the API accepts this sign-in

  if (probe.error instanceof ApiError && (probe.error.status === 401 || probe.error.status === 403)) {
    return (
      <NoAccess
        message={
          probe.error.status === 403
            ? `The API doesn't see a manager or admin role for ${session.name}. In Clerk, set this user's public metadata to {"role": "manager"} and add "role": "{{user.public_metadata.role}}" to the session token (docs.md §11).`
            : "The API didn't accept this sign-in. Sign out and in again."
        }
        onSignOut={session.signOut}
      />
    );
  }

  return (
    <div className="flex h-full flex-col bg-paper">
      <header className="flex flex-wrap items-center gap-x-4 gap-y-2 bg-brand px-4 py-2 text-paper">
        <Mark className="h-5 w-5 shrink-0" />
        <span className="display text-[length:var(--text-step-1)]">{BRAND.name} admin</span>
        <div className="ml-auto flex items-center gap-3">
          <span className="text-[length:var(--text-step-00)] text-paper/85">
            {session.name} · {ROLE_LABELS[session.role ?? ""] ?? session.role}
          </span>
          <Button onClick={session.signOut}>Sign out</Button>
        </div>
      </header>

      {/* Tabs read as a row of folder tabs: the active one is amber and joined to the page below it. */}
      <nav className="flex gap-1 overflow-x-auto border-b-2 border-line bg-card px-2 pt-2">
        {tabs.map((t) => {
          const on = tab === t.id;
          return (
            <button
              key={t.id}
              type="button"
              aria-current={on ? "page" : undefined}
              onClick={() => setTab(t.id)}
              className={`-mb-0.5 shrink-0 border-2 border-b-0 px-4 py-2 text-[length:var(--text-step-0)] font-semibold whitespace-nowrap ${
                on ? "border-line bg-accent text-ink" : "border-transparent text-ink-soft hover:bg-paper-deep"
              }`}
            >
              {t.label}
            </button>
          );
        })}
      </nav>

      <main className="min-h-0 flex-1 overflow-y-auto">
        {tab === "menu" && <MenuPage canEdit={isManager} />}
        {tab === "modifiers" && isManager && <ModifiersPage />}
        {tab === "devices" && isManager && <DevicesPage />}
        {tab === "staff" && isManager && <StaffPage />}
        {tab === "reports" && <ReportsPage />}
      </main>
    </div>
  );
}

function NoAccess({ message, onSignOut }: { message: string; onSignOut: () => void }) {
  return (
    <div className="flex h-full flex-col items-center justify-center gap-5 bg-paper p-10 text-center">
      <p className="max-w-2xl text-[length:var(--text-step-1)] font-semibold">{message}</p>
      <Button variant="primary" size="lg" onClick={onSignOut}>
        Sign out
      </Button>
    </div>
  );
}
