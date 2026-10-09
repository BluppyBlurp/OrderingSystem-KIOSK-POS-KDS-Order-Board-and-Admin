import { useState, type ReactNode } from "react";
import { getTableStatus, verifyDeviceToken } from "../api";
import { BRAND } from "../brand";
import { FoodArt } from "../components/FoodArt";
import { Button, Modal, Screen } from "../components/ui";
import { cleanToken, config, savedTokenHint, saveDeviceToken } from "../config";
import { useKiosk } from "../store";

export function AttractScreen() {
  const go = useKiosk((s) => s.go);
  return (
    <button
      type="button"
      onClick={() => go("dining")}
      className="flex h-full w-full flex-col items-center justify-center gap-6 bg-paper px-6 py-10 text-center sm:gap-10"
    >
      <div className="flex w-full max-w-3xl items-end justify-center gap-2 sm:gap-6">
        <FoodArt name="Spaghetti" className="w-1/4 max-w-56" />
        <FoodArt name="1-pc Chicken Meal" className="w-2/5 max-w-80" />
        <FoodArt name="Sundae" className="w-1/4 max-w-56" />
      </div>

      <div>
        <h1 className="font-[family-name:var(--font-display)] text-[length:var(--text-step-6)] leading-[0.85] tracking-tight text-brand">
          {BRAND.name}
        </h1>
        <p className="mt-2 text-[length:var(--text-step-2)] text-ink-soft">{BRAND.tagline}</p>
      </div>

      <p className="notch animate-pulse bg-brand px-8 py-4 font-[family-name:var(--font-display)] text-[length:var(--text-step-3)] text-paper sm:px-12 sm:py-6">
        Touch to order
      </p>
    </button>
  );
}

/** Two big equal choices, stacked on a narrow screen and side by side once there is room. */
function ChoicePair({ children }: { children: ReactNode }) {
  return <div className="grid h-full grid-cols-1 content-center gap-5 p-5 sm:gap-8 sm:p-8 lg:grid-cols-2">{children}</div>;
}

const line = { stroke: "#241612", strokeWidth: 3, fill: "none", strokeLinejoin: "round" as const };

/** Tray on a table. */
function EatHereIcon() {
  return (
    <svg viewBox="0 0 64 48" className="h-14 w-20 sm:h-20 sm:w-28" aria-hidden="true">
      <ellipse cx="32" cy="22" rx="22" ry="11" {...line} fill="#fff" />
      <ellipse cx="32" cy="21" rx="12" ry="6" {...line} fill="#f5b700" />
      <path d="M32 33 L32 44 M18 44 L46 44" {...line} />
    </svg>
  );
}

/** Takeaway box with a handle. */
function TakeOutIcon() {
  return (
    <svg viewBox="0 0 64 48" className="h-14 w-20 sm:h-20 sm:w-28" aria-hidden="true">
      <path d="M16 16 L48 16 L44 44 L20 44 Z" {...line} fill="#fff" />
      <path d="M22 16 Q32 0 42 16" {...line} />
      <path d="M16 24 L48 24" stroke="#b32317" strokeWidth="5" fill="none" />
    </svg>
  );
}

export function DiningScreen() {
  const { chooseDining, reset } = useKiosk();
  return (
    <Screen title="Where will you eat?" onBack={reset}>
      <ChoicePair>
        <Button variant="choice" size="xl" onClick={() => chooseDining("DineIn")} className="notch flex-col gap-3">
          <EatHereIcon />
          Eat here
        </Button>
        <Button variant="choice" size="xl" onClick={() => chooseDining("TakeOut")} className="notch flex-col gap-3">
          <TakeOutIcon />
          Take out
        </Button>
      </ChoicePair>
    </Screen>
  );
}

export function ServiceScreen() {
  const { chooseService, go } = useKiosk();
  return (
    <Screen title="How should we bring it?" onBack={() => go("dining")}>
      <ChoicePair>
        <Button variant="choice" size="xl" onClick={() => chooseService("CounterPickup")} className="notch flex-col gap-2">
          Pick up at the counter
          <span className="text-[length:var(--text-step-0)] font-normal text-ink-soft">We'll call your number</span>
        </Button>
        <Button variant="choice" size="xl" onClick={() => chooseService("ServeToTable")} className="notch flex-col gap-2">
          Bring it to my table
          <span className="text-[length:var(--text-step-0)] font-normal text-ink-soft">Grab a number stand first</span>
        </Button>
      </ChoicePair>
    </Screen>
  );
}

