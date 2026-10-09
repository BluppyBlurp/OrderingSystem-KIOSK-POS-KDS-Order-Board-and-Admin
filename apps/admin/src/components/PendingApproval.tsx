import { useEffect, useState } from "react";
import { Button } from "./ui";

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
    <div className="flex h-full flex-col items-center justify-center gap-5 bg-paper p-10 text-center">
      <h1 className="display text-[length:var(--text-step-4)]">Waiting for approval</h1>
      <p className="max-w-2xl text-[length:var(--text-step-1)] text-ink-soft">
        Hi {name}. Your account is set up, but a manager still has to approve it and choose your role. Ask them to open
        Admin → Staff. This screen updates on its own once you're approved.
      </p>
      <div className="flex gap-4">
        <Button variant="primary" size="lg" disabled={checking} onClick={() => void check()}>
          {checking ? "Checking…" : "Check again"}
        </Button>
        <Button size="lg" onClick={onSignOut}>
          Sign out
        </Button>
      </div>
    </div>
  );
}
