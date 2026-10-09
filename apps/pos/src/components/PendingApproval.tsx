import { useEffect, useState } from "react";
import { BRAND } from "../brand";
import { Button, Mark } from "./ui";

/**
 * Signed in, but no role yet: either they signed up themselves and a manager hasn't approved them, or their access
 * was removed. Checks again every 20 s, so approval takes effect without signing out.
 */
export function PendingApproval({ name, onRefresh, onSignOut }: { name: string; onRefresh: () => Promise<void>; onSignOut: () => void }) {
  const [checking, setChecking] = useState(false);

  const check = async () => {
    setChecking(true);
    try {
      await onRefresh();
    } finally {
      setChecking(false);
    }
  };

  useEffect(() => {
    const timer = setInterval(() => void onRefresh(), 20_000);
    return () => clearInterval(timer);
  }, [onRefresh]);

  return (
    <div className="flex h-full flex-col bg-paper">
      <header className="flex items-center gap-2.5 bg-brand px-4 py-2 text-paper">
        <Mark className="h-6 w-6 shrink-0" />
        <span className="display text-[length:var(--text-step-1)]">{BRAND.name} till</span>
      </header>
      <div className="mx-auto flex max-w-2xl flex-1 flex-col justify-center gap-5 p-8 text-center">
      <h1 className="display text-[length:var(--text-step-4)]">Waiting for approval</h1>
      <p className="text-[length:var(--text-step-1)] text-ink-soft">
        Hi {name}. Your account is set up, but a manager still has to approve it and choose your role. Ask them to open
        Admin, then Staff. This screen updates on its own once you're approved.
      </p>
      <div className="flex justify-center gap-3">
        <Button variant="primary" size="lg" className="notch-sm" disabled={checking} onClick={() => void check()}>
          {checking ? "Checking…" : "Check again"}
        </Button>
        <Button size="lg" onClick={onSignOut}>
          Sign out
        </Button>
      </div>
      </div>
    </div>
  );
}