export function TableScreen() {
  const { setTable, go, tableNumber } = useKiosk();
  const [digits, setDigits] = useState(tableNumber?.toString() ?? "");
  const [checking, setChecking] = useState(false);
  const [inUseWarning, setInUseWarning] = useState(false);

  const value = Number(digits);
  const valid = digits !== "" && value >= config.tableMin && value <= config.tableMax;
  const press = (d: string) => setDigits((cur) => (cur.length >= 2 ? cur : (cur + d).replace(/^0+/, "")));

  const confirm = async () => {
    setChecking(true);
    try {
      // Soft warning only: stands get reused, so never block.
      const status = await getTableStatus(value);
      if (status.inUse) setInUseWarning(true);
      else setTable(value);
    } catch {
      setTable(value); // the check is a nicety; don't block ordering on it
    } finally {
      setChecking(false);
    }
  };

  return (
    <Screen title="Your table number" onBack={() => go("service")}>
      <div className="mx-auto flex h-full max-w-lg flex-col items-center justify-center gap-5 p-5">
        <p className="text-center text-[length:var(--text-step-1)] text-ink-soft">
          Take a number stand from the counter, then key in its number.
        </p>

        <div className="notch flex h-24 w-48 items-center justify-center border-3 border-line bg-card font-[family-name:var(--font-display)] text-[length:var(--text-step-5)] tabular-nums sm:h-32 sm:w-64">
          {digits || <span className="text-ink-soft/40">––</span>}
        </div>

        <div className="grid w-full grid-cols-3 gap-2 sm:gap-3">
          {["1", "2", "3", "4", "5", "6", "7", "8", "9"].map((d) => (
            <Button key={d} size="lg" onClick={() => press(d)} className="font-[family-name:var(--font-display)]">
              {d}
            </Button>
          ))}
          <Button size="lg" onClick={() => setDigits("")}>
            Clear
          </Button>
          <Button size="lg" onClick={() => press("0")} className="font-[family-name:var(--font-display)]">
            0
          </Button>
          <Button size="lg" onClick={() => setDigits((cur) => cur.slice(0, -1))} aria-label="Delete last digit">
            ⌫
          </Button>
        </div>

        <Button variant="primary" size="lg" className="notch-sm w-full" disabled={!valid || checking} onClick={confirm}>
          {checking ? "Checking…" : "Confirm"}
        </Button>
        {digits !== "" && !valid && (
          <p className="text-[length:var(--text-step-0)] text-brand">
            Stands run from {config.tableMin} to {config.tableMax}.
          </p>
        )}
      </div>

      {inUseWarning && (
        <Modal>
          <p className="font-[family-name:var(--font-display)] text-[length:var(--text-step-3)]">
            Table {value} is already taken
          </p>
          <p className="mt-3 text-[length:var(--text-step-1)] text-ink-soft">
            Another order is using this stand. Use it anyway?
          </p>
          <div className="mt-7 grid grid-cols-2 gap-3">
            <Button size="lg" onClick={() => setInUseWarning(false)}>
              Pick another
            </Button>
            <Button variant="primary" size="lg" onClick={() => setTable(value)}>
              Use it
            </Button>
          </div>
        </Modal>
      )}
    </Screen>
  );
}

const setupMessages = {
  rejected: "That token wasn't accepted. Copy the whole value, starting with dev_, and try again.",
  unreachable: "Can't reach the server. If it was asleep it can take about a minute to wake up. Try again.",
};

/**
 * First run, or when the API rejects the saved token. The token is checked with the API before it is
 * saved, so a typo shows up here instead of after a customer touches Start.
 */
export function SetupScreen({ error }: { error?: string }) {
  const [token, setToken] = useState("");
  const [status, setStatus] = useState<"idle" | "checking" | "rejected" | "unreachable">("idle");
  const candidate = cleanToken(token);
  const hint = savedTokenHint();

  const save = async () => {
    setStatus("checking");
    const result = await verifyDeviceToken(candidate);
    if (result === "ok") {
      saveDeviceToken(candidate);
      location.reload();
    } else {
      setStatus(result);
    }
  };

  const message =
    status === "rejected" || status === "unreachable"
      ? setupMessages[status]
      : error && (hint ? `${error} The saved token ends in …${hint}.` : error);

  return (
    <Screen title="Set up this kiosk">
      <div className="mx-auto flex max-w-xl flex-col gap-5 p-6 sm:p-10">
        <p className="text-[length:var(--text-step-1)] text-ink-soft">
          Enter the device token shown when this kiosk was registered in Admin.
        </p>
        {message && (
          <p className="notch-sm border-3 border-brand bg-card p-4 text-[length:var(--text-step-0)] font-semibold text-brand">
            {message}
          </p>
        )}
        <input
          className="min-h-14 border-3 border-line bg-card px-4 text-[length:var(--text-step-1)] outline-none focus:border-brand"
          value={token}
          onChange={(e) => {
            setToken(e.target.value);
            setStatus("idle");
          }}
          placeholder="dev_…"
          autoFocus
        />
        <Button
          variant="primary"
          size="lg"
          className="notch-sm"
          disabled={!candidate.startsWith("dev_") || candidate.length < 36 || status === "checking"}
          onClick={save}
        >
          {status === "checking" ? "Checking…" : "Save and start"}
        </Button>
      </div>
    </Screen>
  );
}
