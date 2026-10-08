import { useEffect, useRef, useState } from "react";
import { Button, Modal } from "../components/ui";

const IDLE_MS = 60_000; // docs §12: 60 s of inactivity …
const COUNTDOWN_S = 15; // … then "Still there?" for 15 s, then reset.

/**
 * Prevents the next customer from inheriting a stranger's cart.
 * Returns the "Still there?" modal to render (or null).
 */
export function useIdleReset(enabled: boolean, onReset: () => void) {
  const [countdown, setCountdown] = useState<number | null>(null);
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const resetRef = useRef(onReset);
  resetRef.current = onReset;

  useEffect(() => {
    if (!enabled) {
      setCountdown(null);
      return;
    }
    const arm = () => {
      if (timer.current) clearTimeout(timer.current);
      timer.current = setTimeout(() => setCountdown(COUNTDOWN_S), IDLE_MS);
    };
    const onActivity = () => {
      setCountdown(null);
      arm();
    };
    arm();
    window.addEventListener("pointerdown", onActivity);
    window.addEventListener("keydown", onActivity);
    return () => {
      if (timer.current) clearTimeout(timer.current);
      window.removeEventListener("pointerdown", onActivity);
      window.removeEventListener("keydown", onActivity);
    };
  }, [enabled]);

  useEffect(() => {
    if (countdown === null) return;
    if (countdown <= 0) {
      setCountdown(null);
      resetRef.current();
      return;
    }
    const t = setTimeout(() => setCountdown((c) => (c === null ? null : c - 1)), 1000);
    return () => clearTimeout(t);
  }, [countdown]);

  if (countdown === null) return null;
  return (
    <Modal>
      <p className="text-4xl font-black uppercase">Still there?</p>
      <p className="mt-4 text-xl">Your order will be cleared in</p>
      <p className="my-6 text-8xl font-black tabular-nums">{countdown}</p>
      <Button variant="solid" size="lg" className="w-full" onClick={() => setCountdown(null)}>
        I'm still here
      </Button>
    </Modal>
  );
}

/** Returns to the start screen after `seconds` (used on the slip and receipt screens). */
export function useAutoReturn(seconds: number, onDone: () => void) {
  const [left, setLeft] = useState(seconds);
  const doneRef = useRef(onDone);
  doneRef.current = onDone;
  useEffect(() => {
    if (left <= 0) {
      doneRef.current();
      return;
    }
    const t = setTimeout(() => setLeft((s) => s - 1), 1000);
    return () => clearTimeout(t);
  }, [left]);
  return left;
}
