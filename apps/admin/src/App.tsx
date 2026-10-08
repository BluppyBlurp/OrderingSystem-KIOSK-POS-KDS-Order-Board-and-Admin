import { ApiError } from "@kiosk/api-client";
import { useState } from "react";
import { setTokenSource, useCategories } from "./api";
import { useSession } from "./auth";
import { Button } from "./components/ui";
import { ALLOWED_ROLES } from "./config";
import { DevicesPage } from "./pages/DevicesPage";
import { MenuPage } from "./pages/MenuPage";
import { ModifiersPage } from "./pages/ModifiersPage";
import { ReportsPage } from "./pages/ReportsPage";

const TABS = [
  { id: "menu", label: "Menu" },
  { id: "modifiers", label: "Options" },
  { id: "devices", label: "Devices" },
  { id: "reports", label: "Reports" },
] as const;

type Tab = (typeof TABS)[number]["id"];

export function App() {
  const session = useSession();
  setTokenSource(session.getToken); // before any query below runs

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
    <div className="flex h-full flex-col">
      <header className="flex min-h-16 items-center justify-between gap-4 border-b-4 border-black px-4">
        <div className="flex items-center gap-6">
          <h1 className="text-2xl font-black uppercase">Admin</h1>
          <nav className="flex gap-2">
            {TABS.map((t) => (
              <Button key={t.id} variant={tab === t.id ? "solid" : "outline"} onClick={() => setTab(t.id)}>
                {t.label}
              </Button>
            ))}
          </nav>
        </div>
        <div className="flex items-center gap-4">
          <span className="font-bold">{session.name}</span>
          <Button onClick={session.signOut}>Sign out</Button>
        </div>
      </header>
      <main className="min-h-0 flex-1 overflow-y-auto">
        {tab === "menu" && <MenuPage />}
        {tab === "modifiers" && <ModifiersPage />}
        {tab === "devices" && <DevicesPage />}
        {tab === "reports" && <ReportsPage />}
      </main>
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
